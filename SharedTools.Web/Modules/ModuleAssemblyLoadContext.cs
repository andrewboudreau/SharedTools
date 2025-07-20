using System.Reflection;
using System.Runtime.Loader;

namespace SharedTools.Web.Modules;

/// <summary>
/// Custom AssemblyLoadContext for loading module assemblies in isolation while sharing
/// framework assemblies and module contracts with the host application.
/// </summary>
public class ModuleAssemblyLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver resolver;
    private readonly Assembly sharedContractsAssembly;

    public ModuleAssemblyLoadContext(string pluginPath) : base(isCollectible: false)
    {
        resolver = new AssemblyDependencyResolver(pluginPath);

        // Store a reference to the host's module contracts assembly
        sharedContractsAssembly = typeof(IApplicationPartModule).Assembly;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Check if this is the shared contracts assembly
        if (assemblyName.Name == sharedContractsAssembly.GetName().Name)
        {
            // Return null to delegate to the host's AssemblyLoadContext
            // This ensures all modules use the same contract types
            return null;
        }

        // Delegate all ASP.NET Core and Microsoft.Extensions assemblies to the host
        // This prevents version conflicts and ensures modules use the host's framework
        if (assemblyName.Name != null &&
            (assemblyName.Name.StartsWith("Microsoft.AspNetCore.", StringComparison.OrdinalIgnoreCase) ||
             assemblyName.Name.StartsWith("Microsoft.Extensions.", StringComparison.OrdinalIgnoreCase) ||
             assemblyName.Name.Equals("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase)))
        {
            // Let the host provide these framework assemblies
            return null;
        }
        
        // For System.* assemblies, first try to resolve from the plugin's directory
        // If not found there, delegate to the host
        if (assemblyName.Name != null && assemblyName.Name.StartsWith("System.", StringComparison.OrdinalIgnoreCase))
        {
            string? assemblyPath = resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null && File.Exists(assemblyPath))
            {
                return LoadFromAssemblyPath(assemblyPath);
            }
            // If not found in plugin directory, delegate to host
            return null;
        }

        // For all other assemblies, resolve them from the plugin's directory
        // This keeps the plugin's specific dependencies isolated
        string? assemblyPath2 = resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath2 != null)
        {
            return LoadFromAssemblyPath(assemblyPath2);
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath != null ? LoadUnmanagedDllFromPath(libraryPath) : IntPtr.Zero;
    }
}