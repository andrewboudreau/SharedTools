using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;

namespace SharedTools.Web.Modules;

/// <summary>
/// A web module: a class library whose <c>dotnet publish</c> output (a "drop") a host loads at startup.
/// Modules can contribute services, endpoints, Razor Pages and static files under <c>wwwroot/</c>.
/// </summary>
public interface IApplicationPartModule
{
    /// <summary>
    /// The module's name. Its static files are served at <c>/_content/{Name}/</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The module's home URL, used by the module hub to link to it. Defaults to <c>/{Name}/</c>.
    /// </summary>
    string Home => $"/{Name}/";

    /// <summary>
    /// Optional: registers the module's services before the host is built. If this throws, none of
    /// its registrations are kept.
    /// </summary>
    void ConfigureServices(IServiceCollection services) { }

    /// <summary>
    /// Optional: advanced ApplicationPart configuration. The loader registers the module assembly's
    /// parts automatically, so most modules leave this as the default no-op.
    /// </summary>
    void ConfigureApplicationParts(ApplicationPartManager applicationPartManager) { }

    /// <summary>
    /// Configures the module's middleware and endpoints after the host is built.
    /// </summary>
    void Configure(WebApplication app);
}
