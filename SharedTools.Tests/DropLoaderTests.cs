using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharedTools.Tests.WebApplicationFactoryTests;
using SharedTools.Web.Modules;

namespace SharedTools.Tests;

/// <summary>
/// The loader's contract with the on-disk store: fallback, crash recovery, status, isolation.
/// Uses the TestModule drop, whose ConfigureServices throws when its drop contains a <c>throw</c> file.
/// </summary>
[TestClass]
public class DropLoaderTests
{
    private TestModuleStore store = null!;

    [TestInitialize]
    public void Setup() => store = new TestModuleStore();

    [TestCleanup]
    public void Cleanup() => store.Dispose();

    private static async Task<(WebApplication App, string Body)> StartAndGet(string root, string path)
    {
        var app = TestHost.Create(root, withHub: false);
        await app.StartAsync();
        var client = app.GetTestClient();
        var response = await client.GetAsync(path);
        var body = response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : $"HTTP {(int)response.StatusCode}";
        return (app, body);
    }

    [TestMethod]
    public async Task LoadsDrop_ServesEndpointsAndStaticFiles_AndWritesLoadedStatus()
    {
        store.AddDrop("TestModule", "d1");
        store.MarkPending("TestModule", "d1");

        var (app, body) = await StartAndGet(store.Root, "/test-module/drop");
        await using var _ = app;

        Assert.AreEqual("d1", body);
        var file = await app.GetTestClient().GetStringAsync("/_content/TestModule/hello.txt");
        StringAssert.Contains(file, "hello from the test module");

        var status = store.Store.ReadStatus("TestModule");
        Assert.IsNotNull(status);
        Assert.AreEqual(ModuleDropStatus.Loaded, status.Status);
        Assert.AreEqual("d1", status.Drop);
        Assert.AreEqual("d1", status.Serving);
        Assert.IsFalse(store.Store.BootMarkerExists, "Boot marker should be cleared once the host has started");
    }

    [TestMethod]
    public async Task ConfigureServicesThrows_FallsBackToPrevious_WithNoStrayServices()
    {
        store.AddDrop("TestModule", "good", previous: true);
        var live = store.AddDrop("TestModule", "bad");
        File.WriteAllText(Path.Combine(live, "throw"), "");
        store.MarkPending("TestModule", "bad", serving: "good");

        var (app, body) = await StartAndGet(store.Root, "/test-module/drop");
        await using var _ = app;

        Assert.AreEqual("good", body, "The previous drop should be serving");
        Assert.AreEqual("bad", ModuleStore.ReadDropId(store.Store.PreviousDirectory("TestModule")),
            "The failed drop should have been swapped into .prev on disk");

        var registry = app.Services.GetRequiredService<ModuleRegistry>();
        var serviceTypes = app.Services.GetRequiredService<IServiceProviderIsService>();
        var testModuleTypes = registry.Loaded.Single(l => l.Id == "TestModule").Module.GetType().Assembly.GetTypes();
        Assert.IsTrue(serviceTypes.IsService(testModuleTypes.Single(t => t.Name == "Marker")));

        // The failing drop's ThrowingMarker lives in its own load context; no service of that name may exist.
        var failedAssemblyLoaded = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetName().Name == "SharedTools.Tests.TestModule" ? a.GetTypes() : [])
            .Where(t => t.Name == "ThrowingMarker");
        Assert.IsFalse(failedAssemblyLoaded.Any(serviceTypes.IsService), "Services from a failed ConfigureServices must be discarded");

        var status = store.Store.ReadStatus("TestModule")!;
        Assert.AreEqual(ModuleDropStatus.Failed, status.Status);
        Assert.AreEqual(nameof(ModuleLoadPhase.ConfigureServices), status.Phase);
        Assert.AreEqual("bad", status.Drop);
        Assert.AreEqual("good", status.Serving);
    }

    [TestMethod]
    public async Task NoDepsJson_FailsLoad_AndFallsBack()
    {
        store.AddDrop("TestModule", "good", previous: true);
        var live = store.AddDrop("TestModule", "broken");
        File.Delete(Path.Combine(live, "SharedTools.Tests.TestModule.deps.json"));
        store.MarkPending("TestModule", "broken", serving: "good");

        var (app, body) = await StartAndGet(store.Root, "/test-module/drop");
        await using var _ = app;

        Assert.AreEqual("good", body);
        var status = store.Store.ReadStatus("TestModule")!;
        Assert.AreEqual(nameof(ModuleLoadPhase.Load), status.Phase);
    }

    [TestMethod]
    public async Task CrashDuringStartup_RollsBackPendingDrop()
    {
        store.AddDrop("TestModule", "good", previous: true);
        store.AddDrop("TestModule", "crashy");
        store.MarkPending("TestModule", "crashy", serving: "good");
        store.Store.WriteBootMarker(); // left behind by a host that died while starting

        var (app, body) = await StartAndGet(store.Root, "/test-module/drop");
        await using var _ = app;

        Assert.AreEqual("good", body);
        var status = store.Store.ReadStatus("TestModule")!;
        Assert.AreEqual(ModuleDropStatus.Failed, status.Status);
        Assert.AreEqual(nameof(ModuleLoadPhase.Crash), status.Phase);
        Assert.AreEqual("crashy", status.Drop);
        Assert.AreEqual("good", status.Serving);
    }

    [TestMethod]
    public async Task CrashDuringStartup_LeavesLoadedDropsAlone()
    {
        store.AddDrop("TestModule", "d1");
        store.Store.WriteStatus("TestModule", new ModuleDropStatus("d1", ModuleDropStatus.Loaded, null, null, "d1", DateTimeOffset.UtcNow));
        store.Store.WriteBootMarker();

        var (app, body) = await StartAndGet(store.Root, "/test-module/drop");
        await using var _ = app;

        Assert.AreEqual("d1", body);
        Assert.AreEqual(ModuleDropStatus.Loaded, store.Store.ReadStatus("TestModule")!.Status);
    }

    [TestMethod]
    public async Task CrashDuringStartup_WithNoPrevious_DisablesDrop()
    {
        store.AddDrop("TestModule", "crashy");
        store.MarkPending("TestModule", "crashy");
        store.Store.WriteBootMarker();

        var (app, body) = await StartAndGet(store.Root, "/test-module/drop");
        await using var _ = app;

        Assert.AreEqual("HTTP 404", body);
        Assert.IsTrue(Directory.Exists(store.Store.DisabledDirectory("TestModule")));
        Assert.IsFalse(Directory.Exists(store.Store.ModuleDirectory("TestModule")));
        var status = store.Store.ReadStatus("TestModule")!;
        Assert.AreEqual(ModuleDropStatus.Failed, status.Status);
        Assert.IsNull(status.Serving);
    }

    [TestMethod]
    public async Task CrashAgainAfterRollback_DisablesInsteadOfSwappingBack()
    {
        // First crash: rolled back to "good", which is recorded on disk before it loads.
        store.AddDrop("TestModule", "good", previous: true);
        store.AddDrop("TestModule", "crashy");
        store.MarkPending("TestModule", "crashy", serving: "good");
        store.Store.WriteBootMarker();

        var builder = WebApplication.CreateBuilder();
        builder.AddModules(store.Root); // simulate a host that dies before it starts: no StartAsync

        Assert.AreEqual("good", ModuleStore.ReadDropId(store.Store.ModuleDirectory("TestModule")));
        Assert.IsTrue(store.Store.BootMarkerExists);

        // Second crash: the fallback is not swapped back; it is disabled.
        var (app, body) = await StartAndGet(store.Root, "/test-module/drop");
        await using var _ = app;

        Assert.AreEqual("HTTP 404", body);
        Assert.AreEqual("good", ModuleStore.ReadDropId(store.Store.DisabledDirectory("TestModule")));
        Assert.AreEqual("crashy", ModuleStore.ReadDropId(store.Store.PreviousDirectory("TestModule")));
    }

    [TestMethod]
    public async Task SameAssemblyTwice_SecondIsRejected()
    {
        store.AddDrop("TestModule", "a", id: "first");
        store.AddDrop("TestModule", "b", id: "second");

        var (app, body) = await StartAndGet(store.Root, "/test-module/drop");
        await using var _ = app;

        Assert.AreEqual("a", body);
        var registry = app.Services.GetRequiredService<ModuleRegistry>();
        var failure = registry.Failures.Single();
        Assert.AreEqual("second", failure.ModuleId);
        Assert.AreEqual(ModuleLoadPhase.Load, failure.Phase);
        StringAssert.Contains(failure.Error, "already loaded");
    }

    [TestMethod]
    public async Task MissingRoot_LoadsNothing()
    {
        var (app, body) = await StartAndGet(Path.Combine(store.Root, "does-not-exist"), "/test-module/drop");
        await using var _ = app;

        Assert.AreEqual("HTTP 404", body);
        Assert.IsFalse(Directory.Exists(Path.Combine(store.Root, "does-not-exist")));
    }

    [TestMethod]
    public async Task ModulePaths_LoadsUnmanagedDrop()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Modules:Paths:TestModule"] = TestModuleStore.BuiltDrop("TestModule");
        builder.AddModules(Path.Combine(store.Root, "none"));
        await using var app = builder.Build();
        app.UseModules();
        await app.StartAsync();

        var body = await app.GetTestClient().GetStringAsync("/test-module/drop");
        Assert.AreEqual("none", body); // built drops have no .drop file
    }
}
