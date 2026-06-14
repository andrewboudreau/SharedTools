using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;

namespace SharedTools.Web.Modules;

/// <summary>
/// Modern interface for web modules that integrate with ASP.NET Core's ApplicationParts system.
/// Modules implementing this interface can contribute Razor Pages, views, and other components.
/// </summary>
public interface IApplicationPartModule
{
    /// <summary>
    /// Gets the name of the module for identification and static asset routing.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Optional: configures services for the module during the host's service configuration phase.
    /// This is called before the application is built. Defaults to a no-op so a trivial module
    /// need only implement Name and Configure.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    void ConfigureServices(IServiceCollection services) { }

    /// <summary>
    /// Optional: configures the module's application parts, allowing it to contribute Razor Pages,
    /// views, and other components. The loader registers assembly parts automatically, so most
    /// modules leave this as the default no-op.
    /// </summary>
    /// <param name="applicationPartManager">The application part manager to configure.</param>
    void ConfigureApplicationParts(ApplicationPartManager applicationPartManager) { }

    /// <summary>
    /// Configures the application pipeline for the module after the host is built.
    /// This is where modules can add middleware, endpoints, etc.
    /// </summary>
    /// <param name="app">The web application to configure.</param>
    void Configure(WebApplication app);
}