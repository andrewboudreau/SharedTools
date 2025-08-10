using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Logging;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Protocol.Core.Types;

namespace SharedTools.Web.Modules;

/// <summary>
/// Context for loading application part modules with all required dependencies.
/// </summary>
public class ModuleLoadingContext
{
    public required IWebHostEnvironment Environment { get; init; }
    public required ApplicationPartManager PartManager { get; init; }
    public required ApplicationPartModuleExtensions.ModuleRegistry ModuleRegistry { get; init; }
    public ILogger? Logger { get; init; }
    public string TempCachePath { get; init; } = Path.Combine(Path.GetTempPath(), "SharedTools_ApplicationPartModulesCache");
}

/// <summary>
/// Context for processing a single assembly for modules.
/// </summary>
public class AssemblyProcessingContext
{
    public required Assembly Assembly { get; init; }
    public required string ModuleName { get; init; }
    public required string ExtractionPath { get; init; }
    public required WebApplicationBuilder Builder { get; init; }
    public required ApplicationPartManager PartManager { get; init; }
    public required ApplicationPartModuleExtensions.ModuleRegistry ModuleRegistry { get; init; }
    public required IWebHostEnvironment Environment { get; init; }
    public required ModuleAssemblyLoadContext LoadContext { get; init; }
    public ILogger? Logger { get; init; }
}

/// <summary>
/// Context for NuGet package operations.
/// </summary>
public class NuGetOperationContext
{
    public required NuGetFramework TargetFramework { get; init; }
    public required IEnumerable<SourceRepository> Repositories { get; init; }
    public required SourceCacheContext CacheContext { get; init; }
    public required ISettings Settings { get; init; }
    public ILogger? Logger { get; init; }
}

/// <summary>
/// Context for extracting packages to a flat directory structure.
/// </summary>
public class PackageExtractionContext
{
    public required IEnumerable<PackageIdentity> Packages { get; init; }
    public required string FlatExtractionPath { get; init; }
    public required NuGetFramework TargetFramework { get; init; }
    public required IEnumerable<SourceRepository> Repositories { get; init; }
    public required ISettings Settings { get; init; }
    public required SourceCacheContext CacheContext { get; init; }
    public ILogger? Logger { get; init; }
}

/// <summary>
/// Context for finding and downloading a specific package.
/// </summary>
public class PackageDownloadContext
{
    public required string PackageId { get; init; }
    public string? SpecificVersion { get; init; }
    public required IEnumerable<SourceRepository> Repositories { get; init; }
    public required ISettings Settings { get; init; }
    public required SourceCacheContext CacheContext { get; init; }
    public ILogger? Logger { get; init; }
}

/// <summary>
/// Context for resolving package dependency graphs.
/// </summary>
public class DependencyResolutionContext
{
    public required string PackageId { get; init; }
    public string? Version { get; init; }
    public required NuGetFramework Framework { get; init; }
    public required IEnumerable<SourceRepository> Repositories { get; init; }
    public required SourceCacheContext CacheContext { get; init; }
    public ILogger? Logger { get; init; }
}