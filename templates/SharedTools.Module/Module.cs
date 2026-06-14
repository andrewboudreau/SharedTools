using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;

using SharedTools.Web.Modules;

namespace SharedTools.ModuleTemplate;

/// <summary>
/// A SharedTools web module. A host discovers and loads this from NuGet at startup, then
/// calls <see cref="Configure"/> to wire up its endpoints. Files under wwwroot are embedded
/// in this assembly and served by the host at <c>/_content/{Name}/</c>.
/// </summary>
public class Module : IApplicationPartModule
{
    // The module's name drives its routes (/{Name}/) and static asset path (/_content/{Name}/).
    // Shorten this if you want a tidier route than the full package id.
    public string Name => "SharedTools.ModuleTemplate";

    // The module's wwwroot is embedded in this assembly; HTML pages are served from here.
    private static readonly string PagePrefix =
        $"{typeof(Module).Assembly.GetName().Name}.wwwroot.";

    public void ConfigureServices(IServiceCollection services)
    {
        // Register this module's services here.
        //
        // For configuration, bind a strongly-typed options class from this module's own
        // "Modules:{Name}" config section (requires SharedTools.Web with AddModuleOptions):
        //
        //   services.AddModuleOptions<MyOptions>(Name).ValidateOnStart();
    }

    public void Configure(WebApplication app)
    {
        // Serve the module's home page from the embedded wwwroot.
        app.MapGet($"/{Name}/", () => Page("index.html"));

        // Add this module's API endpoints here, for example:
        //   app.MapGet($"/{Name}/api/hello", () => Results.Ok(new { hello = Name }));
    }

    public void ConfigureApplicationParts(ApplicationPartManager partManager)
    {
        // Usually empty - the loader registers this assembly's parts automatically.
    }

    // Serves an embedded HTML page from the module's wwwroot, or 404 if it is missing.
    private static IResult Page(string fileName)
    {
        var stream = typeof(Module).Assembly.GetManifestResourceStream(PagePrefix + fileName);
        return stream is not null
            ? Results.Stream(stream, "text/html; charset=utf-8")
            : Results.NotFound();
    }
}
