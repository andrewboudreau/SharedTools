using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Razor.Compilation;
using Microsoft.Extensions.DependencyInjection;
using SharedTools.Web.Modules;
using System.Reflection;

var builder = WebApplication.CreateBuilder();
builder.Services.AddRazorPages();

// Get the ApplicationPartManager
var mvcBuilder = builder.Services.AddRazorPages();
var partManager = mvcBuilder.PartManager;

// Load module assembly
var moduleAssemblyPath = @"C:\Users\andre\source\repos\SharedTools\ExampleWebModule\bin\Debug\net10.0\ExampleWebModule.dll";
var moduleAssembly = Assembly.LoadFrom(moduleAssemblyPath);
Console.WriteLine($"Module Assembly Loaded: {moduleAssembly.FullName}");

// Add as ApplicationPart
partManager.ApplicationParts.Add(new AssemblyPart(moduleAssembly));
partManager.ApplicationParts.Add(new CompiledRazorAssemblyPart(moduleAssembly));

var app = builder.Build();

// List all application parts
Console.WriteLine("\nApplication Parts:");
foreach (var part in partManager.ApplicationParts)
{
    Console.WriteLine($"  - {part.GetType().Name}: {part.Name}");
}

// Check for compiled Razor views
var viewsFeature = new ViewsFeature();
partManager.PopulateFeature(viewsFeature);
Console.WriteLine($"\nCompiled Views Found: {viewsFeature.ViewDescriptors.Count}");
foreach (var view in viewsFeature.ViewDescriptors)
{
    Console.WriteLine($"  - {view.RelativePath} (Type: {view.Type?.FullName})");
}

// Check manifest resources
Console.WriteLine($"\nManifest Resources in Module Assembly:");
foreach (var resource in moduleAssembly.GetManifestResourceNames())
{
    Console.WriteLine($"  - {resource}");
}

// Check for Razor Page types
var pageTypes = moduleAssembly.GetTypes().Where(t => t.Name.EndsWith("Model") && t.Namespace?.Contains("Pages") == true);
Console.WriteLine($"\nRazor Page Model Types:");
foreach (var pageType in pageTypes)
{
    Console.WriteLine($"  - {pageType.FullName}");
}