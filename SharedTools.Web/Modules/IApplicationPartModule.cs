using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;

namespace SharedTools.Web.Modules;

/// <summary>
/// Modern interface for web modules that integrate with ASP.NET Core's ApplicationParts system.
/// Modules implementing this interface can contribute controllers, views, and other MVC components.
/// </summary>
public interface IApplicationPartModule
{
    /// <summary>
    /// Gets the name of the module for identification and static asset routing.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Configures services for the module during the host's service configuration phase.
    /// This is called before the application is built.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    void ConfigureServices(IServiceCollection services);

    /// <summary>
    /// Configures the module's application parts, allowing it to contribute controllers,
    /// views, and other MVC components to the application.
    /// </summary>
    /// <param name="applicationPartManager">The application part manager to configure.</param>
    void ConfigureApplicationParts(ApplicationPartManager applicationPartManager);

    /// <summary>
    /// Configures the application pipeline for the module after the host is built.
    /// This is where modules can add middleware, endpoints, etc.
    /// </summary>
    /// <param name="app">The web application to configure.</param>
    void Configure(WebApplication app);
}