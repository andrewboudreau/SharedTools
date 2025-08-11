using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ExampleWebModule.Pages.Modules;

public class IndexModel : PageModel
{
    private readonly IExampleBlobStorage storage;

    public IndexModel(IExampleBlobStorage storage)
    {
        this.storage = storage;
    }

    public async Task OnGet()
    {
        await storage.SaveAsync(new ExampleData
        {
            Id = Guid.CreateVersion7(),
            Name = "Test Data"
        });
    }
}