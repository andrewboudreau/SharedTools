using Microsoft.AspNetCore.Mvc.RazorPages;
using SharedTools.ModuleManagement.Services;

namespace SharedTools.ModuleManagement.Pages.Modules;

public class IndexModel : PageModel
{
    private readonly ModuleManagementSystem moduleManagementSystem;

    public IndexModel(ModuleManagementSystem moduleManagementSystem)
    {
        this.moduleManagementSystem = moduleManagementSystem;
    }

    public IEnumerable<ModuleInfo> Modules { get; private set; } = [];
    public int ModuleCount => moduleManagementSystem.ModuleCount;

    public void OnGet()
    {
        Modules = moduleManagementSystem.GetAllModules();
    }
}