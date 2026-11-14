using System.Reflection;
using System.Runtime.Loader;

using Microsoft.AspNetCore.Builder;

namespace SharedTools.Web.Modules;

/// <summary>
/// Loads one module drop in isolation. The module's own dependencies resolve from its drop through
/// its <c>.deps.json</c>; the contract assembly (SharedTools.Web) and the shared framework come from
/// the host, so types like <see cref="IApplicationPartModule"/> and <see cref="WebApplication"/> are
/// the same on both sides.
/// </summary>
public sealed class ModuleAssemblyLoadContext : AssemblyLoadContext
{
    private static readonly string ContractAssemblyName = typeof(IApplicationPartModule).Assembly.GetName().Name!;

    // Assemblies shipped in the host's shared frameworks (Microsoft.NETCore.App, Microsoft.AspNetCore.App),
    // by simple name -> path. A drop normally doesn't contain these (publish prunes them); when it does,
    // its copy is used only if it is newer than the framework's, because the host can't satisfy a
    // reference to a higher version.
    private static readonly Lazy<Dictionary<string, string>> FrameworkAssemblies = new(() =>
        new[] { typeof(object).Assembly.Location, typeof(WebApplication).Assembly.Location }
            .Select(Path.GetDirectoryName)
            .Where(dir => !string.IsNullOrEmpty(dir))
            .Distinct()
            .SelectMany(dir => Directory.EnumerateFiles(dir!, "*.dll"))
            .GroupBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase));

    private readonly AssemblyDependencyResolver resolver;

    public ModuleAssemblyLoadContext(string entryAssemblyPath)
        : base(name: Path.GetFileNameWithoutExtension(entryAssemblyPath), isCollectible: false)
    {
        resolver = new AssemblyDependencyResolver(entryAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is null || assemblyName.Name == ContractAssemblyName)
        {
            return null; // host's copy
        }

        var dropPath = resolver.ResolveAssemblyToPath(assemblyName);

        if (FrameworkAssemblies.Value.TryGetValue(assemblyName.Name, out var frameworkPath))
        {
            return dropPath is not null && IsNewer(dropPath, frameworkPath)
                ? LoadFromAssemblyPath(dropPath)
                : null; // shared framework wins
        }

        return dropPath is not null ? LoadFromAssemblyPath(dropPath) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath is not null ? LoadUnmanagedDllFromPath(libraryPath) : IntPtr.Zero;
    }

    private static bool IsNewer(string candidatePath, string frameworkPath)
    {
        try
        {
            return AssemblyName.GetAssemblyName(candidatePath).Version > AssemblyName.GetAssemblyName(frameworkPath).Version;
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException)
        {
            return false;
        }
    }
}
