using Microsoft.AspNetCore.Mvc.ApplicationParts;
using System.Reflection;

namespace SharedTools.Web.Modules;

/// <summary>
/// Represents a module as an ApplicationPart, providing both assembly information
/// and module-specific functionality for Razor Pages and views.
/// </summary>
public class ModuleApplicationPart : ApplicationPart, IApplicationPartTypeProvider
{
    private readonly Assembly assembly;
    private readonly IApplicationPartModule module;

    public override string Name { get; }

    public ModuleApplicationPart(Assembly assembly, IApplicationPartModule module)
    {
        this.assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
        this.module = module ?? throw new ArgumentNullException(nameof(module));
        Name = module.Name;
    }

    /// <summary>
    /// Gets the module instance associated with this application part.
    /// </summary>
    public IApplicationPartModule Module => module;

    /// <summary>
    /// Gets the assembly associated with this module.
    /// </summary>
    public Assembly Assembly => assembly;

    /// <summary>
    /// Gets the types defined in the module's assembly.
    /// </summary>
    public IEnumerable<TypeInfo> Types => assembly.DefinedTypes;
}