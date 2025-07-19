using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Razor.Compilation;
using Microsoft.AspNetCore.Razor.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

Console.WriteLine("Testing ExampleWebModule Razor Pages integration...\n");

// Check the module assembly directly
var moduleAssembly = typeof(ExampleWebModule.ExampleApplicationPartModule).Assembly;
Console.WriteLine($"Module Assembly: {moduleAssembly.FullName}");

// Check for manifest resources (compiled Razor views)
var resources = moduleAssembly.GetManifestResourceNames();
Console.WriteLine($"\nManifest Resources ({resources.Length}):");
foreach (var resource in resources)
{
    Console.WriteLine($"  - {resource}");
}

// Check for Razor compiled types
var razorCompiledTypes = moduleAssembly.GetTypes()
    .Where(t => t.FullName?.Contains("Views_") == true || t.FullName?.Contains("Pages_") == true)
    .ToList();
Console.WriteLine($"\nRazor Compiled Types ({razorCompiledTypes.Count}):");
foreach (var type in razorCompiledTypes)
{
    Console.WriteLine($"  - {type.FullName}");
}

// Test with ApplicationPartManager
Console.WriteLine("\nTesting with ApplicationPartManager...");
var builder = WebApplication.CreateBuilder();
builder.Services.AddRazorPages();

var mvcBuilder = builder.Services.AddRazorPages();
var partManager = mvcBuilder.PartManager;

// Add module assembly as parts
partManager.ApplicationParts.Add(new AssemblyPart(moduleAssembly));
partManager.ApplicationParts.Add(new CompiledRazorAssemblyPart(moduleAssembly));

// Check ViewsFeature
var viewsFeature = new ViewsFeature();
partManager.PopulateFeature(viewsFeature);
Console.WriteLine($"\nViews found via ViewsFeature: {viewsFeature.ViewDescriptors.Count}");
foreach (var view in viewsFeature.ViewDescriptors)
{
    Console.WriteLine($"  - {view.RelativePath} => {view.Type?.FullName}");
}

// Check for RazorCompiledItemAttribute
var compiledItems = moduleAssembly.GetCustomAttributes<RazorCompiledItemAttribute>().ToList();
Console.WriteLine($"\nRazorCompiledItemAttributes ({compiledItems.Count}):");
foreach (var item in compiledItems)
{
    Console.WriteLine($"  - {item.Identifier} => {item.Type.FullName}");
}

Console.WriteLine("\nDone.");