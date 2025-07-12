# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Structure

This is a .NET 10.0 solution with three main projects:

- **SharedTools.Web**: Core web utilities and dynamic module loading system
- **SharedTools.Tests**: Testing utilities and shared test resources
- **ExampleWebModule**: Template/example web module demonstrating proper module creation

All projects are packaged as NuGet packages with MIT licensing.

Additional projects:
- **ExampleWebApp**: Demonstrates how to consume web modules (loads ProjectGeoShot.Game and local modules)

## Build and Development Commands

### Build the solution
```bash
dotnet build
```

### Build in release mode
```bash
dotnet build -c Release
```

### Run tests
```bash
dotnet test
```

### Run the example application
```bash
cd ExampleWebApp
dotnet run
```

### Pack NuGet packages
```bash
dotnet pack -c Release
```

### Pack modules to local NuGet feed (for development)
```bash
dotnet pack ExampleWebModule -c Debug
```
Note: ExampleWebModule is configured to output to `C:\LocalNuGet` in Debug mode for local testing.

### Clean build artifacts
```bash
dotnet clean
```

## Core Architecture

### Web Module System
The main architectural pattern is a dynamic web module loading system that allows runtime discovery and loading of web modules from:

1. **NuGet packages**: Automatic discovery, download, and extraction from NuGet feeds with full dependency resolution
2. **Explicit assemblies**: Processing of already-loaded assemblies for development scenarios

### Key Components

**IWebModule Interface** (`SharedTools.Web/Modules/IWebModule.cs:9`)
- Contract for web module plugins
- `ConfigureBuilder(WebApplicationBuilder builder)` - Configure services
- `ConfigureApp(WebApplication app)` - Configure middleware/endpoints

**WebModuleExtensions** (`SharedTools.Web/Modules/WebModuleExtensions.cs:19`)
- Core extension methods for web module discovery and loading
- `AddWebModules()` - Load modules from NuGet packages with dependency resolution
- `UseWebModules()` - Configure loaded modules in the pipeline

**WebModuleLoadContext** (`SharedTools.Web/Modules/WebModuleLoadContext.cs:6`)
- Custom AssemblyLoadContext for isolated module loading
- Uses AssemblyDependencyResolver for dependency resolution
- Implements selective assembly delegation - shared contracts load from host, private dependencies load isolated

### Assembly Processing Pipeline

1. **Dependency Resolution**: Recursively resolve all NuGet package dependencies using prioritized repository search
2. **Download**: Download packages from configured NuGet sources with fallback to nuget.org
3. **Flat Extraction**: Extract all assemblies to a single "flat" directory for proper dependency resolution
4. **Isolated Loading**: Use WebModuleLoadContext to load main assembly with selective delegation
5. **Type Discovery**: Find concrete types implementing IWebModule interface
6. **ASP.NET Integration**: Register assemblies with ApplicationPartManager for MVC/Razor discovery
7. **Service Configuration**: Call ConfigureBuilder() for service registration
8. **Pipeline Configuration**: Call ConfigureApp() for middleware/endpoint setup

### Static Asset Handling

- Embedded `wwwroot` resources are automatically discovered and registered using ManifestEmbeddedFileProvider
- Uses CompositeFileProvider to combine host and module static assets
- Assets accessible via `_content/{ModuleName}/` path convention

### Key Dependencies

- **Microsoft.Extensions.DependencyInjection.Abstractions**: Dependency injection abstractions
- **NuGet.Packaging & NuGet.Protocol**: NuGet package discovery, download, and extraction
- **Microsoft.AspNetCore.App**: ASP.NET Core framework (via FrameworkReference)

### Configuration Sources

The system supports multiple NuGet configuration approaches:
1. Explicit repository URLs passed to `AddWebModules()`
2. Discovery from `nuget.config` files in the project directory
3. Fallback to default nuget.org source

### Local Development Setup

The ExampleWebApp includes a `nuget.config` that configures:
- **Local Feed**: `C:\LocalNuGet` for locally built modules
- **Official NuGet**: Standard nuget.org source
- **Visual Studio Offline**: Local Visual Studio packages

This enables testing locally built modules before publishing to public NuGet feeds.

## Working with Web Modules

When creating or modifying web modules:

1. Implement `IWebModule` interface (`SharedTools.Web/Modules/IWebModule.cs:9`)
2. Use `ConfigureBuilder()` for service registration and dependency injection setup
3. Use `ConfigureApp()` for middleware/endpoint configuration 
4. Include embedded resources in `wwwroot` folder for static assets
5. Compile views into the assembly for Razor support
6. Reference `Microsoft.AspNetCore.App` via `<FrameworkReference>` to avoid conflicts
7. Use the Options Pattern for configuration with validation
8. Reference `SharedTools.Web` with `<PrivateAssets>all</PrivateAssets>` to avoid bundling

### Module Project Template

The `ExampleWebModule` project demonstrates proper configuration:
- Uses `Microsoft.NET.Sdk.Razor` SDK for Razor support
- Sets `StaticWebAssetBasePath` for proper asset routing
- Configures local NuGet output in Debug mode (`C:\LocalNuGet`)
- Includes proper framework and package references

## Example Usage

The `ExampleWebApp` demonstrates basic usage:

```csharp
// Program.cs
await builder.AddWebModules(["ProjectGeoShot.Game"]);
var app = builder.Build();
// ... configure pipeline ...
app.UseWebModules();
```

## Testing

The SharedTools.Tests project provides utilities for testing web modules and shared functionality. Tests should verify module loading, service registration, and proper isolation between modules.