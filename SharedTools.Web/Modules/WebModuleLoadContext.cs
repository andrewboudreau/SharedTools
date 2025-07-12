using System.Reflection;

namespace SharedTools.Web.Modules;

public class WebModuleLoadContext : System.Runtime.Loader.AssemblyLoadContext
{
    private readonly System.Runtime.Loader.AssemblyDependencyResolver resolver;
    private readonly Assembly sharedContractsAssembly;

    public WebModuleLoadContext(string pluginPath) : base(isCollectible: true)
    {
        resolver = new System.Runtime.Loader.AssemblyDependencyResolver(pluginPath);

        // Store a reference to the host's IWebModule assembly.
        // This is the "one true" assembly that we will share.
        sharedContractsAssembly = typeof(IWebModule).Assembly;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Is the assembly being requested the shared contracts assembly?
        // We check by name. A more robust check might include the public key token.
        if (assemblyName.Name == sharedContractsAssembly.GetName().Name)
        {
            // If it is, DO NOT resolve it from the plugin's folder.
            // Return null to fall back to the host's AssemblyLoadContext.
            // The host will provide its already-loaded instance of the assembly.
            return null;
        }

        // CRITICAL: Also delegate all ASP.NET Core and Microsoft.Extensions assemblies to the host
        // This prevents MissingMethodException when modules try to use framework APIs
        if (assemblyName.Name != null && 
            (assemblyName.Name.StartsWith("Microsoft.AspNetCore.", StringComparison.OrdinalIgnoreCase) ||
             assemblyName.Name.StartsWith("Microsoft.Extensions.", StringComparison.OrdinalIgnoreCase)))
        {
            // Let the host provide these framework assemblies
            return null;
        }

        // For all other assemblies, resolve them from the plugin's flat directory.
        // This keeps the plugin's dependencies (like Azure.Core) isolated.
        string? assemblyPath = resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath != null ? LoadUnmanagedDllFromPath(libraryPath) : IntPtr.Zero;
    }
}