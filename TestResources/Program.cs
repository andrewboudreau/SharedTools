using System.Reflection;
using Microsoft.Extensions.FileProviders;

var assemblyPath = @"C:\Users\andre\source\repos\SharedTools\ExampleWebModule\bin\Debug\net10.0\SharedTools.ExampleWebModule.dll";
var assembly = Assembly.LoadFrom(assemblyPath);
var resources = assembly.GetManifestResourceNames();

Console.WriteLine($"Assembly: {assembly.FullName}");
Console.WriteLine($"Found {resources.Length} resources:");
foreach (var resource in resources)
{
    Console.WriteLine($"  - {resource}");
}

// Test EmbeddedFileProvider
Console.WriteLine("\nTesting EmbeddedFileProvider with base namespace: SharedTools.ExampleWebModule.wwwroot");
var provider = new EmbeddedFileProvider(assembly, "SharedTools.ExampleWebModule.wwwroot");
var contents = provider.GetDirectoryContents("");

Console.WriteLine($"Files found by EmbeddedFileProvider:");
foreach (var file in contents)
{
    Console.WriteLine($"  - {file.Name} (Exists: {file.Exists}, IsDirectory: {file.IsDirectory})");
}

Console.WriteLine("\nTrying to get styles.css:");
var styleFile = provider.GetFileInfo("styles.css");
Console.WriteLine($"  - styles.css exists: {styleFile.Exists}");
if (styleFile.Exists)
{
    Console.WriteLine($"  - Physical path: {styleFile.PhysicalPath}");
    Console.WriteLine($"  - Length: {styleFile.Length}");
}