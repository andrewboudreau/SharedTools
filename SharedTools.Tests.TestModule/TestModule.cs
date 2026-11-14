using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedTools.Web.Modules;

namespace SharedTools.Tests.TestModule;

/// <summary>Registered by every drop that loads.</summary>
public sealed class Marker;

/// <summary>Registered just before ConfigureServices throws; must never survive into the host.</summary>
public sealed class ThrowingMarker;

/// <summary>
/// A test module whose behavior is driven by files in its drop: a <c>throw</c> file makes
/// ConfigureServices throw, and <c>/test-module/drop</c> reports the serving drop's <c>.drop</c> id.
/// </summary>
public sealed class TestModule : IApplicationPartModule
{
    public string Name => "TestModule";

    public void ConfigureServices(IServiceCollection services)
    {
        if (File.Exists(Path.Combine(this.ContentRoot(), "throw")))
        {
            services.AddSingleton<ThrowingMarker>();
            throw new InvalidOperationException("TestModule was told to throw.");
        }

        services.AddSingleton<Marker>();
    }

    public void Configure(WebApplication app)
    {
        var dropFile = Path.Combine(this.ContentRoot(), ".drop");
        var drop = File.Exists(dropFile) ? File.ReadAllText(dropFile).Trim() : "none";
        app.MapGet("/test-module/drop", () => Results.Text(drop));
    }
}
