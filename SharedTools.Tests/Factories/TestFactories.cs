using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedTools.Web.Modules;

namespace SharedTools.Tests.WebApplicationFactoryTests;

/// <summary>
/// Marker class for WebApplicationFactory entry point discovery.
/// </summary>
public class TestProgram { }

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
/// Simplified factory for testing without modules.
/// Overrides CreateHost directly to bypass DeferredHostBuilder entry point discovery.
/// </summary>
public class BasicWebApplicationFactory : WebApplicationFactory<TestProgram>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var app = TestableProgram.CreateApplicationAsync([], []).GetAwaiter().GetResult();

        app.MapGet("/", () => Results.Content(
            "<html><body><h1>Test Application</h1></body></html>", "text/html"));

        return app;
    }
}