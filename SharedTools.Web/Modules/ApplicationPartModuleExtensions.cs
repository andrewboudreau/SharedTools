using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

using System.Reflection;

namespace SharedTools.Web.Modules;

/// <summary>
/// Extension methods for adding and configuring ApplicationPart-based modules.
/// </summary>
public static class ApplicationPartModuleExtensions
{
    private static NuGet.Common.ILogger NuGetLogger { get; set; } = NuGet.Common.NullLogger.Instance;

    internal class ModuleRegistry
    {
        public List<IApplicationPartModule> Modules { get; } = [];
        public List<(string ModuleName, IFileProvider FileProvider)> StaticFileProviders { get; } = [];
        public List<ModuleAssemblyLoadContext> AssemblyLoadContexts { get; } = [];
    }

    /// <summary>
    /// Discovers modules from NuGet packages, resolves their dependencies, downloads them,
    /// and integrates them into the application using ApplicationParts.
    /// </summary>
    public static async Task<WebApplicationBuilder> AddApplicationPartModules(
        this WebApplicationBuilder builder,
        IEnumerable<string> packageIds,
        IEnumerable<string>? nuGetRepositoryUrls = null,
        string? specificPackageVersion = null)
    {
        if (builder.Environment is not IWebHostEnvironment env)
        {
            throw new InvalidOperationException("AddApplicationPartModules requires an IWebHostEnvironment.");
        }

        // Get or create the module registry
        var moduleRegistry = GetOrCreateModuleRegistry(builder.Services);

        // Get the ApplicationPartManager
        var mvcBuilder = builder.Services.AddRazorPages();
        var partManager = mvcBuilder.PartManager;

        var logger = CreateTemporaryLogger();
        var processedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string tempCachePath = Path.Combine(Path.GetTempPath(), "SharedTools_ApplicationPartModulesCache");
        Directory.CreateDirectory(tempCachePath);
        logger?.LogInformation("Using temporary cache directory: {BaseTempPath}", tempCachePath);

        var repositories = CreateSourceRepositories(nuGetRepositoryUrls, logger);
        var nuGetCacheContext = new SourceCacheContext { NoCache = true };
        var nuGetSettings = Settings.LoadDefaultSettings(root: null);

        var targetFramework = new NuGetFramework(".NETCoreApp", new Version(10, 0));
        logger?.LogInformation("Resolving dependencies for target framework: {Framework}", targetFramework.DotNetFrameworkName);

        foreach (var packageId in packageIds)
        {
            logger?.LogInformation("--- Processing root package: {PackageId} ---", packageId);
            try
            {
                var allPackagesToInstall = await ResolveDependencyGraphAsync(
                    packageId, specificPackageVersion, targetFramework,
                    repositories, nuGetCacheContext, logger);

                if (allPackagesToInstall == null || !allPackagesToInstall.Any())
                {
                    logger?.LogError("Could not resolve dependency graph for {PackageId}.", packageId);
                    continue;
                }

                var rootPackageIdentity = allPackagesToInstall.First(p =>
                    p.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));

                string flatExtractionPath = Path.Combine(tempCachePath,
                    $"{rootPackageIdentity.Id}.{rootPackageIdentity.Version}_flat");

                if (Directory.Exists(flatExtractionPath))
                {
                    Directory.Delete(flatExtractionPath, recursive: true);
                }
                Directory.CreateDirectory(flatExtractionPath);

                logger?.LogInformation("Extracting all package dependencies to flat directory: {Path}",
                    flatExtractionPath);

                // Extract all packages to flat directory
                await ExtractPackagesToFlatDirectory(
                    allPackagesToInstall, flatExtractionPath, targetFramework,
                    repositories, nuGetSettings, nuGetCacheContext, logger);

                // Load the main assembly
                string mainAssemblyPath = Path.Combine(flatExtractionPath, $"{rootPackageIdentity.Id}.dll");
                if (!File.Exists(mainAssemblyPath))
                {
                    logger?.LogError("Could not find main assembly {AssemblyPath} after extraction.",
                        mainAssemblyPath);
                    continue;
                }

                Assembly assembly;
                ModuleAssemblyLoadContext loadContext;
                try
                {
                    logger?.LogInformation("Loading main assembly from: {AssemblyPath}", mainAssemblyPath);
                    loadContext = new ModuleAssemblyLoadContext(mainAssemblyPath);
                    assembly = loadContext.LoadFromAssemblyPath(mainAssemblyPath);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Failed to load assembly from {MainAssemblyPath}", mainAssemblyPath);
                    continue;
                }

                if (processedAssemblies.Contains(assembly.FullName!))
                {
                    logger?.LogInformation("Assembly {AssemblyName} was already processed. Skipping.",
                        assembly.FullName);
                    continue;
                }

                ProcessAssemblyForModules(
                    assembly, rootPackageIdentity.Id, flatExtractionPath,
                    builder, partManager, moduleRegistry, logger, env, loadContext);

                // Store the load context to prevent it from being garbage collected
                moduleRegistry.AssemblyLoadContexts.Add(loadContext);

                processedAssemblies.Add(assembly.FullName!);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "An unhandled error occurred while processing package {PackageId}",
                    packageId);
            }
        }

        // Register the module registry as a singleton
        builder.Services.AddSingleton<IReadOnlyCollection<IApplicationPartModule>>(
            moduleRegistry.Modules.AsReadOnly());

        logger?.LogInformation("Registered {ModuleCount} application part modules in total.",
            moduleRegistry.Modules.Count);

        return builder;
    }

    private static void ProcessAssemblyForModules(
        Assembly assembly,
        string moduleName,
        string extractionPath,
        WebApplicationBuilder builder,
        ApplicationPartManager partManager,
        ModuleRegistry moduleRegistry,
        ILogger? logger,
        IWebHostEnvironment env,
        ModuleAssemblyLoadContext loadContext)
    {
        logger?.LogInformation("Processing assembly {AssemblyName} for application part modules.",
            assembly.FullName ?? "UnknownAssembly");

        // Find all types implementing IApplicationPartModule
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Some types couldn't be loaded, but we can still work with the ones that did load
            types = ex.Types.Where(t => t != null).ToArray()!;
            logger?.LogWarning("Some types could not be loaded from {AssemblyName}. Continuing with {LoadedTypeCount} types.",
                assembly.FullName ?? "UnknownAssembly", types.Length);
        }

        var moduleTypes = types
            .Where(t => t != null && typeof(IApplicationPartModule).IsAssignableFrom(t) &&
                       !t.IsInterface && !t.IsAbstract);

        logger?.LogInformation("Found {ModuleTypeCount} IApplicationPartModule implementations in {AssemblyName}",
            moduleTypes.Count(), assembly.FullName ?? "UnknownAssembly");

        foreach (var moduleType in moduleTypes)
        {
            try
            {
                // Create module instance
                var module = (IApplicationPartModule)Activator.CreateInstance(moduleType)!;

                // Configure services
                module.ConfigureServices(builder.Services);

                // Create and add ModuleApplicationPart
                var modulePart = new ModuleApplicationPart(assembly, module);
                partManager.ApplicationParts.Add(modulePart);

                // Also add as AssemblyPart for Razor Pages discovery
                var assemblyPart = new AssemblyPart(assembly);
                if (!partManager.ApplicationParts.Any(p => p is AssemblyPart ap && ap.Assembly == assembly))
                {
                    partManager.ApplicationParts.Add(assemblyPart);
                }

                // Add CompiledRazorAssemblyPart for the main assembly (views are compiled into main assembly in .NET 6+)
                var compiledRazorPart = new CompiledRazorAssemblyPart(assembly);
                partManager.ApplicationParts.Add(compiledRazorPart);
                logger?.LogInformation("Added CompiledRazorAssemblyPart for main assembly {AssemblyName} of module {ModuleName}", 
                    assembly.GetName().Name, module.Name);               

                // Let the module configure additional application parts if needed
                module.ConfigureApplicationParts(partManager);

                // Register the module
                moduleRegistry.Modules.Add(module);

                logger?.LogInformation(
                    "Initialized module {ModuleTypeName} from {AssemblyName} as ApplicationPart",
                    moduleType.FullName, assembly.FullName ?? "UnknownAssembly");

                // Handle static assets
                RegisterModuleStaticAssets(assembly, module.Name, moduleRegistry, logger);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex,
                    "Failed to create instance or configure module {ModuleTypeName} from assembly {AssemblyName}\r\n{Error}",
                    moduleType.FullName, assembly.FullName ?? "UnknownAssembly", ex.Message);
            }
        }

        // Also add regular ApplicationParts for assemblies without modules (for compatibility)
        if (!moduleTypes.Any())
        {
            // Add AssemblyPart for Razor Pages discovery
            if (!partManager.ApplicationParts.Any(part => part is AssemblyPart ap && ap.Assembly == assembly))
            {
                partManager.ApplicationParts.Add(new AssemblyPart(assembly));
                logger?.LogTrace("Added AssemblyPart for {AssemblyName}", assembly.FullName ?? "UnknownAssembly");
            }

            // Add CompiledRazorAssemblyPart for compiled Razor views/pages
            if (!partManager.ApplicationParts.Any(part => part is CompiledRazorAssemblyPart crap &&
                crap.Assembly == assembly))
            {
                partManager.ApplicationParts.Add(new CompiledRazorAssemblyPart(assembly));
                logger?.LogTrace("Added CompiledRazorAssemblyPart for {AssemblyName}",
                    assembly.FullName ?? "UnknownAssembly");
            }
        }
    }

    private static void RegisterModuleStaticAssets(
        Assembly assembly,
        string moduleName,
        ModuleRegistry moduleRegistry,
        ILogger? logger)
    {
        // Check for embedded wwwroot resources
        var manifestResourceNames = assembly.GetManifestResourceNames();
        logger?.LogInformation("We found {ResourceCount} manifest resources in assembly {AssemblyName} {items}",
            manifestResourceNames.Length, assembly.FullName ?? "UnknownAssembly", 
            string.Join("\r\n\t", manifestResourceNames)
        );

        if (manifestResourceNames.Any(r => r.Contains("wwwroot.", StringComparison.OrdinalIgnoreCase)))
        {
            // Use EmbeddedFileProvider instead of ManifestEmbeddedFileProvider
            // The namespace prefix for embedded resources is the assembly name + ".wwwroot"
            var assemblyName = assembly.GetName().Name ?? "Unknown";
            var embeddedProvider = new EmbeddedFileProvider(assembly, $"{assemblyName}.wwwroot");
            moduleRegistry.StaticFileProviders.Add((moduleName, embeddedProvider));
            logger?.LogInformation(
                "Registered EmbeddedFileProvider for module {ModuleName} from assembly {AssemblyName} with base namespace {BaseNamespace}",
                moduleName, assembly.FullName ?? "UnknownAssembly", $"{assemblyName}.wwwroot");
        }
    }

    /// <summary>
    /// Configures the application to use the loaded application part modules.
    /// </summary>
    public static WebApplication UseApplicationPartModules(this WebApplication app)
    {
        var loggerFactory = app.Services.GetService<ILoggerFactory>();
        var logger = loggerFactory?.CreateLogger(typeof(ApplicationPartModuleExtensions));

        // Configure static files for each module
        var moduleRegistry = app.Services.GetService<ModuleRegistry>();
        if (moduleRegistry != null)
        {
            logger?.LogInformation("Configuring static files for {ProviderCount} providers",
                moduleRegistry.StaticFileProviders.Count);

            foreach (var (moduleName, fileProvider) in moduleRegistry.StaticFileProviders)
            {
                var requestPath = $"/_content/{moduleName}";
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = fileProvider,
                    RequestPath = requestPath
                });
                logger?.LogInformation("Configured static files for module {ModuleName} at path {RequestPath}",
                    moduleName, requestPath);
            }
        }

        // Configure each module
        var modules = app.Services.GetRequiredService<IReadOnlyCollection<IApplicationPartModule>>();
        logger?.LogInformation("Configuring {ModuleCount} application part modules.", modules.Count);

        foreach (var module in modules)
        {
            try
            {
                logger?.LogTrace("Configuring module {ModuleName}", module.Name);
                module.Configure(app);
                logger?.LogInformation("Successfully configured module {ModuleName}", module.Name);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error configuring module {ModuleName}", module.Name);
            }
        }

        return app;
    }

    #region Helper Methods

    private static ModuleRegistry GetOrCreateModuleRegistry(IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(ModuleRegistry));
        if (descriptor?.ImplementationInstance is ModuleRegistry registry)
        {
            return registry;
        }

        var newRegistry = new ModuleRegistry();
        services.AddSingleton(newRegistry);
        return newRegistry;
    }

    private static async Task ExtractPackagesToFlatDirectory(
        IEnumerable<PackageIdentity> packages,
        string flatExtractionPath,
        NuGetFramework targetFramework,
        IEnumerable<SourceRepository> repositories,
        ISettings nuGetSettings,
        SourceCacheContext cacheContext,
        ILogger? logger)
    {
        foreach (var packageIdentity in packages)
        {
            var (downloadResult, _) = await FindAndDownloadPackageAsync(
                packageIdentity.Id, packageIdentity.Version.ToNormalizedString(),
                repositories, nuGetSettings, cacheContext, logger);

            if (downloadResult == null)
            {
                logger?.LogWarning("Failed to download dependency package {PackageId}", packageIdentity);
                continue;
            }

            using (var reader = new PackageArchiveReader(downloadResult.PackageStream))
            {
                var libItems = await reader.GetLibItemsAsync(CancellationToken.None);
                var nearestFramework = NuGetFrameworkUtility.GetNearest(libItems, targetFramework,
                    f => f.TargetFramework);

                if (nearestFramework != null)
                {
                    foreach (var item in nearestFramework.Items)
                    {
                        if (item.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            reader.ExtractFile(item, Path.Combine(flatExtractionPath,
                                Path.GetFileName(item)), NuGetLogger);
                        }
                    }
                }

            }

            downloadResult.Dispose();
        }
    }


    // The remaining helper methods (ResolveDependencyGraphAsync, FindAndDownloadPackageAsync, 
    // CreateSourceRepositories, CreateTemporaryLogger) remain the same as in WebModuleExtensions
    // I'll include them here for completeness...

    private static async Task<IEnumerable<PackageIdentity>?> ResolveDependencyGraphAsync(
        string packageId,
        string? version,
        NuGetFramework framework,
        IEnumerable<SourceRepository> repositories,
        SourceCacheContext cacheContext,
        ILogger? logger)
    {
        var resolvedPackages = new Dictionary<string, PackageIdentity>(StringComparer.OrdinalIgnoreCase);
        var packagesToProcess = new Queue<(PackageIdentity package, SourceRepository repository)>();

        async Task<(NuGetVersion? version, SourceRepository? repo)> FindPackageInPrioritizedReposAsync(
            string pkgId, VersionRange? range = null)
        {
            foreach (var repo in repositories)
            {
                try
                {
                    var findResource = await repo.GetResourceAsync<FindPackageByIdResource>();
                    var versions = await findResource.GetAllVersionsAsync(pkgId, cacheContext,
                        NuGetLogger, CancellationToken.None);

                    if (versions != null && versions.Any())
                    {
                        logger?.LogInformation(
                            "Package '{packageId}' found in repository: {PackageSourceName}. Using this source.",
                            pkgId, repo.PackageSource.Name);

                        NuGetVersion? bestVersion;
                        if (range != null)
                        {
                            bestVersion = range.FindBestMatch(versions);
                        }
                        else
                        {
                            bestVersion = versions.Where(v => !v.IsPrerelease).Max() ?? versions.Max();
                        }

                        return (bestVersion, repo);
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning("Could not get versions for {packageId} from {PackageSourceName}: {message}",
                        pkgId, repo.PackageSource.Name, ex.Message);
                }
            }

            return (null, null);
        }

        // Find initial package
        NuGetVersion? initialVersion;
        SourceRepository? initialRepo;

        if (!string.IsNullOrEmpty(version) && NuGetVersion.TryParse(version, out var parsedVersion))
        {
            (initialVersion, initialRepo) = await FindPackageInPrioritizedReposAsync(packageId,
                new VersionRange(parsedVersion, true, parsedVersion, true));
        }
        else
        {
            (initialVersion, initialRepo) = await FindPackageInPrioritizedReposAsync(packageId);
        }

        if (initialVersion == null || initialRepo == null)
        {
            logger?.LogError("Could not find root package {packageId} in any repository.", packageId);
            return null;
        }

        var initialIdentity = new PackageIdentity(packageId, initialVersion);
        packagesToProcess.Enqueue((initialIdentity, initialRepo));
        resolvedPackages.Add(packageId, initialIdentity);

        while (packagesToProcess.Count > 0)
        {
            var (currentPackage, sourceRepo) = packagesToProcess.Dequeue();
            logger?.LogTrace("Analyzing dependencies for {currentPackage} from {PackageSourceName}",
                currentPackage, sourceRepo.PackageSource.Name);

            var depInfoResource = await sourceRepo.GetResourceAsync<DependencyInfoResource>();
            var dependencyInfo = await depInfoResource.ResolvePackage(currentPackage, framework,
                cacheContext, NuGetLogger, CancellationToken.None);

            if (dependencyInfo == null)
            {
                logger?.LogWarning(
                    "Could not find dependency info for package {currentPackage}. It might be a package without dependencies.",
                    currentPackage);
                continue;
            }

            foreach (var dependency in dependencyInfo.Dependencies)
            {
                var (bestVersion, foundInRepo) = await FindPackageInPrioritizedReposAsync(
                    dependency.Id, dependency.VersionRange);

                if (bestVersion == null || foundInRepo == null)
                {
                    logger?.LogWarning(
                        "Could not find a compatible version for dependency {Id} with range {VersionRange} in any repository.",
                        dependency.Id, dependency.VersionRange);
                    continue;
                }

                if (!resolvedPackages.TryGetValue(dependency.Id, out var existing) || bestVersion > existing.Version)
                {
                    var newIdentity = new PackageIdentity(dependency.Id, bestVersion);
                    if (existing == null)
                    {
                        packagesToProcess.Enqueue((newIdentity, foundInRepo));
                    }
                    resolvedPackages[dependency.Id] = newIdentity;
                    logger?.LogTrace("  -> Resolved dependency: {Id} {Version} from {PackageSourceName}",
                        newIdentity.Id, newIdentity.Version, foundInRepo.PackageSource.Name);
                }
            }
        }

        return resolvedPackages.Values;
    }

    private static async Task<(DownloadResourceResult? downloadResult, PackageIdentity? packageIdentity)>
        FindAndDownloadPackageAsync(
            string packageId,
            string? specificVersion,
            IEnumerable<SourceRepository> repositories,
            ISettings nugetSettings,
            SourceCacheContext cacheContext,
            ILogger? logger)
    {
        PackageIdentity? packageIdentity = null;
        DownloadResourceResult? downloadResult = null;

        foreach (var repo in repositories)
        {
            logger?.LogTrace("Searching for {PackageId} in {Repository}", packageId, repo.PackageSource.Name);
            var findResource = await repo.GetResourceAsync<FindPackageByIdResource>();
            if (findResource == null) continue;

            var allVersions = await findResource.GetAllVersionsAsync(packageId, cacheContext,
                NuGetLogger, CancellationToken.None);
            if (allVersions == null || !allVersions.Any()) continue;

            NuGetVersion? versionToDownload = null;
            if (!string.IsNullOrEmpty(specificVersion) && NuGetVersion.TryParse(specificVersion, out var parsedVersion))
            {
                versionToDownload = allVersions.FirstOrDefault(v => v.Equals(parsedVersion));
            }
            versionToDownload ??= allVersions.Where(v => !v.IsPrerelease).Max() ?? allVersions.Max();

            if (versionToDownload == null) continue;

            packageIdentity = new PackageIdentity(packageId, versionToDownload);
            var downloadResource = await repo.GetResourceAsync<DownloadResource>();
            if (downloadResource == null) continue;

            var globalPackagesFolder = SettingsUtility.GetGlobalPackagesFolder(nugetSettings);
            var downloadContext = new PackageDownloadContext(cacheContext, Path.GetTempPath(),
                cacheContext.DirectDownload);

            logger?.LogTrace("Downloading {PackageId} version {PackageVersion} from {Repository}",
                packageId, versionToDownload, repo.PackageSource.Name);
            downloadResult = await downloadResource.GetDownloadResourceResultAsync(packageIdentity,
                downloadContext, globalPackagesFolder, NuGetLogger, CancellationToken.None);

            if (downloadResult?.Status == DownloadResourceResultStatus.Available)
            {
                logger?.LogTrace("Successfully downloaded package {PackageId}.", packageId);
                return (downloadResult, packageIdentity);
            }
        }

        logger?.LogError("Failed to find or download package {PackageId} from any repository.", packageId);
        return (null, null);
    }

    private static ILogger? CreateTemporaryLogger()
    {
        try
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.ClearProviders();
                builder.AddConsole(options =>
                {
                    options.FormatterName = "terse";
                })
                .AddConsoleFormatter<Logging.TerseConsoleFormatter, Logging.TerseConsoleFormatterOptions>()
                .SetMinimumLevel(LogLevel.Debug);

                builder.AddDebug();
            });
            return loggerFactory.CreateLogger(typeof(ApplicationPartModuleExtensions));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Failed to create temporary logger: {ex.Message}");
            return null;
        }
    }

    private static List<SourceRepository> CreateSourceRepositories(IEnumerable<string>? repositoryUrls, ILogger? logger)
    {
        var sources = new List<PackageSource>();
        ISettings settings;

        if (repositoryUrls != null && repositoryUrls.Any())
        {
            logger?.LogInformation("Using explicit repository URLs provided in configuration.");
            sources.AddRange(repositoryUrls.Select(url => new PackageSource(url)));
            settings = Settings.LoadDefaultSettings(root: null);
        }
        else
        {
            logger?.LogInformation("No explicit URLs provided. Attempting to discover sources from nuget.config files...");
            settings = Settings.LoadDefaultSettings(root: Directory.GetCurrentDirectory());
            var discoveredSources = new PackageSourceProvider(settings).LoadPackageSources();

            if (discoveredSources.Any())
            {
                logger?.LogInformation($"Discovered {discoveredSources.Count()} sources from nuget.config.");
                sources.AddRange(discoveredSources);
            }
            else
            {
                logger?.LogInformation("No sources found in nuget.config. Falling back to default nuget.org source.");
                sources.Add(new PackageSource("https://api.nuget.org/v3/index.json", "nuget.org"));
            }
        }

        var sourceProvider = new PackageSourceProvider(settings, sources);
        var sourceRepoProvider = new SourceRepositoryProvider(sourceProvider, Repository.Provider.GetCoreV3());

        return [.. sourceRepoProvider.GetRepositories()];
    }

    #endregion
}