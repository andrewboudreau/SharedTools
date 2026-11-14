namespace SharedTools.Web.Modules;

/// <summary>
/// What the host loaded at startup: modules, where they came from, and what failed.
/// Registered as a singleton so the module hub and host code can inspect it.
/// </summary>
public sealed class ModuleRegistry
{
    /// <summary>Modules that loaded and will be (or were) configured.</summary>
    public List<IApplicationPartModule> Modules { get; } = [];

    /// <summary>Per-module load details: the drop it came from, when known.</summary>
    public List<LoadedModule> Loaded { get; } = [];

    /// <summary>
    /// Modules that failed to load, captured instead of silently dropped so the hub, logs and
    /// <c>_status/{id}.json</c> can show what broke and why.
    /// </summary>
    public List<ModuleFailure> Failures { get; } = [];

    /// <summary>The managed module store (<c>Modules:Root</c>), if one was loaded.</summary>
    public ModuleStore? Store { get; internal set; }

    // Kept alive for the life of the process; module load contexts are not collectible.
    internal List<ModuleAssemblyLoadContext> LoadContexts { get; } = [];

    // Managed module ids whose status is written once the host has started.
    internal Dictionary<string, PendingStatus> StatusById { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal sealed class PendingStatus
    {
        public string? AttemptedDrop { get; set; }
        public string? ServingDrop { get; set; }
        public ModuleFailure? Failure { get; set; }
    }
}

/// <summary>A module that loaded.</summary>
/// <param name="Module">The module instance.</param>
/// <param name="Id">The store id (directory name) or <c>Modules:Paths</c> key; null for compile-time modules.</param>
/// <param name="Directory">The drop directory it was loaded from; null for compile-time modules.</param>
/// <param name="Drop">The drop id from the drop's <c>.drop</c> file, if present.</param>
public sealed record LoadedModule(IApplicationPartModule Module, string? Id, string? Directory, string? Drop);

/// <summary>The lifecycle phase at which a module failed.</summary>
public enum ModuleLoadPhase
{
    /// <summary>Finding or loading the drop's assembly, or discovering its module type.</summary>
    Load,
    /// <summary>The module's <c>ConfigureServices</c> threw.</summary>
    ConfigureServices,
    /// <summary>The module's <c>Configure</c> threw.</summary>
    Configure,
    /// <summary>The process died during startup while this drop was new (uploaded, not yet loaded).</summary>
    Crash
}

/// <summary>A module that failed, captured for diagnostics and the module hub.</summary>
/// <param name="ModuleId">The store id, path key, or module type that failed.</param>
/// <param name="Phase">The lifecycle phase at which it failed.</param>
/// <param name="Error">The failure message.</param>
/// <param name="Drop">The drop id that failed, if known.</param>
public sealed record ModuleFailure(string ModuleId, ModuleLoadPhase Phase, string Error, string? Drop = null);
