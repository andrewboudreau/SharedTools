using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Protocol.Core.Types;

using SharedTools.Web.Services;

using System.Reflection;

namespace SharedTools.Web.Modules;

/// <summary>
/// Extension methods for adding and configuring ApplicationPart-based modules.
/// </summary>
public static class ApplicationPartModuleExtensions
{
    private static NuGetFramework DefaultTargetFramework { get; } = new NuGetFramework(".NETCoreApp", new Version(10, 0));

    private static NuGet.Common.ILogger NuGetLogger { get; set; } = NuGet.Common.NullLogger.Instance;
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

        var processedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string tempCachePath = Path.Combine(Path.GetTempPath(), "SharedTools_ApplicationPartModulesCache");
        Directory.CreateDirectory(tempCachePath);

        var (logger, loggerFactory) = CreateTemporaryLogger();
        logger?.LogInformation("Using temporary cache directory: {BaseTempPath}", tempCachePath);

        var repositories = NuGetPackageService.CreateSourceRepositories(nuGetRepositoryUrls, logger);
        var nuGetCacheContext = new SourceCacheContext { NoCache = true };
        var nuGetSettings = Settings.LoadDefaultSettings(root: null);

        var targetFramework = DefaultTargetFramework;
        logger?.LogInformation("Resolving dependencies for target framework: {Framework}", targetFramework.DotNetFrameworkName);

        // Create NuGet service
        var nugetService = new NuGetPackageService(logger);

        // Create module loading context
        var loadingContext = new ModuleLoadingContext
        {
            Environment = env,
            PartManager = partManager,
            ModuleRegistry = moduleRegistry,
            Logger = logger,
            TempCachePath = tempCachePath
        };

        foreach (var packageId in packageIds)
        {
            logger?.LogInformation("--- Processing root package: {PackageId} ---", packageId);
            try
            {
                // Create dependency resolution context
                var resolutionContext = new DependencyResolutionContext
                {
                    PackageId = packageId,
                    Version = specificPackageVersion,
                    Framework = targetFramework,
                    Repositories = repositories,
                    CacheContext = nuGetCacheContext,
                    Logger = logger
                };

                var allPackagesToInstall = await NuGetPackageService.ResolveDependencyGraphAsync(resolutionContext);

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

                logger?.LogInformation("Extracting all package dependencies to flat directory: {Path}", flatExtractionPath);

                // Create extraction context
                var extractionContext = new PackageExtractionContext
                {
                    Packages = allPackagesToInstall,
                    FlatExtractionPath = flatExtractionPath,
                    TargetFramework = targetFramework,
                    Repositories = repositories,
                    Settings = nuGetSettings,
                    CacheContext = nuGetCacheContext,
                    Logger = logger
                };

                // Extract all packages to flat directory
                await nugetService.ExtractPackagesToFlatDirectory(extractionContext);

                // Load the main assembly
                string mainAssemblyPath = Path.Combine(flatExtractionPath, $"{rootPackageIdentity.Id}.dll");
                if (!File.Exists(mainAssemblyPath))
                {
                    logger?.LogError("Could not find main assembly {AssemblyPath} after extraction.", mainAssemblyPath);
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
                    logger?.LogInformation("Assembly {AssemblyName} was already processed. Skipping.", assembly.FullName);
                    continue;
                }

                // Create assembly processing context
                var processingContext = new AssemblyProcessingContext
                {
                    Assembly = assembly,
                    ModuleName = rootPackageIdentity.Id,
                    ExtractionPath = flatExtractionPath,
                    Builder = builder,
                    PartManager = partManager,
                    ModuleRegistry = moduleRegistry,
                    Environment = env,
                    LoadContext = loadContext,
                    Logger = logger
                };

                ProcessAssemblyForModules(processingContext);

                // Store the load context to prevent it from being garbage collected
                moduleRegistry.AssemblyLoadContexts.Add(loadContext);

                processedAssemblies.Add(assembly.FullName!);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "An unhandled error occurred while processing package {PackageId}", packageId);
            }
        }

        // Register the module registry as a singleton
        builder.Services.AddSingleton<IReadOnlyCollection<IApplicationPartModule>>(
            moduleRegistry.Modules.AsReadOnly());

        logger?.LogInformation("Registered {ModuleCount} application part modules in total.", moduleRegistry.Modules.Count);

        loggerFactory?.Dispose();
        return builder;
    }
    private static void ProcessAssemblyForModules(AssemblyProcessingContext context)
    {
        context.Logger?.LogInformation("Processing assembly {AssemblyName} for application part modules.",
            context.Assembly.FullName ?? "UnknownAssembly");

        // Find all types implementing IApplicationPartModule
        Type[] types;
        try
        {
            types = context.Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Some types couldn't be loaded, but we can still work with the ones that did load
            types = ex.Types.Where(t => t != null).ToArray()!;
            context.Logger?.LogWarning("Some types could not be loaded from {AssemblyName}. Continuing with {LoadedTypeCount} types.",
                context.Assembly.FullName ?? "UnknownAssembly", types.Length);
        }

        var moduleTypes = types
            .Where(t => t != null && typeof(IApplicationPartModule).IsAssignableFrom(t) &&
                       !t.IsInterface && !t.IsAbstract);

        context.Logger?.LogInformation("Found {ModuleTypeCount} IApplicationPartModule implementations in {AssemblyName}",
            moduleTypes.Count(), context.Assembly.FullName ?? "UnknownAssembly");

        foreach (var moduleType in moduleTypes)
        {
            try
            {
                // Create module instance
                var module = (IApplicationPartModule)Activator.CreateInstance(moduleType)!;

                // Configure services
                module.ConfigureServices(context.Builder.Services);

                // Add AssemblyPart for Razor Pages discovery
                if (!context.PartManager.ApplicationParts.Any(p => p is AssemblyPart ap && ap.Assembly == context.Assembly))
                {
                    context.PartManager.ApplicationParts.Add(new AssemblyPart(context.Assembly));
                }

                // Add CompiledRazorAssemblyPart for the main assembly (views are compiled into main assembly in .NET 6+)
                var compiledRazorPart = new CompiledRazorAssemblyPart(context.Assembly);
                context.PartManager.ApplicationParts.Add(compiledRazorPart);
                context.Logger?.LogInformation("Added CompiledRazorAssemblyPart for main assembly {AssemblyName} of module {ModuleName}",
                    context.Assembly.GetName().Name, module.Name);

                // Let the module configure additional application parts if needed
                module.ConfigureApplicationParts(context.PartManager);

                // Register the module
                context.ModuleRegistry.Modules.Add(module);

                context.Logger?.LogInformation(
                    "Initialized module {ModuleTypeName} from {AssemblyName} as ApplicationPart",
                    moduleType.FullName, context.Assembly.FullName ?? "UnknownAssembly");

                // Handle static assets
                RegisterModuleStaticAssets(context.Assembly, module.Name, context.ModuleRegistry, context.Logger);
            }
            catch (Exception ex)
            {
                context.Logger?.LogError(ex,
                    "Failed to create instance or configure module {ModuleTypeName} from assembly {AssemblyName}\r\n{Error}",
                    moduleType.FullName, context.Assembly.FullName ?? "UnknownAssembly", ex.Message);
            }
        }

        // Also add regular ApplicationParts for assemblies without modules (for compatibility)
        if (!moduleTypes.Any())
        {
            // Add AssemblyPart for Razor Pages discovery
            if (!context.PartManager.ApplicationParts.Any(part => part is AssemblyPart ap && ap.Assembly == context.Assembly))
            {
                context.PartManager.ApplicationParts.Add(new AssemblyPart(context.Assembly));
                context.Logger?.LogTrace("Added AssemblyPart for {AssemblyName}", context.Assembly.FullName ?? "UnknownAssembly");
            }

            // Add CompiledRazorAssemblyPart for compiled Razor views/pages
            if (!context.PartManager.ApplicationParts.Any(part => part is CompiledRazorAssemblyPart crap &&
                crap.Assembly == context.Assembly))
            {
                context.PartManager.ApplicationParts.Add(new CompiledRazorAssemblyPart(context.Assembly));
                context.Logger?.LogTrace("Added CompiledRazorAssemblyPart for {AssemblyName}", context.Assembly.FullName ?? "UnknownAssembly");
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
        logger?.LogInformation("Found {ResourceCount} manifest resources in assembly {AssemblyName}",
            manifestResourceNames.Length, assembly.FullName ?? "UnknownAssembly");

        // Find resources that contain wwwroot
        var wwwrootResources = manifestResourceNames.Where(r => r.Contains("wwwroot.", StringComparison.OrdinalIgnoreCase)).ToList();

        if (wwwrootResources.Any())
        {
            // Detect the actual namespace prefix used for wwwroot resources
            // Resources are typically named like "ExampleWebModule.wwwroot.styles.css" or "SharedTools.ExampleWebModule.wwwroot.styles.css"
            var firstResource = wwwrootResources.First();
            var wwwrootIndex = firstResource.IndexOf("wwwroot.", StringComparison.OrdinalIgnoreCase);

            if (wwwrootIndex > 0)
            {
                // Extract the namespace prefix up to and including "wwwroot"
                var baseNamespace = firstResource.Substring(0, wwwrootIndex + "wwwroot".Length);

                logger?.LogInformation(
                    "Detected embedded resource namespace: {BaseNamespace} from resource {ResourceName}",
                    baseNamespace, firstResource);

                var embeddedProvider = new EmbeddedFileProvider(assembly, baseNamespace);
                moduleRegistry.StaticFileProviders.Add((moduleName, embeddedProvider));

                logger?.LogInformation(
                    "Registered EmbeddedFileProvider for module {ModuleName} from assembly {AssemblyName} with base namespace {BaseNamespace}",
                    moduleName, assembly.FullName ?? "UnknownAssembly", baseNamespace);
            }
            else
            {
                logger?.LogWarning(
                    "Could not determine namespace prefix for wwwroot resources in assembly {AssemblyName}",
                    assembly.FullName ?? "UnknownAssembly");
            }
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
            logger?.LogInformation("Configuring static files for {ProviderCount} providers", moduleRegistry.StaticFileProviders.Count);

            foreach (var (moduleName, fileProvider) in moduleRegistry.StaticFileProviders)
            {
                var requestPath = $"/_content/{moduleName}";

                // Validate module name to prevent path traversal
                if (!IsValidModuleName(moduleName))
                {
                    logger?.LogError("Skipping static file configuration for module with invalid name: {ModuleName}", moduleName);
                    continue;
                }

                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = fileProvider,
                    RequestPath = requestPath
                });

                logger?.LogInformation("Configured static files for module {ModuleName} at path {RequestPath}", moduleName, requestPath);
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

    /// <summary>
    /// Validates that a module name is safe for use in URL paths.
    /// Prevents directory traversal and other path-based attacks.
    /// </summary>
    private static bool IsValidModuleName(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
            return false;

        // Check for path traversal attempts
        if (moduleName.Contains("..") || moduleName.Contains('/') || moduleName.Contains('\\'))
            return false;

        // Check for potentially dangerous characters
        var invalidChars = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '|', '?', '*']);
        if (moduleName.Any(c => invalidChars.Contains(c)))
            return false;

        return true;
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

    private static (ILogger?, ILoggerFactory?) CreateTemporaryLogger()
    {
        ILoggerFactory? loggerFactory = default;

        try
        {
            loggerFactory = LoggerFactory.Create(builder =>
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
            return (loggerFactory.CreateLogger(typeof(ApplicationPartModuleExtensions)), loggerFactory);
        }
        catch (Exception ex)
        {
            loggerFactory?.Dispose();
            Console.WriteLine("[Error] Failed to create temporary logger: {0}", ex.Message);
            return (null, null);
        }
    }

    #endregion
}