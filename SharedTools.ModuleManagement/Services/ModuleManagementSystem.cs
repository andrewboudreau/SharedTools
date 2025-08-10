using System.Collections.Concurrent;

namespace SharedTools.ModuleManagement.Services;

/// <summary>
/// Internal system for managing module information for the ModuleManagement UI.
/// This is separate from the main ModuleRegistry which handles ApplicationPart registration.
/// </summary>
public class ModuleManagementSystem
{
    private readonly ConcurrentDictionary<string, ModuleInfo> modules = new();

    public void RegisterModule(ModuleInfo moduleInfo)
    {
        modules.TryAdd(moduleInfo.AssemblyName, moduleInfo);
    }

    public IEnumerable<ModuleInfo> GetAllModules()
    {
        return modules.Values.OrderBy(m => m.Name);
    }

    public ModuleInfo? GetModule(string assemblyName)
    {
        return modules.TryGetValue(assemblyName, out var module) ? module : null;
    }

    public int ModuleCount => modules.Count;
}