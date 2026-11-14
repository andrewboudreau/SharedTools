using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedTools.ModuleManagement;
using SharedTools.Web.Modules;

namespace SharedTools.Tests.WebApplicationFactoryTests;

/// <summary>
/// Marker class for WebApplicationFactory entry point discovery.
/// </summary>
public class TestProgram { }

/// <summary>
/// A temporary managed module store, seeded from the drops the test project's build publishes
/// to <c>{output}/drops/{Id}</c>. Deleted on dispose.
/// </summary>
public sealed class TestModuleStore : IDisposable
{
    public static string BuiltDrop(string id) => Path.Combine(AppContext.BaseDirectory, "drops", id);

    public TestModuleStore()
    {
        Root = Path.Combine(Path.GetTempPath(), "sharedtools-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Store = new ModuleStore(Root);
    }

    public string Root { get; }
    public ModuleStore Store { get; }

    /// <summary>Copies a built drop into the store as <c>{id}</c> (or <c>{id}.prev</c>) with a <c>.drop</c> id.</summary>
    public string AddDrop(string builtId, string dropId, string? id = null, bool previous = false)
    {
        id ??= builtId;
        var target = previous ? Store.PreviousDirectory(id) : Store.ModuleDirectory(id);
        CopyDirectory(BuiltDrop(builtId), target);
        ModuleStore.WriteDropId(target, dropId);
        return target;
    }

    /// <summary>Marks a drop as just uploaded, the way the upload API does.</summary>
    public void MarkPending(string id, string dropId, string? serving = null) =>
        Store.WriteStatus(id, new ModuleDropStatus(dropId, ModuleDropStatus.Pending, null, null, serving, DateTimeOffset.UtcNow));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Module assemblies stay loaded (non-collectible contexts); on Windows their files stay locked.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"Built drop not found at {source}; build the test project first.");
        }

        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        }
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
        }
    }
}

/// <summary>Builds a test host the way a real module host does: AddModules, the hub, UseModules.</summary>
public static class TestHost
{
    public static WebApplication Create(string? moduleRoot, IDictionary<string, string?>? settings = null, bool withHub = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var config = new Dictionary<string, string?>
        {
            // The example module validates its options on start.
            ["Modules:ExampleWebModule:ConnectionString"] = "UseDevelopmentStorage=true",
            ["Modules:ExampleWebModule:ExamplesContainerName"] = "example-data",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            config[key] = value;
        }
        builder.Configuration.AddInMemoryCollection(config);

        builder.Services.AddRazorPages();

        if (moduleRoot is not null)
        {
            builder.AddModules(moduleRoot);
        }
        if (withHub)
        {
            builder.AddModule<ModuleManagementModule>();
        }

        var app = builder.Build();

        app.UseExceptionHandler(err => err.Run(async context =>
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("Internal Server Error");
        }));
        app.UseStaticFiles();
        app.MapRazorPages();
        app.UseModules();

        app.MapGet("/", () => Results.Content(
            "<html><body><h1>Test Application</h1></body></html>", "text/html"));

        return app;
    }
}

/// <summary>
/// A host with the example module loaded from a managed store, plus the module hub.
/// </summary>
public class ModularWebApplicationFactory : WebApplicationFactory<TestProgram>
{
    private readonly TestModuleStore store = new();

    public ModularWebApplicationFactory()
    {
        store.AddDrop("ExampleWebModule", dropId: "example-1");
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var app = TestHost.Create(store.Root);
        app.Start();
        return app;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            store.Dispose();
        }
    }
}

/// <summary>
/// A host with no modules.
/// </summary>
public class BasicWebApplicationFactory : WebApplicationFactory<TestProgram>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var app = TestHost.Create(moduleRoot: null, withHub: false);
        app.Start();
        return app;
    }
}
