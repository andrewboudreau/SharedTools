using Microsoft.AspNetCore.Mvc.RazorPages;
using SharedTools.ModuleManagement.Services;

namespace SharedTools.ModuleManagement.Pages.Modules;

public class IndexModel : PageModel
{
    private readonly ModuleRegistry _moduleRegistry;

    public IndexModel(ModuleRegistry moduleRegistry)
    {
        _moduleRegistry = moduleRegistry;
    }

    public IEnumerable<ModuleInfo> Modules { get; private set; } = Enumerable.Empty<ModuleInfo>();
    public int ModuleCount => _moduleRegistry.ModuleCount;

    public void OnGet()
    {
        Modules = _moduleRegistry.GetAllModules();
    }
}