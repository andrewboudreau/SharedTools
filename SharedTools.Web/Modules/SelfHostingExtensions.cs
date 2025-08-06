using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SharedTools.Web.Modules;

public static class SelfHostingExtensions
{
    /// <summary>
    /// Creates a WebApplication builder pre-configured with module loading capabilities.
    /// This is a convenience method that handles all the standard setup for module hosts.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <param name="packageIds">NuGet package IDs to load as modules</param>
    /// <param name="nuGetRepositoryUrls">Optional NuGet repository URLs (uses nuget.config if not provided)</param>
    /// <param name="urls">Optional URLs for the web host (e.g., "https://localhost:7001", "http://localhost:5001")</param>
    /// <param name="configureDevelopment">Optional action to configure development-specific settings</param>
    /// <param name="configureServices">Optional action to configure additional services</param>
    /// <param name="configureApp">Optional action to configure additional middleware/endpoints</param>
    /// <returns>A fully configured WebApplication ready to run</returns>
    public static async Task<WebApplication> CreateModularApplicationAsync(
        string[] args,
        IEnumerable<string> packageIds,
        IEnumerable<string>? nuGetRepositoryUrls = null,
        IEnumerable<string>? urls = null,
        Action<WebApplicationBuilder>? configureDevelopment = null,
        Action<IServiceCollection>? configureServices = null,
        Action<WebApplication>? configureApp = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure URLs if provided
        if (urls?.Any() == true)
        {
            builder.WebHost.UseUrls([.. urls]);
        }

        // Add standard services
        builder.Services.AddRazorPages();

        // Allow additional service configuration
        configureServices?.Invoke(builder.Services);

        // Load modules from NuGet packages
        await builder.AddApplicationPartModules(packageIds, nuGetRepositoryUrls);

        var app = builder.Build();

        // Configure development environment
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            configureDevelopment?.Invoke(builder);
        }
        else
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        // Standard middleware
        app.UseStaticFiles();
        app.MapRazorPages();

        // Configure loaded modules
        app.UseApplicationPartModules();

        // Allow additional app configuration
        configureApp?.Invoke(app);

        return app;
    }

    /// <summary>
    /// Creates a simple self-hosted module application.
    /// Perfect for module development and testing.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <param name="packageId">The module package ID to load</param>
    /// <param name="localNuGetPath">Path to local NuGet repository (defaults to C:\LocalNuGet)</param>
    /// <param name="httpsPort">HTTPS port (defaults to 7001)</param>
    /// <param name="httpPort">HTTP port (defaults to 5001)</param>
    /// <returns>A configured WebApplication ready to run</returns>
    public static async Task<WebApplication> CreateSelfHostedModuleAsync(
        string[] args,
        string packageId,
        string localNuGetPath = "C:\\LocalNuGet",
        int httpsPort = 7001,
        int httpPort = 5001)
    {
        return await CreateSelfHostedModuleAsync(args, [packageId], localNuGetPath, httpsPort, httpPort);
    }

    public static async Task<WebApplication> CreateSelfHostedModuleAsync(
        string[] args,
        string[] packageIds,
        string localNuGetPath = "C:\\LocalNuGet",
        int httpsPort = 7001,
        int httpPort = 5001)
    {
        return await CreateModularApplicationAsync(
            args: args,
            packageIds: packageIds,
            nuGetRepositoryUrls: [localNuGetPath],
            urls: [$"https://localhost:{httpsPort}", $"http://localhost:{httpPort}"],
            configureApp: app =>
            {
                foreach (var packageId in packageIds)
                {
                    Console.WriteLine($"🚀 {packageId} loaded from NuGet: https://localhost:{httpsPort}/{packageId}");
                }
            });
    }
}