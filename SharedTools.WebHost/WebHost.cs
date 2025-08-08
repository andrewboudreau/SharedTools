using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using SharedTools.Web.Modules;

namespace SharedTools.WebHost;

public static class WebHost
{
    public static async Task<WebApplication> CreateAsync(string[] args, params string[] modules)
    {
        var builder = WebApplication.CreateBuilder(args);
        await builder.AddDefaults(modules);

        var app = builder.Build() ?? throw new InvalidOperationException("There was an error building the WebApplicationBuilder.");
        return app.UseDefaults();
    }

    public static async Task<WebApplication> CreateAsync(params string[] modules)
    {
        return await CreateAsync([], modules);
    }
}

public static class WebHostExtensions
{
    public static async Task AddDefaults(this WebApplicationBuilder builder, params string[] modules)
    {
        // Add default services
        builder.Services.AddRazorPages();

        // Load modules if any specified
        if (modules.Length > 0)
        {
            await builder.AddApplicationPartModules(modules);
        }
    }

    public static WebApplication UseDefaults(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseStaticFiles();
        app.UseRouting();
        app.MapRazorPages();
        app.UseApplicationPartModules();

        return app;
    }
}