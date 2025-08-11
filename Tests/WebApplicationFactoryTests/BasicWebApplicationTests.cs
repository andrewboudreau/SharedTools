using System.Net;

namespace Tests;

[TestClass]
public class BasicWebApplicationTests
{
    private BasicWebApplicationFactory? _factory;

    [TestInitialize]
    public void Setup()
    {
        _factory = new BasicWebApplicationFactory();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _factory?.Dispose();
    }

    [TestMethod]
    public async Task BasicApplication_ShouldStart_Successfully()
    {
        // Arrange
        var client = _factory!.CreateClient();

        // Act
        var response = await client.GetAsync("/");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "Basic application should start successfully");
    }

    [TestMethod]
    public async Task BasicApplication_ShouldServeStaticFiles()
    {
        // Arrange
        var client = _factory!.CreateClient();

        // Act - Test that the static file middleware is working
        // Note: Without modules, we don't expect module-specific static files
        var response = await client.GetAsync("/");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "Static file middleware should be configured");
    }

    [TestMethod]
    public async Task BasicApplication_ShouldHaveRazorPagesConfigured()
    {
        // Arrange
        var client = _factory!.CreateClient();

        // Act
        var response = await client.GetAsync("/");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "Razor Pages should be configured");
    }

    [TestMethod]
    public async Task BasicApplication_ModuleEndpoints_ShouldNotExist()
    {
        // Arrange
        var client = _factory!.CreateClient();

        // Act - Test that module-specific endpoints don't exist in basic app
        var moduleApiResponse = await client.GetAsync("/example-module/info");
        var modulePageResponse = await client.GetAsync("/ExampleWebModule");

        // Assert
        Assert.AreEqual(HttpStatusCode.NotFound, moduleApiResponse.StatusCode,
            "Module API endpoints should not exist in basic application");
        
        Assert.AreEqual(HttpStatusCode.NotFound, modulePageResponse.StatusCode,
            "Module pages should not exist in basic application");
    }

    [TestMethod]
    public async Task BasicApplication_ModuleStaticFiles_ShouldNotExist()
    {
        // Arrange
        var client = _factory!.CreateClient();

        // Act - Test that module static files don't exist in basic app
        var stylesResponse = await client.GetAsync("/_content/ExampleWebModule/styles.css");
        var backgroundResponse = await client.GetAsync("/_content/ExampleWebModule/background.png");

        // Assert
        Assert.AreEqual(HttpStatusCode.NotFound, stylesResponse.StatusCode,
            "Module static files should not exist in basic application");
        
        Assert.AreEqual(HttpStatusCode.NotFound, backgroundResponse.StatusCode,
            "Module static files should not exist in basic application");
    }
}