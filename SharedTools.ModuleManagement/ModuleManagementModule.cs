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
        // Register our internal management system for the UI
        services.AddSingleton<ModuleManagementSystem>();
        services.AddRazorPages();
    }

    public void Configure(WebApplication app)
    {
        app.MapRazorPages();

        // Get our internal management system for tracking module info
        var managementSystem = app.Services.GetRequiredService<ModuleManagementSystem>();
        
        // Get the main module registry from the ApplicationPart system
        var modules = app.Services.GetService<IReadOnlyCollection<IApplicationPartModule>>();

        // Register this module
        managementSystem.RegisterModule(new ModuleInfo
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

                managementSystem.RegisterModule(moduleInfo);
            }
        }
    }

    public void ConfigureApplicationParts(ApplicationPartManager partManager)
    {
        // Module discovery happens after services are built, so we'll discover modules in Configure method
    }
}