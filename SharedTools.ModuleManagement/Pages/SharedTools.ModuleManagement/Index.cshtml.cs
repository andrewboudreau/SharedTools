using Microsoft.AspNetCore.Mvc.RazorPages;
using SharedTools.ModuleManagement.Services;

namespace SharedTools.ModuleManagement.Pages.Modules;

public class IndexModel : PageModel
{
    private readonly ModuleManagementSystem moduleManagementSystem;
    private readonly INuGetVersionService nuGetVersionService;

    public IndexModel(ModuleManagementSystem moduleManagementSystem, INuGetVersionService nuGetVersionService)
    {
        this.moduleManagementSystem = moduleManagementSystem;
        this.nuGetVersionService = nuGetVersionService;
    }

    public IEnumerable<ModuleInfo> Modules { get; private set; } = [];
    public int ModuleCount => moduleManagementSystem.ModuleCount;

    public async Task OnGetAsync()
    {
        var modules = moduleManagementSystem.GetAllModules().ToList();

        var latestVersions = await nuGetVersionService.GetLatestVersionsAsync(
            modules.Select(m => m.AssemblyName),
            HttpContext.RequestAborted);

        foreach (var module in modules)
        {
            if (latestVersions.TryGetValue(module.AssemblyName, out var latest))
            {
                module.LatestNuGetVersion = latest;
            }
        }

        Modules = modules;
    }
}
