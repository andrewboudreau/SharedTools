using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SharedTools.ModuleManagement.Services;
using SharedTools.Web.Modules;

namespace SharedTools.ModuleManagement;

/// <summary>
/// The module hub: lists every module the host loaded (and every one that failed) at <c>/_modules</c>.
/// Add it to a host with <c>builder.AddModule&lt;ModuleManagementModule&gt;()</c>, or upload it as a drop.
/// </summary>
public class ModuleManagementModule : IApplicationPartModule
{
    public const string HubPath = "/_modules";

    public string Name => "SharedTools.ModuleManagement";

    public string Home => HubPath;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ModuleManagementSystem>();
        // The hub's well-known URL is /_modules; the folder-based route
        // (/SharedTools.ModuleManagement) keeps working for existing links and monitors.
        services.AddRazorPages(options =>
            options.Conventions.AddPageRoute("/SharedTools.ModuleManagement/Index", HubPath.TrimStart('/')));
    }

    public void Configure(WebApplication app)
    {
        app.MapRazorPages();

        var managementSystem = app.Services.GetRequiredService<ModuleManagementSystem>();
        var registry = app.Services.GetService<ModuleRegistry>();
        if (registry is null)
        {
            return;
        }

        foreach (var loaded in registry.Loaded)
        {
            var module = loaded.Module;
            var assemblyName = module.GetType().Assembly.GetName();
            var key = loaded.Id ?? module.Name;

            // A failure for a module that is still serving means a newer drop failed and was rolled back.
            var rolledBack = registry.Failures.LastOrDefault(f =>
                string.Equals(f.ModuleId, key, StringComparison.OrdinalIgnoreCase) && f.Drop != loaded.Drop);

            managementSystem.RegisterModule(new ModuleInfo
            {
                Name = module.GetType() == GetType() ? "Module Management" : module.Name,
                AssemblyName = assemblyName.Name ?? key,
                Version = assemblyName.Version?.ToString() ?? "0.0.0",
                Description = module.GetType() == GetType()
                    ? "Lists the modules this host loaded at startup"
                    : null,
                EntryPoint = module.Home,
                Drop = loaded.Drop,
                Source = loaded.Directory is null ? "host"
                    : registry.Store is not null && loaded.Directory.StartsWith(registry.Store.Root, StringComparison.OrdinalIgnoreCase) ? "drop"
                    : "Modules:Paths",
                Warning = rolledBack is null
                    ? null
                    : $"Drop {rolledBack.Drop ?? "(unknown)"} failed during {rolledBack.Phase} and was rolled back: {rolledBack.Error}"
            });
        }

        // Modules with nothing serving: they failed and had no previous drop to fall back to.
        foreach (var failure in registry.Failures)
        {
            var serving = registry.Loaded.Any(l =>
                string.Equals(l.Id ?? l.Module.Name, failure.ModuleId, StringComparison.OrdinalIgnoreCase));
            if (serving)
            {
                continue;
            }

            managementSystem.RegisterModule(new ModuleInfo
            {
                Name = failure.ModuleId,
                AssemblyName = failure.ModuleId,
                Version = "—",
                Description = $"Drop {failure.Drop ?? "(unknown)"} failed during {failure.Phase}.",
                Drop = failure.Drop,
                Status = ModuleStatus.Failed,
                Error = failure.Error
            });
        }
    }
}
