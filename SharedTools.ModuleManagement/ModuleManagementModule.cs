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
        services.AddHttpClient<INuGetVersionService, NuGetVersionService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SharedTools.ModuleManagement/1.0");
        });
        // The hub's well-known URL is /_modules; the folder-based route
        // (/SharedTools.ModuleManagement) keeps working for existing links and monitors.
        services.AddRazorPages(options =>
            options.Conventions.AddPageRoute("/SharedTools.ModuleManagement/Index", HubPath.TrimStart('/')));
    }

    public const string HubPath = "/_modules";

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
            EntryPoint = HubPath
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

        // Surface modules that failed to load, so they're visible instead of silently dropped.
        var registry = app.Services.GetService<ModuleRegistry>();
        if (registry != null)
        {
            foreach (var failure in registry.Failures)
            {
                managementSystem.RegisterModule(new ModuleInfo
                {
                    Name = failure.ModuleId,
                    AssemblyName = failure.ModuleId,
                    Version = "—",
                    Description = $"Failed during {failure.Phase}.",
                    Status = ModuleStatus.Failed,
                    Error = failure.Error
                });
            }
        }
    }

    public void ConfigureApplicationParts(ApplicationPartManager partManager)
    {
        // Module discovery happens after services are built, so we'll discover modules in Configure method
    }
}