using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using SharedTools.Web.Modules;

namespace SharedTools.ModuleTemplate;

/// <summary>
/// A SharedTools web module. Publish it (<c>dotnet publish -o out</c>), zip the output and upload it
/// to a module host; the host loads it at startup and calls <see cref="Configure"/>. Files under
/// wwwroot are published with it and served by the host at <c>/_content/{Name}/</c>.
/// </summary>
public class Module : IApplicationPartModule
{
    // The module's name drives its home (/{Name}/) and static file path (/_content/{Name}/).
    // Shorten this if you want a tidier route than the full project name.
    public string Name => "SharedTools.ModuleTemplate";

    public void ConfigureServices(IServiceCollection services)
    {
        // Register this module's services here. If this throws, none of them are kept and the
        // host falls back to the previous drop.
        //
        // For configuration, bind a strongly-typed options class from this module's own
        // "Modules:{Name}" config section (environment: Modules__{Name}__Setting):
        //
        //   services.AddModuleOptions<MyOptions>(Name).ValidateOnStart();
    }

    public void Configure(WebApplication app)
    {
        // Serve the module's home page from its published wwwroot.
        app.MapGet($"/{Name}/", () => Page("index.html"));

        // Add this module's API endpoints here, for example:
        //   app.MapGet($"/{Name}/api/hello", () => Results.Ok(new { hello = Name }));
    }

    // Serves an HTML page from the module's wwwroot, or 404 if it is missing.
    private IResult Page(string fileName)
    {
        var path = Path.Combine(this.WebRoot(), fileName);
        return File.Exists(path)
            ? Results.File(path, "text/html; charset=utf-8")
            : Results.NotFound();
    }
}
