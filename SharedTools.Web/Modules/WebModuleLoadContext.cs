using System.Reflection;
using System.Runtime.Loader;

namespace SharedTools.Web.Modules;

public class WebModuleLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver resolver;

    public WebModuleLoadContext(string webModulePath)
    {
        // webModulePath is the file path to the main WebModule assembly
        resolver = new AssemblyDependencyResolver(webModulePath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string? assemblyPath = resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            // Load dependency from WebModule's folder
            return LoadFromAssemblyPath(assemblyPath);
        }
        return null; // fallback to default context if not found
    }
}