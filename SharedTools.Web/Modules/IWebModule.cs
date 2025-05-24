using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace SharedTools.Web.Modules;

/// <summary>  
/// Contract that WebModule plugins implement to register their features with the host.  
/// </summary>  
public interface IWebModule
{
    /// Register DI services needed by the WebModule plugin.  
    void ConfigureServices(IServiceCollection services);

    /// Configure WebModule-specific endpoints or middleware into the app.  
    void Configure(WebApplication app);
}
