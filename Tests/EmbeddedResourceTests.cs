using Microsoft.Extensions.FileProviders;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests;

[TestClass]
public class EmbeddedResourceTests
{
    [TestMethod]
    public void EmbeddedResources_ShouldBeDiscoverable()
    {
        // Arrange
        var assembly = typeof(ExampleWebModule.ExampleApplicationPartModule).Assembly;
        
        // Act
        var resources = assembly.GetManifestResourceNames();
        
        // Assert
        Assert.IsNotNull(resources);
        Assert.IsTrue(resources.Length > 0, "Assembly should contain embedded resources");
        
        // Should contain wwwroot resources
        var wwwrootResources = resources.Where(r => r.Contains("wwwroot.", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.IsTrue(wwwrootResources.Length > 0, "Assembly should contain wwwroot embedded resources");
        
        Console.WriteLine($"Found {wwwrootResources.Length} wwwroot resources:");
        foreach (var resource in wwwrootResources)
        {
            Console.WriteLine($"  - {resource}");
        }
    }

    [TestMethod]
    public void EmbeddedFileProvider_ShouldFindStaticFiles()
    {
        // Arrange
        var assembly = typeof(ExampleWebModule.ExampleApplicationPartModule).Assembly;
        var resources = assembly.GetManifestResourceNames();
        var wwwrootResource = resources.FirstOrDefault(r => r.Contains("wwwroot.", StringComparison.OrdinalIgnoreCase));
        
        Assert.IsNotNull(wwwrootResource, "Should have at least one wwwroot resource");
        
        // Detect the namespace prefix (as done in RegisterModuleStaticAssets)
        var wwwrootIndex = wwwrootResource.IndexOf("wwwroot.", StringComparison.OrdinalIgnoreCase);
        var baseNamespace = wwwrootResource.Substring(0, wwwrootIndex + "wwwroot".Length);
        
        // Act
        var provider = new EmbeddedFileProvider(assembly, baseNamespace);
        var stylesFile = provider.GetFileInfo("styles.css");
        var backgroundFile = provider.GetFileInfo("background.png");
        
        // Assert
        Assert.IsTrue(stylesFile.Exists, "styles.css should exist");
        Assert.IsTrue(backgroundFile.Exists, "background.png should exist");
        Assert.IsTrue(stylesFile.Length > 0, "styles.css should have content");
        Assert.IsTrue(backgroundFile.Length > 0, "background.png should have content");
    }

    [TestMethod]
    public void EmbeddedResourceNamespace_ShouldBeDetectedCorrectly()
    {
        // This tests the fix we made for the namespace detection issue
        
        // Arrange
        var assembly = typeof(ExampleWebModule.ExampleApplicationPartModule).Assembly;
        var resources = assembly.GetManifestResourceNames();
        var wwwrootResources = resources.Where(r => r.Contains("wwwroot.", StringComparison.OrdinalIgnoreCase)).ToList();
        
        Assert.IsTrue(wwwrootResources.Any(), "Should have wwwroot resources");
        
        // Act - simulate the namespace detection logic
        var firstResource = wwwrootResources.First();
        var wwwrootIndex = firstResource.IndexOf("wwwroot.", StringComparison.OrdinalIgnoreCase);
        var detectedNamespace = firstResource[..(wwwrootIndex + "wwwroot".Length)];
        
        // Assert
        Assert.IsFalse(string.IsNullOrEmpty(detectedNamespace), "Namespace should be detected");
        Assert.IsTrue(detectedNamespace.EndsWith("wwwroot"), "Namespace should end with 'wwwroot'");
        
        // The namespace should NOT be "SharedTools.ExampleWebModule.wwwroot" 
        // but rather "ExampleWebModule.wwwroot" based on our findings
        Assert.AreEqual("ExampleWebModule.wwwroot", detectedNamespace, 
            "Namespace should match the actual embedded resource naming");
    }
}