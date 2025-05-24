using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging; // Added for ILogger and ILoggerFactory

using System.Reflection;

namespace SharedTools.Web.Modules;

public static class WebModuleExtensions
{
    /// <summary>
    /// Discovers plugin DLLs from remote URLs, downloads them with their dependencies (including .deps.json),
    /// loads them, registers their Razor parts (assuming views are compiled into the main DLL),
    /// merges their static assets, and invokes their ConfigureServices.
    /// </summary>
    public static async Task<WebApplicationBuilder> AddWebModules(this WebApplicationBuilder builder, IEnumerable<string> mainAssemblyUrls)
    {
        var env = builder.Environment;
        var partManager = builder.Services.AddRazorPages().PartManager;
        var webModuleInstance = new List<IWebModule>();
        
        using var httpClient = new HttpClient();

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
                Uri uri = new Uri(mainAssemblyUrl);
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
                logger?.LogInformation("Successfully loaded assembly {AssemblyName} from {LocalDllPath}", assembly.FullName ?? "UnknownAssembly", localDllPath);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to load assembly from {LocalDllPath}. Skipping module.", localDllPath);
                continue;
            }

            // Add the plugin assembly so its pages/controllers are discovered
            partManager.ApplicationParts.Add(new AssemblyPart(assembly));
            logger?.LogTrace("Added AssemblyPart for {AssemblyName} (for controllers/pages)", assembly.FullName ?? "UnknownAssembly");

            // Also add the main assembly as a CompiledRazorAssemblyPart for its views
            // This assumes views are compiled into the main assembly.
            partManager.ApplicationParts.Add(new CompiledRazorAssemblyPart(assembly));
            logger?.LogTrace("Added CompiledRazorAssemblyPart for {AssemblyName} (for compiled views)", assembly.FullName ?? "UnknownAssembly");
            
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
                
                if (isAssignable && typeInAssembly.FullName != typeof(IWebModule).FullName) // Log interfaces it implements if it's a candidate
                {
                    var interfaces = typeInAssembly.GetInterfaces().Select(i => i.AssemblyQualifiedName).ToArray();
                    logger?.LogTrace("  - Type {TypeName} implements interfaces: {Interfaces}", typeInAssembly.FullName, string.Join(", ", interfaces));
                    if (!interfaces.Contains(typeof(IWebModule).AssemblyQualifiedName))
                    {
                        logger?.LogWarning("  - Type {TypeName} is assignable to IWebModule but its GetInterfaces() does not include the host's IWebModule AssemblyQualifiedName. Host IWebModule: {HostIWebModuleAQN}", typeInAssembly.FullName, typeof(IWebModule).AssemblyQualifiedName);
                    }
                }
            }
            
            var pluginTypes = allTypesInAssembly
                .Where(t => typeof(IWebModule).IsAssignableFrom(t)
                            && !t.IsInterface
                            && !t.IsAbstract);

            logger?.LogInformation("Found {PluginTypeCount} concrete IWebModule implementations in {AssemblyName}", pluginTypes.Count(), assembly.FullName ?? "UnknownAssembly");
            foreach (var type in pluginTypes)
            {
                try
                {
                    var plugin = (IWebModule)Activator.CreateInstance(type)!;
                    plugin.ConfigureServices(builder.Services); // Corrected: Pass builder.Services
                    webModuleInstance.Add(plugin);
                    logger?.LogInformation("Initialized and configured services for web module {PluginTypeName}", type.FullName);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Failed to create instance or configure services for web module {PluginTypeName} from assembly {AssemblyName}", type.FullName, assembly.FullName ?? "UnknownAssembly");
                }
            }
        }

        builder.Services.AddSingleton<IReadOnlyCollection<IWebModule>>(webModuleInstance);
        logger?.LogInformation("Registered {WebModuleCount} web modules.", webModuleInstance.Count);
        return builder;
    }

    /// <summary>
    /// Invokes each plugin's Configure method to wire up endpoints and middleware.
    /// </summary>
    public static WebApplication UseWebModules(this WebApplication app)
    {
        var loggerFactory = app.Services.GetService<ILoggerFactory>();
        var logger = loggerFactory?.CreateLogger(typeof(WebModuleExtensions).FullName ?? "WebModuleExtensions");

        var plugins = app.Services.GetRequiredService<IReadOnlyCollection<IWebModule>>();
        logger?.LogInformation("Configuring {PluginCount} web modules in UseWebModules.", plugins.Count);
        foreach (var plugin in plugins)
        {
            try
            {
                logger?.LogTrace("Configuring web module {PluginTypeName}", plugin.GetType().FullName);
                plugin.Configure(app);
                logger?.LogInformation("Successfully configured web module {PluginTypeName}", plugin.GetType().FullName);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error configuring web module {PluginTypeName}", plugin.GetType().FullName);
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
