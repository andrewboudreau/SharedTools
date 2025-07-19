#r "nuget: Microsoft.Extensions.DependencyInjection.Abstractions, 10.0.0"
#r "ExampleWebModule/bin/Debug/net10.0/ExampleWebModule.dll"

using System.Reflection;

try
{
    var assemblyPath = @"ExampleWebModule\bin\Debug\net10.0\ExampleWebModule.dll";
    var assembly = Assembly.LoadFrom(assemblyPath);
    Console.WriteLine($"Loaded assembly: {assembly.FullName}");
    
    var types = assembly.GetTypes();
    Console.WriteLine($"Total types: {types.Length}");
    
    foreach (var type in types)
    {
        Console.WriteLine($"  - {type.FullName}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Stack: {ex.StackTrace}");
}