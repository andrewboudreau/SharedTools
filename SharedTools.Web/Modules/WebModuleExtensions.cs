using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

using System.Reflection;

namespace SharedTools.Web.Modules;

public static class WebModuleExtensions
{
    private static void ProcessAssemblyForWebModules(
        Assembly assembly,
        WebApplicationBuilder builder,
        ApplicationPartManager partManager,
        List<IWebModule> webModuleInstances,
        Microsoft.Extensions.Logging.ILogger? logger,
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


    /// <summary>
    /// Discovers WebModules from NuGet packages, downloads them, extracts their contents,
    /// loads them, registers their Razor parts, merges static assets, and invokes ConfigureServices.
    /// Supports loading multiple packages with automatic dependency resolution.
    /// </summary>
    public static async Task<WebApplicationBuilder> AddWebModules(
        this WebApplicationBuilder builder,
        IEnumerable<string> packageIds,
        IEnumerable<string>? nuGetRepositoryUrls = null,
        string? specificPackageVersion = null)
    {
        if (builder.Environment is not IWebHostEnvironment webHostEnvironment)
        {
            throw new InvalidOperationException("AddWebModulesFromNuGet requires an IWebHostEnvironment. Ensure the application is a web application.");
        }
        var env = webHostEnvironment;
        var partManager = builder.Services.AddRazorPages().PartManager;

        var existingServiceDescriptor = builder.Services.FirstOrDefault(s => s.ServiceType == typeof(IReadOnlyCollection<IWebModule>));
        List<IWebModule> webModuleInstances;
        if (existingServiceDescriptor?.ImplementationInstance is List<IWebModule> existingList)
        {
            webModuleInstances = existingList;
        }
        else if (existingServiceDescriptor?.ImplementationInstance is IReadOnlyCollection<IWebModule> existingCollection)
        {
            webModuleInstances = new List<IWebModule>(existingCollection);
        }
        else
        {
            webModuleInstances = new List<IWebModule>();
        }

        var tempLoggingServices = new ServiceCollection();
        tempLoggingServices.AddLogging(loggingBuilder =>
        {
            loggingBuilder.AddDebug();
            loggingBuilder.AddConsole();
        });

        Microsoft.Extensions.Logging.ILogger? logger = null;
        await using var tempLoggingProvider = tempLoggingServices.BuildServiceProvider();
        try
        {
            var loggerFactory = tempLoggingProvider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>();
            logger = loggerFactory?.CreateLogger(typeof(WebModuleExtensions).FullName ?? "WebModuleExtensions");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Failed to create temporary logger: {ex.Message}");
        }

        string baseTempPath = Path.Combine(Path.GetTempPath(), "SharedTools_NuGetWebModulesCache");
        if (!Directory.Exists(baseTempPath))
        {
            Directory.CreateDirectory(baseTempPath);
        }
        logger?.LogInformation("Using temporary cache directory for NuGet web modules: {BaseTempPath}", baseTempPath);

        var processedAssemblyFullNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in webModuleInstances)
        {
            var assemblyFullName = module.GetType().Assembly.FullName;
            if (assemblyFullName != null)
            {
                processedAssemblyFullNames.Add(assemblyFullName);
            }
        }

        var nuGetSettings = NuGet.Configuration.Settings.LoadDefaultSettings(root: null);
        var packageSourceList = new List<NuGet.Configuration.PackageSource>();
        if (nuGetRepositoryUrls != null && nuGetRepositoryUrls.Any())
        {
            foreach (var url in nuGetRepositoryUrls)
            {
                packageSourceList.Add(new NuGet.Configuration.PackageSource(url));
            }
        }
        else
        {
            packageSourceList.Add(new NuGet.Configuration.PackageSource("https://api.nuget.org/v3/index.json", "nuget.org"));
        }
        var sourceProvider = new PackageSourceProvider(nuGetSettings, packageSourceList);
        var sourceRepositoryProvider = new SourceRepositoryProvider(sourceProvider, Repository.Provider.GetCoreV3());
        var repositoriesToSearch = sourceRepositoryProvider.GetRepositories().ToList();

        var nuGetCacheContext = new SourceCacheContext { NoCache = false, DirectDownload = true };
        var nuGetLogger = NullLogger.Instance;
        var cancellationToken = CancellationToken.None;

        foreach (var packageId in packageIds)
        {
            logger?.LogInformation("Processing NuGet package: {PackageId}", packageId);
            string packageSpecificTempPath = Path.Combine(baseTempPath, $"{packageId}_{Guid.NewGuid():N8}");
            if (!Directory.Exists(packageSpecificTempPath))
            {
                Directory.CreateDirectory(packageSpecificTempPath);
            }

            PackageIdentity? packageIdentity = null;
            DownloadResourceResult? downloadResult = null;

            foreach (var sourceRepository in repositoriesToSearch)
            {
                var findPackageByIdResource = await sourceRepository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
                if (findPackageByIdResource == null)
                {
                    logger?.LogWarning("Could not get FindPackageByIdResource from repository {SourceRepository}", sourceRepository.PackageSource.Source);
                    continue;
                }

                IEnumerable<NuGetVersion>? versions = null;
                try
                {
                    versions = await findPackageByIdResource.GetAllVersionsAsync(packageId, nuGetCacheContext, nuGetLogger, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to get versions for package {PackageId} from repository {SourceRepository}", packageId, sourceRepository.PackageSource.Source);
                }

                if (versions == null || !versions.Any())
                {
                    logger?.LogTrace("Package {PackageId} not found or no versions available in repository {SourceRepository}.", packageId, sourceRepository.PackageSource.Source);
                    continue;
                }

                NuGetVersion? selectedVersion = null;
                if (!string.IsNullOrEmpty(specificPackageVersion))
                {
                    if (NuGetVersion.TryParse(specificPackageVersion, out var parsedVersion))
                    {
                        selectedVersion = versions.FirstOrDefault(v => v.Equals(parsedVersion));
                    }
                    if (selectedVersion == null)
                    {
                        logger?.LogWarning("Specified version {SpecificPackageVersion} for package {PackageId} not found in repository {SourceRepository}. Trying latest stable.", specificPackageVersion, packageId, sourceRepository.PackageSource.Source);
                    }
                }

                if (selectedVersion == null)
                {
                    selectedVersion = versions.Where(v => !v.IsPrerelease).OrderByDescending(v => v).FirstOrDefault();
                    if (selectedVersion == null) selectedVersion = versions.OrderByDescending(v => v).FirstOrDefault();
                }

                if (selectedVersion == null)
                {
                    logger?.LogWarning("Could not determine a suitable version for package {PackageId} in repository {SourceRepository}.", packageId, sourceRepository.PackageSource.Source);
                    continue;
                }

                packageIdentity = new PackageIdentity(packageId, selectedVersion);
                var downloadResource = await sourceRepository.GetResourceAsync<DownloadResource>(cancellationToken);
                if (downloadResource == null)
                {
                    logger?.LogWarning("Could not get DownloadResource from repository {SourceRepository}", sourceRepository.PackageSource.Source);
                    continue;
                }

                var packageDownloadContext = new PackageDownloadContext(nuGetCacheContext, packageSpecificTempPath, nuGetCacheContext.DirectDownload);

                logger?.LogInformation("Attempting to download package {PackageId} version {PackageVersion} from {SourceRepository} to {DownloadPath}", packageId, selectedVersion, sourceRepository.PackageSource.Source, packageSpecificTempPath);

                string globalPackagesFolder = SettingsUtility.GetGlobalPackagesFolder(nuGetSettings);

                downloadResult = await downloadResource.GetDownloadResourceResultAsync(
                    packageIdentity,
                    packageDownloadContext,
                    globalPackagesFolder,
                    nuGetLogger,
                    cancellationToken);

                if (downloadResult != null && downloadResult.Status == DownloadResourceResultStatus.Available)
                {
                    break;
                }
                else
                {
                    logger?.LogWarning("Failed to download package {PackageId} from {SourceRepository}. Status: {Status}", packageId, sourceRepository.PackageSource.Source, downloadResult?.Status.ToString() ?? "Unknown");
                    downloadResult = null;
                }
            }

            if (downloadResult == null || downloadResult.Status != DownloadResourceResultStatus.Available || packageIdentity == null)
            {
                logger?.LogError("Failed to download package {PackageId} from all configured repositories or determine package identity.", packageId);
                continue;
            }

            string nupkgFilePath;
            if (downloadResult.PackageStream is FileStream fs)
            {
                nupkgFilePath = fs.Name;
            }
            else
            {
                var tempNupkgFileName = Path.Combine(packageSpecificTempPath, $"{packageIdentity.Id}.{packageIdentity.Version}.nupkg");
                using (var fileStream = new FileStream(tempNupkgFileName, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await downloadResult.PackageStream.CopyToAsync(fileStream);
                }
                nupkgFilePath = tempNupkgFileName;
                logger?.LogTrace("Package stream was not a FileStream; copied to {NupkgFilePath}", nupkgFilePath);
            }
            downloadResult.PackageStream.Dispose();

            logger?.LogInformation("Successfully downloaded package {PackageId} to {PackagePath}", packageId, nupkgFilePath);

            var packageExtractionPath = Path.Combine(packageSpecificTempPath, "extracted");
            Directory.CreateDirectory(packageExtractionPath);

            logger?.LogInformation("Extracting package {PackageId} to {PackageExtractionPath}", packageId, packageExtractionPath);
            List<string> libAssemblyPaths = new List<string>();
            try
            {
                using var packageReader = new PackageArchiveReader(nupkgFilePath);

                // Corrected ExtractPackageFileDelegate signature and implementation
                ExtractPackageFileDelegate extractDelegate = (string sourceFileInNupkg, string targetDiskPath, Stream fileContentStream) =>
                {
                    var dir = Path.GetDirectoryName(targetDiskPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    // The fileContentStream is the stream from the nupkg for the sourceFileInNupkg.
                    // We need to write this stream to targetDiskPath.
                    using (var fileStreamOnDisk = new FileStream(targetDiskPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        fileContentStream.CopyTo(fileStreamOnDisk);
                    }
                    return targetDiskPath; // Return the path of the extracted file on disk
                };

                var allLibFilesFromNupkg = packageReader.GetFiles("lib")
                                               .Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                                               .ToList();

                if (allLibFilesFromNupkg.Any())
                {
                    // CopyFilesAsync will iterate through allLibFilesFromNupkg.
                    // For each file, it will call extractDelegate with:
                    //   - sourceFileInNupkg: the entry from allLibFilesFromNupkg (e.g., "lib/net8.0/MyLib.dll")
                    //   - targetDiskPath: the calculated full path on disk (e.g., "C:\temp\extracted\lib\net8.0\MyLib.dll")
                    //   - fileContentStream: the stream for that file from the nupkg.
                    var extractedFiles = await packageReader.CopyFilesAsync(packageExtractionPath, allLibFilesFromNupkg, extractDelegate, nuGetLogger, cancellationToken);
                    libAssemblyPaths.AddRange(extractedFiles);
                }

                var contentFileGroups = packageReader.GetContentItems();
                foreach (var group in contentFileGroups)
                {
                    foreach (var itemPathInNupkg in group.Items)
                    {
                        string relativePath = itemPathInNupkg;
                        if (relativePath.StartsWith("content/", StringComparison.OrdinalIgnoreCase))
                            relativePath = relativePath.Substring("content/".Length);
                        else if (relativePath.StartsWith("contentFiles/", StringComparison.OrdinalIgnoreCase))
                        {
                            int wwwrootIndex = relativePath.IndexOf("wwwroot", StringComparison.OrdinalIgnoreCase);
                            if (wwwrootIndex > "contentFiles/".Length)
                            {
                                relativePath = relativePath.Substring(wwwrootIndex);
                            }
                            else
                            {
                                logger?.LogTrace("Skipping content file for wwwroot mapping (path too short or wwwroot not found at expected location): {ItemPathInNupkg}", itemPathInNupkg);
                                continue;
                            }
                        }

                        if (relativePath.StartsWith("wwwroot", StringComparison.OrdinalIgnoreCase))
                        {
                            string targetFilePath = Path.Combine(env.WebRootPath, relativePath.Substring("wwwroot".Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                            string targetDirectory = Path.GetDirectoryName(targetFilePath)!;
                            if (!Directory.Exists(targetDirectory))
                            {
                                Directory.CreateDirectory(targetDirectory);
                            }
                            using Stream sourceStream = packageReader.GetStream(itemPathInNupkg);
                            using FileStream targetStream = File.Create(targetFilePath);
                            await sourceStream.CopyToAsync(targetStream, cancellationToken);
                            logger?.LogTrace("Extracted content file {ItemPathInNupkg} to {TargetFilePath}", itemPathInNupkg, targetFilePath);
                        }
                    }
                }

                if (!libAssemblyPaths.Any())
                {
                    logger?.LogWarning("No library DLL files found or extracted from package {PackageId}.", packageId);
                    continue;
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to extract package {PackageId} or its assemblies.", packageId);
                continue;
            }
            finally
            {
                if (File.Exists(nupkgFilePath))
                {
                    try { File.Delete(nupkgFilePath); } catch (Exception ex) { logger?.LogWarning(ex, "Failed to delete temporary nupkg file: {NupkgFilePath}", nupkgFilePath); }
                }
            }

            string? mainAssemblyFileName = packageId + ".dll";
            string? mainAssemblyPath = libAssemblyPaths.FirstOrDefault(p => Path.GetFileName(p).Equals(mainAssemblyFileName, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(mainAssemblyPath))
            {
                mainAssemblyPath = libAssemblyPaths.FirstOrDefault(p => !Path.GetFileName(p).StartsWith("System.") && !Path.GetFileName(p).StartsWith("Microsoft."));
            }
            if (string.IsNullOrEmpty(mainAssemblyPath))
            {
                mainAssemblyPath = libAssemblyPaths.FirstOrDefault();
            }

            if (string.IsNullOrEmpty(mainAssemblyPath))
            {
                logger?.LogError("Could not determine the main assembly for package {PackageId} among extracted files: {LibFiles}", packageId, string.Join(", ", libAssemblyPaths.Select(Path.GetFileName)));
                continue;
            }

            logger?.LogInformation("Identified main assembly for {PackageId} as {MainAssemblyPath}", packageId, mainAssemblyPath);

            Assembly assembly;
            try
            {
                var loadContext = new WebModuleLoadContext(mainAssemblyPath);
                assembly = loadContext.LoadFromAssemblyPath(mainAssemblyPath);
                logger?.LogInformation("Successfully loaded assembly {AssemblyName} from NuGet package {PackageId} ({MainAssemblyPath})", assembly.FullName ?? "UnknownAssembly", packageId, mainAssemblyPath);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to load assembly from {MainAssemblyPath} (NuGet package {PackageId}). Skipping module.", mainAssemblyPath, packageId);
                continue;
            }

            if (assembly.FullName != null && processedAssemblyFullNames.Contains(assembly.FullName))
            {
                logger?.LogInformation("Assembly {AssemblyName} (from NuGet package {PackageId}) was already processed. Skipping.", assembly.FullName, packageId);
                continue;
            }

            ProcessAssemblyForWebModules(assembly, builder, partManager, webModuleInstances, logger, env);
            if (assembly.FullName != null)
            {
                processedAssemblyFullNames.Add(assembly.FullName);
            }
        }

        if (existingServiceDescriptor != null)
        {
            builder.Services.Remove(existingServiceDescriptor);
        }
        builder.Services.AddSingleton<IReadOnlyCollection<IWebModule>>(webModuleInstances.AsReadOnly());

        logger?.LogInformation("Registered {WebModuleCount} web modules in total after NuGet processing.", webModuleInstances.Count);
        return builder;
    }

    /// <summary>
    /// Invokes each WebModule's Configure method to wire up endpoints and middleware.
    /// </summary>
    public static WebApplication UseWebModules(this WebApplication app)
    {
        var loggerFactory = app.Services.GetService<Microsoft.Extensions.Logging.ILoggerFactory>();
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
