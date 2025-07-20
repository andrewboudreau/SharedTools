using Microsoft.AspNetCore.Mvc.RazorPages;
using SharedTools.ModuleManagement.Services;

namespace SharedTools.ModuleManagement.Pages.Modules;

public class IndexModel : PageModel
{
    private readonly ModuleRegistry moduleRegistry;

    public IndexModel(ModuleRegistry moduleRegistry)
    {
        this.moduleRegistry = moduleRegistry;
    }

    public IEnumerable<ModuleInfo> Modules { get; private set; } = [];
    public int ModuleCount => moduleRegistry.ModuleCount;

    public void OnGet()
    {
        Modules = moduleRegistry.GetAllModules();
    }
}