using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

using System.Reflection;

namespace SharedTools.Web.Modules;

public static class WebModuleExtensions
{
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
                webModule.ConfigureServices(builder.Services);
                webModuleInstances.Add(webModule);
                logger?.LogInformation("Initialized and configured services for web module {WebModuleTypeName} from {AssemblyName}", type.FullName, assembly.FullName ?? "UnknownAssembly");
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to create instance or configure services for web module {WebModuleTypeName} from assembly {AssemblyName}", type.FullName, assembly.FullName ?? "UnknownAssembly");
            }
        }
    }

    /// <summary>
    /// Discovers WebModule DLLs from remote URLs, downloads them with their dependencies (including .deps.json),
    /// loads them, registers their Razor parts (assuming views are compiled into the main DLL),
    /// merges their static assets, and invokes their ConfigureServices.
    /// Also scans a provided list of already loaded assemblies for web modules.
    /// </summary>
    public static async Task<WebApplicationBuilder> AddWebModules(
        this WebApplicationBuilder builder,
        IEnumerable<string> mainAssemblyUrls,
        IEnumerable<Assembly>? explicitAssemblies = null)
    {
        if (builder.Environment is not IWebHostEnvironment webHostEnvironment)
        {
            throw new InvalidOperationException("AddWebModules requires an IWebHostEnvironment. Ensure the application is a web application.");
        }
        var env = webHostEnvironment; // Use IWebHostEnvironment
        var partManager = builder.Services.AddRazorPages().PartManager;
        var webModuleInstances = new List<IWebModule>(); // Renamed from webModuleInstance

        // Create a temporary service provider for logging during this method's execution
        // to avoid prematurely building the main application's service provider.
        var tempLoggingServices = new ServiceCollection();
        tempLoggingServices.AddLogging(loggingBuilder =>
        {
            loggingBuilder.AddDebug();
            loggingBuilder.AddConsole(); // Add console logger for visibility during setup
        });
        
        ILogger? logger = null; // Initialize logger to null
        await using var tempLoggingProvider = tempLoggingServices.BuildServiceProvider();
        try
        {
            var loggerFactory = tempLoggingProvider.GetService<ILoggerFactory>();
            logger = loggerFactory?.CreateLogger(typeof(WebModuleExtensions).FullName ?? "WebModuleExtensions");
        }
        catch (Exception ex)
        {
            // Fallback: Log to console if logger creation fails. This is a last resort.
            Console.WriteLine($"[Error] Failed to create temporary logger: {ex.Message}");
        }

        string baseTempPath = Path.Combine(Path.GetTempPath(), "SharedTools_WebModulesCache");
        if (!Directory.Exists(baseTempPath))
        {
            Directory.CreateDirectory(baseTempPath);
        }
        logger?.LogInformation("Using temporary cache directory for web modules: {BaseTempPath}", baseTempPath);

        var processedAssemblyFullNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Process modules from URLs
        if (mainAssemblyUrls != null)
        {
            using var httpClient = new HttpClient();
            foreach (var mainAssemblyUrl in mainAssemblyUrls)
            {
                if (string.IsNullOrWhiteSpace(mainAssemblyUrl))
                {
                    logger?.LogWarning("Skipping empty or whitespace main assembly URL.");
                    continue;
                }

                string dllFileName;
                try
                {
                    Uri uri = new(mainAssemblyUrl);
                    dllFileName = Path.GetFileName(uri.LocalPath);
                    if (string.IsNullOrEmpty(dllFileName) || !dllFileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        logger?.LogWarning("Invalid URL or cannot determine DLL filename from URL: {MainAssemblyUrl}. Skipping.", mainAssemblyUrl);
                        continue;
                    }
                }
                catch (UriFormatException ex)
                {
                    logger?.LogWarning(ex, "Invalid URL format: {MainAssemblyUrl}. Skipping.", mainAssemblyUrl);
                    continue;
                }

                string moduleSpecificTempPath = Path.Combine(baseTempPath, string.Concat(Path.GetFileNameWithoutExtension(dllFileName), "_", Guid.NewGuid().ToString("N").AsSpan(0, 8)));
                if (!Directory.Exists(moduleSpecificTempPath))
                {
                    Directory.CreateDirectory(moduleSpecificTempPath);
                }
                logger?.LogTrace("Created temporary directory for module {DllFileName}: {ModuleSpecificTempPath}", dllFileName, moduleSpecificTempPath);

                string localDllPath = Path.Combine(moduleSpecificTempPath, dllFileName);

                logger?.LogInformation("Downloading main assembly {DllFileName} from {MainAssemblyUrl}", dllFileName, mainAssemblyUrl);
                if (!await TryDownloadFileAsync(httpClient, mainAssemblyUrl, localDllPath, logger))
                {
                    logger?.LogError("Failed to download main assembly {DllFileName} from {MainAssemblyUrl}. Skipping module.", dllFileName, mainAssemblyUrl);
                    continue;
                }

                string depsJsonFileName = dllFileName.Replace(".dll", ".deps.json", StringComparison.OrdinalIgnoreCase);
                string depsJsonUrl = mainAssemblyUrl.Replace(".dll", ".deps.json", StringComparison.OrdinalIgnoreCase);
                string localDepsJsonPath = Path.Combine(moduleSpecificTempPath, depsJsonFileName);
                logger?.LogTrace("Attempting to download deps.json for {DllFileName} from {DepsJsonUrl}", dllFileName, depsJsonUrl);
                await TryDownloadFileAsync(httpClient, depsJsonUrl, localDepsJsonPath, logger);

                Assembly assembly;
                try
                {
                    var loadContext = new WebModuleLoadContext(localDllPath);
                    assembly = loadContext.LoadFromAssemblyPath(localDllPath);
                    logger?.LogInformation("Successfully loaded assembly {AssemblyName} from {LocalDllPath} (URL)", assembly.FullName ?? "UnknownAssembly", localDllPath);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Failed to load assembly from {LocalDllPath} (URL). Skipping module.", localDllPath);
                    continue;
                }

                if (processedAssemblyFullNames.Contains(assembly.FullName!))
                {
                    logger?.LogInformation("Assembly {AssemblyName} (from URL) was already processed. Skipping.", assembly.FullName);
                    continue;
                }

                ProcessAssemblyForWebModules(assembly, builder, partManager, webModuleInstances, logger, env);
                processedAssemblyFullNames.Add(assembly.FullName!);
            }
        }

        // 2. Process modules from explicitly provided already loaded assemblies
        if (explicitAssemblies != null)
        {
            logger?.LogInformation("Processing explicitly provided loaded assemblies for web modules.");
            foreach (var assembly in explicitAssemblies)
            {
                if (assembly.FullName == null || processedAssemblyFullNames.Contains(assembly.FullName))
                {
                    logger?.LogTrace("Assembly {AssemblyName} was already processed or has null FullName. Skipping.", assembly.FullName ?? "Unknown (null FullName)");
                    continue;
                }

                if (assembly.IsDynamic)
                {
                    logger?.LogTrace("Skipping dynamic assembly: {AssemblyName}", assembly.FullName);
                    continue;
                }
                
                // We assume if an assembly is explicitly provided, it's a candidate.
                logger?.LogInformation("Considering explicitly provided loaded assembly {AssemblyName} as a candidate for web modules.", assembly.FullName);
                ProcessAssemblyForWebModules(assembly, builder, partManager, webModuleInstances, logger, env);
                processedAssemblyFullNames.Add(assembly.FullName);
            }
        }

        builder.Services.AddSingleton<IReadOnlyCollection<IWebModule>>(webModuleInstances);
        logger?.LogInformation("Registered {WebModuleCount} web modules in total.", webModuleInstances.Count);
        return builder;
    }

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
                webModule.Configure(app);
                logger?.LogInformation("Successfully configured web module {WebModuleTypeName}", webModule.GetType().FullName);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error configuring web module {WebModuleTypeName}", webModule.GetType().FullName);
             }
        }
        return app;
    }

    // Helper method to download files
    private static async Task<bool> TryDownloadFileAsync(HttpClient httpClient, string url, string outputPath, ILogger? logger)
    {
        try
        {
            logger?.LogTrace("Attempting to download file from {Url} to {OutputPath}", url, outputPath);
            var response = await httpClient.GetAsync(url);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                logger?.LogTrace("File not found at {Url}", url);
                return false; // File not found, not an error for optional files
            }
            response.EnsureSuccessStatusCode(); // Throw for other errors

            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (outputDirectory != null && !Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fileStream);
            logger?.LogTrace("Successfully downloaded file from {Url} to {OutputPath}", url, outputPath);
            return true;
        }
        catch (HttpRequestException ex)
        {
            logger?.LogWarning(ex, "Failed to download file from {Url}. HTTP status: {StatusCode}", url, ex.StatusCode);
            return false; // Indicate download failure
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "An unexpected error occurred while downloading file from {Url}", url);
            return false;
        }
    }
}
