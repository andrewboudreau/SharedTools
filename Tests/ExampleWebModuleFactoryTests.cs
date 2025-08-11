using Microsoft.AspNetCore.Mvc.Testing;

using System.Net;

namespace Tests;

[TestClass]
public class ExampleWebModuleFactoryTests
{
    private WebApplicationFactory<ExampleWebApp.Program>? _factory;

    [TestInitialize]
    public void Setup()
    {
        _factory = new WebApplicationFactory<ExampleWebApp.Program>();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _factory?.Dispose();
    }

    [TestMethod]
    public async Task Application_ShouldStart_Successfully()
    {
        // Arrange
        var client = _factory!.CreateClient();

        // Act
        var response = await client.GetAsync("/");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "Application should start and respond to root request");
    }

    [TestMethod]
    public async Task ExampleWebModule_ApiEndpoint_ShouldReturnModuleInfo()
    {
        // Arrange
        var client = _factory!.CreateClient();

        // Act
        var response = await client.GetAsync("/example-module/info");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "Module info endpoint should be accessible");

        var content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("ExampleWebModule", StringComparison.OrdinalIgnoreCase),
            "Response should contain module name");
        Assert.IsTrue(content.Contains("Active", StringComparison.OrdinalIgnoreCase),
            "Response should indicate module is active");
    }

    [TestMethod]
    public async Task ExampleWebModule_StaticAssets_ShouldBeAccessible()
    {
        // Arrange
        var client = _factory!.CreateClient();

        // Act
        var stylesResponse = await client.GetAsync("/_content/ExampleWebModule/styles.css");
        var backgroundResponse = await client.GetAsync("/_content/ExampleWebModule/background.png");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, stylesResponse.StatusCode,
            "styles.css should be accessible at /_content/ExampleWebModule/styles.css");

        Assert.AreEqual(HttpStatusCode.OK, backgroundResponse.StatusCode,
            "background.png should be accessible at /_content/ExampleWebModule/background.png");

        // Verify content types
        Assert.AreEqual("text/css", stylesResponse.Content.Headers.ContentType?.MediaType,
            "CSS file should have correct content type");

        Assert.AreEqual("image/png", backgroundResponse.Content.Headers.ContentType?.MediaType,
            "PNG file should have correct content type");

        // Verify content
        var cssContent = await stylesResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(cssContent.Contains("background-size: cover"),
            "CSS should contain expected content");
    }

    [TestMethod]
    public async Task ExampleWebModule_RazorPage_ShouldBeAccessible()
    {
        // Arrange
        var client = _factory!.CreateClient();

        // Act - Try different possible routes for the module's Razor page
        var responses = new[]
        {
            await client.GetAsync("/ExampleWebModule"),
            await client.GetAsync("/ExampleWebModule/Index"),
            await client.GetAsync("/ExampleWebModule/")
        };

        // Assert - At least one route should work
        var successfulResponse = responses.FirstOrDefault(r => r.StatusCode == HttpStatusCode.OK);
        Assert.IsNotNull(successfulResponse,
            "At least one ExampleWebModule Razor page route should be accessible");

        var content = await successfulResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("html", StringComparison.OrdinalIgnoreCase),
            "Response should contain HTML");
    }

    [TestMethod]
    public async Task ModuleManagement_IndexPage_ShouldBeAccessible()
    {
        // Arrange
        var client = _factory!.CreateClient();

        // Act - Try different possible routes for the module management page
        var responses = new[]
        {
            await client.GetAsync("/SharedTools.ModuleManagement"),
            await client.GetAsync("/SharedTools.ModuleManagement/Index"),
            await client.GetAsync("/SharedTools.ModuleManagement/")
        };

        // Assert - At least one route should work
        var successfulResponse = responses.FirstOrDefault(r => r.StatusCode == HttpStatusCode.OK);
        Assert.IsNotNull(successfulResponse,
            "Module Management page should be accessible at one of the expected routes");

        var content = await successfulResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("html", StringComparison.OrdinalIgnoreCase),
            "Response should contain HTML");

        // The page should show information about loaded modules
        Assert.IsTrue(content.Contains("Module", StringComparison.OrdinalIgnoreCase),
            "Page should contain module information");
    }

    [TestMethod]
    public async Task Application_ShouldLoadBothModules()
    {
        // Arrange
        var client = _factory!.CreateClient();

        // Act & Assert - Both modules should be accessible
        var exampleModuleResponse = await client.GetAsync("/example-module/info");
        Assert.AreEqual(HttpStatusCode.OK, exampleModuleResponse.StatusCode,
            "ExampleWebModule should be loaded and accessible");

        var moduleManagementResponse = await client.GetAsync("/SharedTools.ModuleManagement");
        if (moduleManagementResponse.StatusCode == HttpStatusCode.NotFound)
        {
            moduleManagementResponse = await client.GetAsync("/SharedTools.ModuleManagement/");
        }
        Assert.AreEqual(HttpStatusCode.OK, moduleManagementResponse.StatusCode,
            "ModuleManagement should be loaded and accessible");
    }

    [TestMethod]
    public async Task Application_MultipleRequests_ShouldHandleConcurrency()
    {
        // Arrange
        var client = _factory!.CreateClient();
        const int concurrentRequests = 5;

        // Act - Make multiple concurrent requests
        var tasks = new List<Task<HttpResponseMessage>>();
        for (int i = 0; i < concurrentRequests; i++)
        {
            tasks.Add(client.GetAsync("/example-module/info"));
            tasks.Add(client.GetAsync("/_content/ExampleWebModule/styles.css"));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert - All requests should succeed
        foreach (var response in responses)
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
                "All concurrent requests should succeed");
        }
    }

    [TestMethod]
    public async Task Application_NonExistentModuleResource_ShouldReturn404()
    {
        // Arrange
        var client = _factory!.CreateClient();

        // Act
        var response = await client.GetAsync("/_content/ExampleWebModule/nonexistent.css");

        // Assert
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode,
            "Non-existent module resources should return 404");
    }
}
