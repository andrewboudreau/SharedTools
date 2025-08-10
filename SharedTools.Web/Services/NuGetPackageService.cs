using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using SharedTools.Web.Modules;
using SharedTools.Web.Modules.Logging;

namespace SharedTools.Web.Services;

/// <summary>
/// Service for handling NuGet package operations including resolution, download, and extraction.
/// </summary>
public class NuGetPackageService
{
    private static NuGet.Common.ILogger NuGetLogger { get; set; } = NullLogger.Instance;
    private readonly ILogger<NuGetPackageService>? _logger;
    public NuGetPackageService(ILogger? logger = null)
    {
        _logger = logger as ILogger<NuGetPackageService>;
        if (logger != null)
        {
            NuGetLogger = new NugetLoggerAdapter(logger);
        }
    }
    /// <summary>
    /// Resolves the dependency graph for a package and returns all required packages.
    /// </summary>
    public async Task<IEnumerable<PackageIdentity>?> ResolveDependencyGraphAsync(DependencyResolutionContext context)
    {
        var resolvedPackages = new Dictionary<string, PackageIdentity>(StringComparer.OrdinalIgnoreCase);
        var packagesToProcess = new Queue<(PackageIdentity package, SourceRepository repository)>();

        async Task<(NuGetVersion? version, SourceRepository? repo)> FindPackageInPrioritizedReposAsync(
        string pkgId, VersionRange? range = null)
        {
            foreach (var repo in context.Repositories)
            {
                try
                {
                    var findResource = await repo.GetResourceAsync<FindPackageByIdResource>();
                    var versions = await findResource.GetAllVersionsAsync(pkgId, context.CacheContext,
                    NuGetLogger, CancellationToken.None);

                    if (versions != null && versions.Any())
                    {
                        context.Logger?.LogInformation(
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
                    context.Logger?.LogWarning("Could not get versions for {packageId} from {PackageSourceName}: {message}",
                    pkgId, repo.PackageSource.Name, ex.Message);
                }
            }

            return (null, null);
        }

        // Find initial package
        NuGetVersion? initialVersion;
        SourceRepository? initialRepo;

        if (!string.IsNullOrEmpty(context.Version) && NuGetVersion.TryParse(context.Version, out var parsedVersion))
        {
            (initialVersion, initialRepo) = await FindPackageInPrioritizedReposAsync(context.PackageId,
            new VersionRange(parsedVersion, true, parsedVersion, true));
        }
        else
        {
            (initialVersion, initialRepo) = await FindPackageInPrioritizedReposAsync(context.PackageId);
        }

        if (initialVersion == null || initialRepo == null)
        {
            context.Logger?.LogError("Could not find root package {packageId} in any repository.", context.PackageId);
            return null;
        }

        var initialIdentity = new PackageIdentity(context.PackageId, initialVersion);
        packagesToProcess.Enqueue((initialIdentity, initialRepo));
        resolvedPackages.Add(context.PackageId, initialIdentity);

        while (packagesToProcess.Count > 0)
        {
            var (currentPackage, sourceRepo) = packagesToProcess.Dequeue();
            context.Logger?.LogTrace("Analyzing dependencies for {currentPackage} from {PackageSourceName}",
            currentPackage, sourceRepo.PackageSource.Name);

            var depInfoResource = await sourceRepo.GetResourceAsync<DependencyInfoResource>();
            var dependencyInfo = await depInfoResource.ResolvePackage(currentPackage, context.Framework,
            context.CacheContext, NuGetLogger, CancellationToken.None);

            if (dependencyInfo == null)
            {
                context.Logger?.LogWarning(
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
                    context.Logger?.LogWarning(
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
                    context.Logger?.LogTrace("  -> Resolved dependency: {Id} {Version} from {PackageSourceName}",
                    newIdentity.Id, newIdentity.Version, foundInRepo.PackageSource.Name);
                }
            }
        }

        return resolvedPackages.Values;
    }

    /// <summary>
    /// Finds and downloads a specific package version.
    /// </summary>
    public async Task<(DownloadResourceResult? downloadResult, PackageIdentity? packageIdentity)> FindAndDownloadPackageAsync(

        SharedTools.Web.Modules.PackageDownloadContext context)
    {
        PackageIdentity? packageIdentity = null;
        DownloadResourceResult? downloadResult = null;

        foreach (var repo in context.Repositories)
        {
            context.Logger?.LogTrace("Searching for {PackageId} in {Repository}", context.PackageId, repo.PackageSource.Name);
            var findResource = await repo.GetResourceAsync<FindPackageByIdResource>();
            if (findResource == null) continue;

            var allVersions = await findResource.GetAllVersionsAsync(context.PackageId, context.CacheContext,
            NuGetLogger, CancellationToken.None);
            if (allVersions == null || !allVersions.Any()) continue;

            NuGetVersion? versionToDownload = null;
            if (!string.IsNullOrEmpty(context.SpecificVersion) && NuGetVersion.TryParse(context.SpecificVersion, out var parsedVersion))
            {
                versionToDownload = allVersions.FirstOrDefault(v => v.Equals(parsedVersion));
            }
            versionToDownload ??= allVersions.Where(v => !v.IsPrerelease).Max() ?? allVersions.Max();

            if (versionToDownload == null) continue;

            packageIdentity = new PackageIdentity(context.PackageId, versionToDownload);
            var downloadResource = await repo.GetResourceAsync<DownloadResource>();
            if (downloadResource == null) continue;

            var globalPackagesFolder = SettingsUtility.GetGlobalPackagesFolder(context.Settings);
            var downloadContextInternal = new NuGet.Protocol.Core.Types.PackageDownloadContext(context.CacheContext, Path.GetTempPath(),
            context.CacheContext.DirectDownload);

            context.Logger?.LogTrace("Downloading {PackageId} version {PackageVersion} from {Repository}",
            context.PackageId, versionToDownload, repo.PackageSource.Name);
            downloadResult = await downloadResource.GetDownloadResourceResultAsync(packageIdentity,
            downloadContextInternal, globalPackagesFolder, NuGetLogger, CancellationToken.None);

            if (downloadResult?.Status == DownloadResourceResultStatus.Available)
            {
                context.Logger?.LogTrace("Successfully downloaded package {PackageId}.", context.PackageId);
                return (downloadResult, packageIdentity);
            }
        }

        context.Logger?.LogError("Failed to find or download package {PackageId} from any repository.", context.PackageId);
        return (null, null);
    }

    /// <summary>
    /// Extracts packages to a flat directory structure for assembly loading.
    /// </summary>
    public async Task ExtractPackagesToFlatDirectory(SharedTools.Web.Modules.PackageExtractionContext context)
    {
        foreach (var packageIdentity in context.Packages)
        {
            var downloadContext = new SharedTools.Web.Modules.PackageDownloadContext
            {
                PackageId = packageIdentity.Id,
                SpecificVersion = packageIdentity.Version.ToNormalizedString(),
                Repositories = context.Repositories,
                Settings = context.Settings,
                CacheContext = context.CacheContext,
                Logger = context.Logger
            };

            var (downloadResult, _) = await FindAndDownloadPackageAsync(downloadContext);

            if (downloadResult == null)
            {
                context.Logger?.LogWarning("Failed to download dependency package {PackageId}", packageIdentity);
                continue;
            }

            using (var reader = new PackageArchiveReader(downloadResult.PackageStream))
            {
                var libItems = await reader.GetLibItemsAsync(CancellationToken.None);
                var nearestFramework = NuGetFrameworkUtility.GetNearest(libItems, context.TargetFramework,
                f => f.TargetFramework);

                if (nearestFramework != null)
                {
                    foreach (var item in nearestFramework.Items)
                    {
                        if (item.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            reader.ExtractFile(item, Path.Combine(context.FlatExtractionPath,
                            Path.GetFileName(item)), NuGetLogger);
                        }
                    }
                }
            }

            downloadResult.Dispose();
        }
    }

    /// <summary>
    /// Creates source repositories from configuration or default sources.
    /// </summary>
    public static List<SourceRepository> CreateSourceRepositories(IEnumerable<string>? repositoryUrls, Microsoft.Extensions.Logging.ILogger? logger)
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
                logger?.LogInformation("Discovered {DiscoveredSourcesCount} sources from nuget.config.", discoveredSources.Count());
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
}