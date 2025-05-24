using System.Reflection;
using System.Runtime.Loader;

namespace SharedTools.Web.Modules;

public class WebModuleLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver resolver;

    public WebModuleLoadContext(string pluginPath)
    {
        // pluginPath is the file path to the main plugin assembly
        resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string? assemblyPath = resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            // Load dependency from plugin's folder
            return LoadFromAssemblyPath(assemblyPath);
        }
        return null; // fallback to default context if not found
    }
}