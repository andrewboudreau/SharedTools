using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedTools.Web.Modules;

namespace SharedTools.Tests.WebApplicationFactoryTests;

/// <summary>
/// Program class for WebApplicationFactory
/// </summary>
public class TestProgram
{
    public static void Main(string[] args)
    {
        // This is only used by WebApplicationFactory
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddRazorPages();
        var app = builder.Build();
        app.UseStaticFiles();
        app.MapRazorPages();
        app.Run();
    }
}

/// <summary>
/// Testable Program class that encapsulates the ExampleWebApp startup logic
/// This makes WebApplicationFactory testing much simpler
/// </summary>
public class TestableProgram
{
    public static async Task<WebApplication> CreateApplicationAsync(string[] args, params string[] modulePackageIds)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        builder.Services.AddRazorPages();

        // Load modules if specified
        if (modulePackageIds.Length > 0)
        {
            await builder.AddApplicationPartModules(modulePackageIds);
        }
        else
        {
            // When no modules are loaded, we still need to register an empty collection
            // to satisfy UseApplicationPartModules()
            builder.Services.AddSingleton<IReadOnlyCollection<IApplicationPartModule>>(
                new List<IApplicationPartModule>().AsReadOnly());
        }

        var app = builder.Build();

        app.UseStaticFiles();
        app.MapRazorPages();

        // Configure modules - this is safe even with an empty collection
        app.UseApplicationPartModules();

        return app;
    }
}

/// <summary>
/// Factory for testing with modules loaded - using the async workaround
/// </summary>
public class ModularWebApplicationFactory : WebApplicationFactory<TestProgram>
{
    private readonly string[] _modulePackageIds;

    public ModularWebApplicationFactory(params string[] modulePackageIds)
    {
        _modulePackageIds = modulePackageIds ?? [];
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // We need the async workaround because modules require async loading
        var taskCompletionSource = new TaskCompletionSource<IHost>();
        
        Task.Run(async () =>
        {
            try
            {
                var app = await TestableProgram.CreateApplicationAsync([], _modulePackageIds);
                taskCompletionSource.SetResult(app);
            }
            catch (Exception ex)
            {
                taskCompletionSource.SetException(ex);
            }
        });
        
        return taskCompletionSource.Task.GetAwaiter().GetResult();
    }
}

/// <summary>
/// Simplified factory for testing without modules
/// </summary>
public class BasicWebApplicationFactory : WebApplicationFactory<TestProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddRazorPages();
            
            // Register empty module collection for compatibility
            services.AddSingleton<IReadOnlyCollection<IApplicationPartModule>>(
                new List<IApplicationPartModule>().AsReadOnly());
        });

        builder.Configure(app =>
        {
            app.UseStaticFiles();
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                
                // Add a simple root endpoint for basic tests
                endpoints.MapGet("/", async context =>
                {
                    await context.Response.WriteAsync("<html><body><h1>Test Application</h1></body></html>");
                });
            });
        });
    }
}