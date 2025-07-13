using Microsoft.AspNetCore.Mvc.ApplicationParts;
using System.Reflection;

namespace SharedTools.Web.Modules;

/// <summary>
/// Represents a module as an ApplicationPart, providing both assembly information
/// and module-specific functionality.
/// </summary>
public class ModuleApplicationPart : ApplicationPart, IApplicationPartTypeProvider
{
    private readonly Assembly _assembly;
    private readonly IApplicationPartModule _module;

    public override string Name { get; }

    public ModuleApplicationPart(Assembly assembly, IApplicationPartModule module)
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
        _module = module ?? throw new ArgumentNullException(nameof(module));
        Name = module.Name;
    }

    /// <summary>
    /// Gets the module instance associated with this application part.
    /// </summary>
    public IApplicationPartModule Module => _module;

    /// <summary>
    /// Gets the assembly associated with this module.
    /// </summary>
    public Assembly Assembly => _assembly;

    /// <summary>
    /// Gets the types defined in the module's assembly.
    /// </summary>
    public IEnumerable<TypeInfo> Types => _assembly.DefinedTypes;
}