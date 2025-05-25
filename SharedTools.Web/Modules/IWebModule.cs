using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace SharedTools.Web.Modules;

/// <summary>  
/// Contract that WebModule plugins implement to register their features with the host.  
/// </summary>  
public interface IWebModule
{
    void ConfigureBuilder(WebApplicationBuilder builder);

    void ConfigureApp(WebApplication app);
}
