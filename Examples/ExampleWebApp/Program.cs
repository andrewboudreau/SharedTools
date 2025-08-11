using SharedTools.Web.Modules;

namespace ExampleWebApp;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddRazorPages();

        // Use the new ApplicationPart-based module system
        // This will download packages, resolve dependencies, and register them as ApplicationParts
        await builder.AddApplicationPartModules([
            "SharedTools.ExampleWebModule",
            "SharedTools.ModuleManagement"
        ],
        nuGetRepositoryUrls: ["C:\\LocalNuget\\"]);

        var app = builder.Build();

        app.UseStaticFiles();
        app.MapRazorPages();

        // Configure modules - this calls each module's Configure method
        app.UseApplicationPartModules();

        app.Run();
    }
}
