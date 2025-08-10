using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;

using SharedTools.ModuleManagement.Services;
using SharedTools.Web.Modules;

namespace SharedTools.ModuleManagement;

public class ModuleManagementModule : IApplicationPartModule
{
    public string Name => "SharedTools.ModuleManagement";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<SharedTools.ModuleManagement.Services.ModuleRegistry>();
        services.AddRazorPages();
    }

    public void Configure(WebApplication app)
    {
        app.MapRazorPages();

        // Get the registry and discover all loaded modules
        var registry = app.Services.GetRequiredService<SharedTools.ModuleManagement.Services.ModuleRegistry>();
        var modules = app.Services.GetService<IReadOnlyCollection<IApplicationPartModule>>();

        // Register this module
        registry.RegisterModule(new ModuleInfo
        {
            Name = "Module Management",
            AssemblyName = GetType().Assembly.GetName().Name ?? "SharedTools.ModuleManagement",
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Description = "Web-based management interface for viewing and managing loaded modules",
            EntryPoint = "/SharedTools.ModuleManagement"
        });

        // Register all other loaded modules
        if (modules != null)
        {
            foreach (var module in modules.Where(m => m.GetType() != GetType()))
            {
                var assembly = module.GetType().Assembly;
                var assemblyName = assembly.GetName();

                var moduleInfo = new ModuleInfo
                {
                    Name = module.Name,
                    AssemblyName = assemblyName.Name ?? "Unknown",
                    Version = assemblyName.Version?.ToString() ?? "0.0.0",
                    Description = $"Module loaded from {assemblyName.Name}",
                    EntryPoint = $"/{module.Name}/"
                };

                registry.RegisterModule(moduleInfo);
            }
        }
    }

    public void ConfigureApplicationParts(ApplicationPartManager partManager)
    {
        // Module discovery happens after services are built, so we'll discover modules in Configure method
    }
}