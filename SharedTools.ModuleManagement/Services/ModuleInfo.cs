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

    /// <summary>The drop id serving this module (from the drop's <c>.drop</c> file), if known.</summary>
    public string? Drop { get; set; }

    /// <summary>Where the module came from: a managed drop, a <c>Modules:Paths</c> directory, or the host itself.</summary>
    public string? Source { get; set; }

    /// <summary>
    /// For a loaded module, a problem that didn't stop it serving, e.g. a newer drop that failed
    /// and was rolled back.
    /// </summary>
    public string? Warning { get; set; }
    public DateTime LoadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Whether the module loaded successfully or failed during startup.</summary>
    public ModuleStatus Status { get; set; } = ModuleStatus.Loaded;

    /// <summary>The failure message when <see cref="Status"/> is <see cref="ModuleStatus.Failed"/>.</summary>
    public string? Error { get; set; }
}