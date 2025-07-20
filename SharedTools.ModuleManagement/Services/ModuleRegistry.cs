using System.Collections.Concurrent;

namespace SharedTools.ModuleManagement.Services;

public class ModuleRegistry
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