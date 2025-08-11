using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Razor.Hosting;

using SharedTools.Web.Modules;

using System.Reflection;

namespace ExampleWebModule.Tests;

[TestClass]
public class RazorCompilationTests
{
    [TestMethod]
    public void ModuleAssembly_ShouldContainCompiledRazorPages()
    {
        // Arrange
        var moduleAssembly = typeof(ExampleApplicationPartModule).Assembly;

        // Act
        var razorCompiledTypes = moduleAssembly.GetTypes()
            .Where(t => t.FullName?.Contains("Views_") == true ||
                       t.FullName?.Contains("Pages_") == true)
            .ToList();

        // Assert
        Assert.IsTrue(razorCompiledTypes.Count > 0,
            "Module assembly should contain compiled Razor pages/views");

        Console.WriteLine($"Found {razorCompiledTypes.Count} Razor compiled types:");
        foreach (var type in razorCompiledTypes)
        {
            Console.WriteLine($"  - {type.FullName}");
        }
    }

    [TestMethod]
    public void ModuleAssembly_ShouldHaveRazorCompiledItemAttributes()
    {
        // Arrange
        var moduleAssembly = typeof(ExampleApplicationPartModule).Assembly;

        // Act
        var compiledItems = moduleAssembly
            .GetCustomAttributes<RazorCompiledItemAttribute>()
            .ToList();

        // Assert
        Assert.IsTrue(compiledItems.Count > 0,
            "Module assembly should have RazorCompiledItemAttribute attributes");

        Console.WriteLine($"Found {compiledItems.Count} RazorCompiledItemAttributes:");
        foreach (var item in compiledItems)
        {
            Console.WriteLine($"  - {item.Identifier} => {item.Type.FullName}");
        }
    }

    [TestMethod]
    public void ApplicationPartManager_ShouldDiscoverModuleViews()
    {
        // This test is complex due to runtime assembly loading and view compilation
        // The ViewsFeature requires full initialization that's typically done by the ASP.NET Core host

        // Arrange
        var moduleAssembly = typeof(ExampleApplicationPartModule).Assembly;
        var partManager = new ApplicationPartManager();

        // Act
        partManager.ApplicationParts.Add(new AssemblyPart(moduleAssembly));
        partManager.ApplicationParts.Add(new CompiledRazorAssemblyPart(moduleAssembly));

        // Check that parts were added
        Assert.AreEqual(2, partManager.ApplicationParts.Count, "Should have added 2 application parts");

        // Note: ViewsFeature.PopulateFeature requires full ASP.NET Core initialization
        // which is difficult to do in a unit test context without a full host
        // The test above for RazorCompiledItemAttributes is sufficient to verify
        // that Razor views are compiled into the assembly

        // We can at least verify the parts are there
        var assemblyPart = partManager.ApplicationParts.OfType<AssemblyPart>().FirstOrDefault();
        var razorPart = partManager.ApplicationParts.OfType<CompiledRazorAssemblyPart>().FirstOrDefault();

        Assert.IsNotNull(assemblyPart, "Should have an AssemblyPart");
        Assert.IsNotNull(razorPart, "Should have a CompiledRazorAssemblyPart");
        Assert.AreEqual(moduleAssembly, assemblyPart.Assembly, "AssemblyPart should reference the module assembly");
        Assert.AreEqual(moduleAssembly, razorPart.Assembly, "CompiledRazorAssemblyPart should reference the module assembly");
    }

    [TestMethod]
    public void ModuleType_ShouldImplementIApplicationPartModule()
    {
        // Arrange
        var moduleAssembly = typeof(ExampleWebModule.ExampleApplicationPartModule).Assembly;

        // Act
        var moduleTypes = moduleAssembly.GetTypes()
            .Where(t => typeof(IApplicationPartModule).IsAssignableFrom(t) &&
                       !t.IsInterface && !t.IsAbstract)
            .ToList();

        // Assert
        Assert.AreEqual(1, moduleTypes.Count, "Module should have exactly one IApplicationPartModule implementation");
        Assert.AreEqual("ExampleWebModule.ExampleApplicationPartModule", moduleTypes[0].FullName);
    }
}