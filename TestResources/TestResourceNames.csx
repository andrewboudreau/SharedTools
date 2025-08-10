#r "C:\Users\andre\source\repos\SharedTools\ExampleWebModule\bin\Debug\net10.0\SharedTools.ExampleWebModule.dll"

using System.Reflection;

var assembly = Assembly.LoadFrom(@"C:\Users\andre\source\repos\SharedTools\ExampleWebModule\bin\Debug\net10.0\SharedTools.ExampleWebModule.dll");
var resources = assembly.GetManifestResourceNames();

Console.WriteLine($"Found {resources.Length} resources:");
foreach (var resource in resources)
{
    Console.WriteLine($"  - {resource}");
}