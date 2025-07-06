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

using SharedTestTools.Web;

using System.Reflection;

namespace SharedTools.Web.Modules;

public static class WebModuleExtensions
{
    private static NuGet.Common.ILogger NuGetLogger { get; set; } = NuGet.Common.NullLogger.Instance;


    /// <summary>
    /// Discovers WebModules from NuGet packages, resolves their dependencies, downloads them, 
    /// extracts their contents, loads them, and integrates them into the application.
    /// </summary>
    public static async Task<WebApplicationBuilder> AddWebModules(
        this WebApplicationBuilder builder,
        IEnumerable<string> packageIds,
        IEnumerable<string>? nuGetRepositoryUrls = null,
        string? specificPackageVersion = null)
    {
        if (builder.Environment is not IWebHostEnvironment env)
        {
            throw new InvalidOperationException("AddWebModules requires an IWebHostEnvironment.");
        }

        var partManager = builder.Services.AddRazorPages().PartManager;
        var webModuleInstances = GetOrCreateWebModuleList(builder.Services);
        var logger = CreateTemporaryLogger();
        NuGetLogger = logger != null ? new NugetLoggerAdapter(logger) : NuGetLogger;

        var processedAssemblies = new HashSet<string>(webModuleInstances.Select(m => m.GetType().Assembly.FullName).Where(n => n != null)!);

        string tempCachePath = Path.Combine(Path.GetTempPath(), "SharedTools_NuGetWebModulesCache");
        Directory.CreateDirectory(tempCachePath);
        logger?.LogInformation("Using temporary cache directory: {BaseTempPath}", tempCachePath);

        var repositories = CreateSourceRepositories(nuGetRepositoryUrls, logger);
        var nuGetCacheContext = new SourceCacheContext { NoCache = true };
        var nuGetSettings = Settings.LoadDefaultSettings(root: null);

        //todo: this could be parsed so it's not hardcoded framework version
        var targetFramework = new NuGetFramework(".NETCoreApp", new Version(10, 0));
        logger?.LogInformation("Resolving dependencies for target framework: {Framework}", targetFramework.DotNetFrameworkName);

        foreach (var packageId in packageIds)
        {
            logger?.LogInformation("--- Processing root package: {PackageId} ---", packageId);
            try
            {
                var allPackagesToInstall = await ResolveDependencyGraphAsync(packageId, specificPackageVersion, targetFramework, repositories, nuGetCacheContext, logger);
                if (allPackagesToInstall == null || !allPackagesToInstall.Any())
                {
                    logger?.LogError("Could not resolve dependency graph for {PackageId}.", packageId);
                    continue;
                }

                var rootPackageIdentity = allPackagesToInstall.First(p => p.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));
                string flatExtractionPath = Path.Combine(tempCachePath, $"{rootPackageIdentity.Id}.{rootPackageIdentity.Version}_flat");
                if (Directory.Exists(flatExtractionPath))
                {
                    Directory.Delete(flatExtractionPath, recursive: true);
                }
                Directory.CreateDirectory(flatExtractionPath);

                logger?.LogInformation("Extracting all package dependencies to flat directory: {Path}", flatExtractionPath);
                foreach (var packageIdentity in allPackagesToInstall)
                {
                    var (downloadResult, _) = await FindAndDownloadPackageAsync(packageIdentity.Id, packageIdentity.Version.ToNormalizedString(), repositories, nuGetSettings, nuGetCacheContext, logger);
                    if (downloadResult == null)
                    {
                        logger?.LogWarning("Failed to download dependency package {PackageId}, which may cause runtime errors.", packageIdentity);
                        continue;
                    }

                    // Extract DLLs from this package into the single flat directory
                    using (var reader = new PackageArchiveReader(downloadResult.PackageStream))
                    {
                        var libItems = await reader.GetLibItemsAsync(CancellationToken.None);

                        // Find the library folder that is most compatible with our target framework.
                        var nearestFramework = NuGetFrameworkUtility.GetNearest(libItems, targetFramework, f => f.TargetFramework);

                        if (nearestFramework != null)
                        {
                            foreach (var item in nearestFramework.Items)
                            {
                                // We only care about DLLs for the load context.
                                if (item.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Extract the file directly into our flat path.
                                    reader.ExtractFile(item, Path.Combine(flatExtractionPath, Path.GetFileName(item)), NuGetLogger);
                                }
                            }
                        }
                    }
                    downloadResult.Dispose();
                }

                // Now, the main assembly path is inside the flat directory.
                string mainAssemblyPath = Path.Combine(flatExtractionPath, $"{rootPackageIdentity.Id}.dll");
                if (!File.Exists(mainAssemblyPath))
                {
                    logger?.LogError("Could not find main assembly {AssemblyPath} after flat extraction.", mainAssemblyPath);
                    continue;
                }

                Assembly assembly;
                try
                {
                    logger?.LogInformation("Loading main assembly from: {AssemblyPath}", mainAssemblyPath);
                    // The AssemblyDependencyResolver will now correctly find all dependencies
                    // because they are in the same directory as the main assembly.
                    var loadContext = new WebModuleLoadContext(mainAssemblyPath);
                    assembly = loadContext.LoadFromAssemblyPath(mainAssemblyPath);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Failed to load assembly from {MainAssemblyPath}", mainAssemblyPath);
                    continue;
                }

                if (processedAssemblies.Contains(assembly.FullName!))
                {
                    logger?.LogInformation("Assembly {AssemblyName} was already processed. Skipping.", assembly.FullName);
                    continue;
                }

                ProcessAssemblyForWebModules(assembly, builder, partManager, webModuleInstances, logger, env);
                processedAssemblies.Add(assembly.FullName!);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "An unhandled error occurred while processing package {PackageId}", packageId);
            }
        }

        builder.Services.AddSingleton<IReadOnlyCollection<IWebModule>>(webModuleInstances.AsReadOnly());
        logger?.LogInformation("Registered {WebModuleCount} web modules in total.", webModuleInstances.Count);
        return builder;
    }

    private static void ProcessAssemblyForWebModules(
        Assembly assembly,
        WebApplicationBuilder builder,
        ApplicationPartManager partManager,
        List<IWebModule> webModuleInstances,
        ILogger? logger,
        IWebHostEnvironment env)
    {
        logger?.LogInformation("Processing assembly {AssemblyName} for web modules.", assembly.FullName ?? "UnknownAssembly");

        // Add the assembly for controllers/pages discovery
        if (!partManager.ApplicationParts.Any(part => part is AssemblyPart ap && ap.Assembly == assembly))
        {
            partManager.ApplicationParts.Add(new AssemblyPart(assembly));
            logger?.LogTrace("Added AssemblyPart for {AssemblyName} (for controllers/pages)", assembly.FullName ?? "UnknownAssembly");
        }
        else
        {
            logger?.LogTrace("AssemblyPart for {AssemblyName} already exists.", assembly.FullName ?? "UnknownAssembly");
        }

        // Add the assembly for compiled Razor views discovery
        if (!partManager.ApplicationParts.Any(part => part is CompiledRazorAssemblyPart crap && crap.Assembly == assembly))
        {
            partManager.ApplicationParts.Add(new CompiledRazorAssemblyPart(assembly));
            logger?.LogTrace("Added CompiledRazorAssemblyPart for {AssemblyName} (for compiled views)", assembly.FullName ?? "UnknownAssembly");
        }
        else
        {
            logger?.LogTrace("CompiledRazorAssemblyPart for {AssemblyName} already exists.", assembly.FullName ?? "UnknownAssembly");
        }

        var manifestResourceNames = assembly.GetManifestResourceNames();
        if (manifestResourceNames.Any(r => r.StartsWith("wwwroot", StringComparison.OrdinalIgnoreCase)))
        {
            var embeddedProvider = new ManifestEmbeddedFileProvider(assembly, "wwwroot");
            env.WebRootFileProvider = new CompositeFileProvider(env.WebRootFileProvider, embeddedProvider);
            logger?.LogTrace("Configured ManifestEmbeddedFileProvider for 'wwwroot' from assembly {AssemblyName}", assembly.FullName ?? "UnknownAssembly");
        }
        else
        {
            logger?.LogTrace("No 'wwwroot' embedded resources found in assembly {AssemblyName}", assembly.FullName ?? "UnknownAssembly");
        }

        var allTypesInAssembly = assembly.GetTypes();
        logger?.LogTrace("Inspecting {TypeCount} types in assembly {AssemblyName} for IWebModule implementations.", allTypesInAssembly.Length, assembly.FullName ?? "UnknownAssembly");

        foreach (var typeInAssembly in allTypesInAssembly)
        {
            bool isAssignable = typeof(IWebModule).IsAssignableFrom(typeInAssembly);
            bool isInterface = typeInAssembly.IsInterface;
            bool isAbstract = typeInAssembly.IsAbstract;
            logger?.LogTrace("Type: {TypeName}, Implements IWebModule: {IsAssignable}, IsInterface: {IsInterface}, IsAbstract: {IsAbstract}, AssemblyQualifiedName: {AssemblyQualifiedName}",
                            typeInAssembly.FullName, isAssignable, isInterface, isAbstract, typeInAssembly.AssemblyQualifiedName);

            if (isAssignable && typeInAssembly.FullName != typeof(IWebModule).FullName)
            {
                var interfaces = typeInAssembly.GetInterfaces().Select(i => i.AssemblyQualifiedName).ToArray();
                logger?.LogTrace("  - Type {TypeName} implements interfaces: {Interfaces}", typeInAssembly.FullName, string.Join(", ", interfaces));
                if (!interfaces.Contains(typeof(IWebModule).AssemblyQualifiedName))
                {
                    logger?.LogWarning("  - Type {TypeName} is assignable to IWebModule but its GetInterfaces() does not include the host's IWebModule AssemblyQualifiedName. Host IWebModule: {HostIWebModuleAQN}", typeInAssembly.FullName, typeof(IWebModule).AssemblyQualifiedName);
                }
            }
        }

        var webModuleTypes = allTypesInAssembly
            .Where(t => typeof(IWebModule).IsAssignableFrom(t)
                        && !t.IsInterface
                        && !t.IsAbstract);

        logger?.LogInformation("Found {WebModuleTypeCount} concrete IWebModule implementations in {AssemblyName}", webModuleTypes.Count(), assembly.FullName ?? "UnknownAssembly");
        foreach (var type in webModuleTypes)
        {
            try
            {
                var webModule = (IWebModule)Activator.CreateInstance(type)!;
                webModule.ConfigureBuilder(builder);
                webModuleInstances.Add(webModule);
                logger?.LogInformation("Initialized and configured services for web module {WebModuleTypeName} from {AssemblyName}", type.FullName, assembly.FullName ?? "UnknownAssembly");
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to create instance or configure services for web module {WebModuleTypeName} from assembly {AssemblyName}", type.FullName, assembly.FullName ?? "UnknownAssembly");
            }
        }
    }

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

        // --- New Helper Function with Prioritized Search ---
        async Task<(NuGetVersion? version, SourceRepository? repo)> FindPackageInPrioritizedReposAsync(string pkgId, VersionRange? range = null)
        {
            // Search repositories in the configured order.
            foreach (var repo in repositories)
            {
                try
                {
                    var findResource = await repo.GetResourceAsync<FindPackageByIdResource>();
                    var versions = await findResource.GetAllVersionsAsync(pkgId, cacheContext, NuGetLogger, CancellationToken.None);

                    // If ANY versions are found in this repo, we stop searching further.
                    // This repository wins.
                    if (versions != null && versions.Any())
                    {
                        logger?.LogInformation("Package '{packageId}' found in prioritized repository: {PackageSourceName}. Using this source exclusively for this package.", pkgId, repo.PackageSource.Name);

                        NuGetVersion? bestVersion;
                        if (range != null)
                        {
                            bestVersion = range.FindBestMatch(versions);
                        }
                        else
                        {
                            bestVersion = versions.Where(v => !v.IsPrerelease).Max() ?? versions.Max();
                        }

                        // Return the best version from THIS repo and the repo itself.
                        return (bestVersion, repo);
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning("Could not get versions for {packageId} from {PackageSourceName}: {message}", pkgId, repo.PackageSource.Name, ex.Message);
                }
            }

            // If the package was not found in any repository.
            return (null, null);
        }

        // --- Start of Main Logic ---
        NuGetVersion? initialVersion;
        SourceRepository? initialRepo;

        if (!string.IsNullOrEmpty(version) && NuGetVersion.TryParse(version, out var parsedVersion))
        {
            // If a specific version is requested, we still need to find which repo has it.
            (initialVersion, initialRepo) = await FindPackageInPrioritizedReposAsync(packageId, new VersionRange(parsedVersion, true, parsedVersion, true));
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
            logger?.LogTrace("Analyzing dependencies for {currentPackage} from {PackageSourceName}", currentPackage, sourceRepo.PackageSource.Name);

            // We already know which repo this package came from, so we use it directly.
            var depInfoResource = await sourceRepo.GetResourceAsync<DependencyInfoResource>();
            var dependencyInfo = await depInfoResource.ResolvePackage(currentPackage, framework, cacheContext, NuGetLogger, CancellationToken.None);

            if (dependencyInfo == null)
            {
                logger?.LogWarning("Could not find dependency info for package {currentPackage}. It might be a package without dependencies.", currentPackage);
                continue;
            }

            foreach (var dependency in dependencyInfo.Dependencies)
            {
                // For each dependency, run the prioritized search to find which repo it lives in.
                var (bestVersion, foundInRepo) = await FindPackageInPrioritizedReposAsync(dependency.Id, dependency.VersionRange);

                if (bestVersion == null || foundInRepo == null)
                {
                    logger?.LogWarning("Could not find a compatible version for dependency {Id} with range {VersionRange} in any repository.", dependency.Id, dependency.VersionRange);
                    continue;
                }

                if (!resolvedPackages.TryGetValue(dependency.Id, out var existing) || bestVersion > existing.Version)
                {
                    var newIdentity = new PackageIdentity(dependency.Id, bestVersion);
                    if (existing == null)
                    {
                        // Enqueue the dependency along with the repository where it was found.
                        packagesToProcess.Enqueue((newIdentity, foundInRepo));
                    }
                    resolvedPackages[dependency.Id] = newIdentity;
                    logger?.LogTrace("  -> Resolved dependency: {Id} {Version} from {PackageSourceName}", newIdentity.Id, newIdentity.Version, foundInRepo.PackageSource.Name);
                }
            }
        }

        return resolvedPackages.Values;
    }

    private static async Task<(DownloadResourceResult? downloadResult, PackageIdentity? packageIdentity)> FindAndDownloadPackageAsync(
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

            var allVersions = await findResource.GetAllVersionsAsync(packageId, cacheContext, NuGetLogger, CancellationToken.None);
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
            var downloadContext = new PackageDownloadContext(cacheContext, Path.GetTempPath(), cacheContext.DirectDownload);

            logger?.LogTrace("Downloading {PackageId} version {PackageVersion} from {Repository}", packageId, versionToDownload, repo.PackageSource.Name);
            downloadResult = await downloadResource.GetDownloadResourceResultAsync(packageIdentity, downloadContext, globalPackagesFolder, NuGetLogger, CancellationToken.None);

            if (downloadResult?.Status == DownloadResourceResultStatus.Available)
            {
                logger?.LogTrace("Successfully downloaded package {PackageId}.", packageId);
                return (downloadResult, packageIdentity); // Success!
            }
        }

        logger?.LogError("Failed to find or download package {PackageId} from any repository.", packageId);
        return (null, null);
    }

    #region Helper Methods

    private static List<IWebModule> GetOrCreateWebModuleList(IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IReadOnlyCollection<IWebModule>));
        if (descriptor?.ImplementationInstance is List<IWebModule> list)
        {
            services.Remove(descriptor);
            return list;
        }
        if (descriptor?.ImplementationInstance is IReadOnlyCollection<IWebModule> collection)
        {
            services.Remove(descriptor);
            return [.. collection];
        }

        return [];
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
                .AddConsoleFormatter<TerseConsoleFormatter, TerseConsoleFormatterOptions>()
                .SetMinimumLevel(LogLevel.Debug);

                builder.AddDebug();
            });
            return loggerFactory.CreateLogger(typeof(WebModuleExtensions));
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

        // Priority 1: Use explicitly provided URLs if they exist.
        if (repositoryUrls != null && repositoryUrls.Any())
        {
            logger?.LogInformation("Using explicit repository URLs provided in configuration.");
            sources.AddRange(repositoryUrls.Select(url => new PackageSource(url)));
            settings = Settings.LoadDefaultSettings(root: null);
        }
        else
        {
            // Priority 2: Discover sources from nuget.config files.
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
                // Priority 3: Fallback to default nuget.org if nothing else was found.
                logger?.LogInformation("No sources found in nuget.config. Falling back to default nuget.org source.");
                sources.Add(new PackageSource("https://api.nuget.org/v3/index.json", "nuget.org"));
            }
        }

        // We have our final list of sources, now create the provider.
        // Note: The 'settings' object is still needed for other configurations like the global packages folder path.
        var sourceProvider = new PackageSourceProvider(settings, sources);
        var sourceRepoProvider = new SourceRepositoryProvider(sourceProvider, Repository.Provider.GetCoreV3());

        return [.. sourceRepoProvider.GetRepositories()];
    }
    #endregion

    /// <summary>
    /// Invokes each WebModule's Configure method to wire up endpoints and middleware.
    /// </summary>
    public static WebApplication UseWebModules(this WebApplication app)
    {
        var loggerFactory = app.Services.GetService<ILoggerFactory>();
        var logger = loggerFactory?.CreateLogger(typeof(WebModuleExtensions).FullName ?? "WebModuleExtensions");

        var WebModules = app.Services.GetRequiredService<IReadOnlyCollection<IWebModule>>();
        logger?.LogInformation("Configuring {WebModuleCount} web modules in UseWebModules.", WebModules.Count);
        foreach (var webModule in WebModules)
        {
            try
            {
                logger?.LogTrace("Configuring web module {WebModuleTypeName}", webModule.GetType().FullName);
                webModule.ConfigureApp(app);
                logger?.LogInformation("Successfully configured web module {WebModuleTypeName}", webModule.GetType().FullName);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error configuring web module {WebModuleTypeName}", webModule.GetType().FullName);
            }
        }
        return app;
    }
}
