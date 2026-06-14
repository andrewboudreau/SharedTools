namespace SharedTools.ModuleManagement.Services;

public enum ModuleStatus
{
    Loaded,
    Failed
}

public class ModuleInfo
{
    public required string Name { get; set; }
    public required string AssemblyName { get; set; }
    public required string Version { get; set; }
    public string? Description { get; set; }
    public string? EntryPoint { get; set; }
    public List<string> Routes { get; set; } = [];
    public List<string> StaticAssets { get; set; } = [];
    public string? LatestNuGetVersion { get; set; }
    public DateTime LoadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Whether the module loaded successfully or failed during startup.</summary>
    public ModuleStatus Status { get; set; } = ModuleStatus.Loaded;

    /// <summary>The failure message when <see cref="Status"/> is <see cref="ModuleStatus.Failed"/>.</summary>
    public string? Error { get; set; }
}