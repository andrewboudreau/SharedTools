using Microsoft.AspNetCore.Builder;

namespace SharedTools.Web.Modules;

/// <summary>  
/// Contract that plugins implement to register their features with the host.  
/// </summary>  
public interface IWebModule
{
    /// Register DI services needed by the plugin.  
    void ConfigureServices(WebApplicationBuilder builder);

    /// Configure plugin-specific endpoints or middleware into the app.  
    void Configure(WebApplication app);
}
