using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using System.Reflection;

namespace SharedTools.Web.Modules;

/// <summary>
/// A feature provider that discovers controllers from module application parts.
/// This ensures that controllers from dynamically loaded modules are properly registered.
/// </summary>
public class ModuleControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        foreach (var part in parts)
        {
            if (part is ModuleApplicationPart modulePart)
            {
                // Add all controller types from the module
                var controllerTypes = modulePart.Types
                    .Where(t => IsController(t) && !feature.Controllers.Contains(t))
                    .ToList();

                foreach (var type in controllerTypes)
                {
                    feature.Controllers.Add(type);
                }
            }
        }
    }

    private static bool IsController(TypeInfo typeInfo)
    {
        if (!typeInfo.IsClass || typeInfo.IsAbstract || !typeInfo.IsPublic)
            return false;

        if (typeInfo.ContainsGenericParameters)
            return false;

        var typeName = typeInfo.Name;
        return typeName.EndsWith("Controller", StringComparison.OrdinalIgnoreCase) ||
               typeInfo.GetCustomAttribute<Microsoft.AspNetCore.Mvc.ControllerAttribute>() != null;
    }
}