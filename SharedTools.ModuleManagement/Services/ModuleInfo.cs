namespace SharedTools.ModuleManagement.Services;

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
}