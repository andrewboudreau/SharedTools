# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SharedTools is a dynamic web module loading library for ASP.NET Core that enables runtime discovery, download, and loading of web modules from NuGet packages. The key innovation is the ability to load complete web applications (including Razor Pages, static assets, and dependencies) as modular components at startup.

## Project Structure

This is a .NET 10.0 solution with the following projects:

- **SharedTools.Web**: The core library providing dynamic NuGet package loading and ApplicationPart integration
  - Handles NuGet package discovery, download, and extraction
  - Manages isolated assembly loading with dependency resolution
  - Integrates modules with ASP.NET Core's ApplicationPart system
  - Supports Razor Pages, static assets, and full MVC features

- **SharedTools.Tests**: Testing utilities and shared test resources

- **ExampleWebModule**: Example web module demonstrating proper module structure
  - Shows how to implement IApplicationPartModule interface
  - Includes Razor Pages compiled into the assembly
  - Demonstrates static asset embedding
  - Example of NuGet package dependencies (Azure.Storage.Blobs)

- **ExampleWebApp**: Host application demonstrating module consumption
  - Shows how to load modules from NuGet at startup
  - Demonstrates loading from local NuGet feed (C:\LocalNuGet)
  - Example of loading external modules (ProjectGeoShot.Game)

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

## Development Workflow

1. **Develop Module**: Create or modify a web module implementing IApplicationPartModule
2. **Pack Module**: Run `dotnet pack ExampleWebModule -c Debug` to output to C:\LocalNuGet
3. **Test Module**: Run the ExampleWebApp which loads modules from the local feed
4. **Iterate**: Make changes and repack - the module will be reloaded on next app start

## Core Architecture

### Dynamic Module Loading System

The library provides a complete solution for loading ASP.NET Core web modules from NuGet packages at runtime:

1. **NuGet Integration**: Full NuGet client implementation for package discovery and download
2. **Dependency Resolution**: Recursive resolution of all package dependencies with version conflict handling
3. **Isolated Loading**: Custom AssemblyLoadContext for loading modules with proper dependency isolation
4. **ASP.NET Integration**: Seamless integration with ApplicationParts for Razor Pages and MVC
5. **Asset Management**: Automatic discovery and serving of embedded static assets

### Key Components

**IApplicationPartModule Interface** (`SharedTools.Web/Modules/IApplicationPartModule.cs`)
- Core contract that modules must implement
- `ConfigureServices(IServiceCollection services)` - Register services with DI container
- `Configure(WebApplication app)` - Configure middleware and endpoints
- `ConfigureApplicationParts(ApplicationPartManager partManager)` - Advanced ApplicationPart configuration

**ApplicationPartModuleExtensions** (`SharedTools.Web/Modules/ApplicationPartModuleExtensions.cs`)
- Main entry point for module loading functionality
- `AddApplicationPartModules()` - Discovers, downloads, and loads modules from NuGet
- `UseApplicationPartModules()` - Activates loaded modules in the pipeline

**ModuleAssemblyLoadContext** (`SharedTools.Web/Modules/ModuleAssemblyLoadContext.cs`)
- Custom AssemblyLoadContext providing proper isolation
- Smart delegation strategy: framework assemblies use host versions, module dependencies load isolated
- Handles System.* assemblies by trying module directory first, then delegating to host
- Prevents version conflicts while maintaining compatibility

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

- Static assets in `wwwroot` folder are embedded as resources in the module assembly
- Automatically discovered and served using ManifestEmbeddedFileProvider
- Assets accessible via `_content/{ModuleName}/` path convention
- No extraction step required - faster module loading

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

## Creating Web Modules

### Module Requirements

1. Implement `IApplicationPartModule` interface
2. Use `ConfigureServices()` for dependency injection setup
3. Use `Configure()` for middleware/endpoint configuration
4. Include embedded resources in `wwwroot` folder for static assets
5. Reference `Microsoft.AspNetCore.App` via `<FrameworkReference>`
6. Reference `SharedTools.Web` with `<PrivateAssets>all</PrivateAssets>`

### Module Best Practices

- Use Razor SDK (`Microsoft.NET.Sdk.Razor`) for Razor Pages support
- Set `StaticWebAssetBasePath` to `_content/{ModuleName}`
- Enable `RazorCompileOnBuild` and `RazorCompileOnPublish`
- Include all required dependencies in the NuGet package
- Test with local NuGet feed before publishing

### Module Project Template

The `ExampleWebModule` project demonstrates proper configuration:
- Uses `Microsoft.NET.Sdk.Razor` SDK for Razor support
- Sets `StaticWebAssetBasePath` for proper asset routing
- Configures local NuGet output in Debug mode (`C:\LocalNuGet`)
- Includes proper framework and package references

## Usage Example

The `ExampleWebApp` demonstrates loading modules:

```csharp
// Program.cs
// Load modules from NuGet (uses nuget.config for source configuration)
await builder.AddApplicationPartModules(["ExampleWebModule", "ProjectGeoShot.Game"]);

var app = builder.Build();

// ... configure pipeline ...

// Activate loaded modules
app.UseApplicationPartModules();
```

Modules are automatically discovered, downloaded, extracted, and integrated into the ASP.NET Core pipeline.

## Key Features

- **Runtime Module Loading**: Load complete ASP.NET Core modules from NuGet at application startup
- **Full Dependency Resolution**: Automatically resolves and downloads all transitive dependencies
- **Assembly Isolation**: Prevents version conflicts between modules and host application
- **Razor Pages Support**: Modules can include compiled Razor Pages and views
- **Static Asset Handling**: Embedded static assets served via ManifestEmbeddedFileProvider
- **Development Workflow**: Local NuGet feed support for rapid development iteration

## Module Management Hub

### Overview

SharedTools.ModuleManagement is a special module that provides a web-based management interface for viewing and managing loaded modules. Since modules are loaded dynamically at runtime from NuGet packages, this management hub discovers loaded modules and provides:

- Overview page listing all installed modules
- Module details including version, dependencies, and exposed endpoints
- Direct links to visit module entry points
- Module health and status information

### Implementation Requirements

The module management hub itself is implemented as an IApplicationPartModule and must:

1. **Track Module Loading**: Hook into the module loading process to maintain a registry of loaded modules
2. **Expose Module Metadata**: Collect and display information about each module including:
   - Module name and version
   - Exposed routes and endpoints
   - Static asset paths
   - Service registrations
3. **Provide Navigation**: Create a central hub for navigating between modules
4. **Self-Register**: The management module must be discoverable by itself

### Architecture Considerations

- The hub needs access to ApplicationPartManager to discover loaded assemblies
- It should register early in the pipeline to track other module registrations
- Module metadata should be collected during the ConfigureServices phase
- UI should be accessible at a well-known route (e.g., `/_modules`)

## Testing

The SharedTools.Tests project provides utilities for testing web modules and shared functionality. Tests should verify module loading, service registration, and proper isolation between modules.

## Troubleshooting

### Common Issues

1. **FileNotFoundException for dependencies**: Usually indicates missing transitive dependencies. The ModuleAssemblyLoadContext now checks module directory first for System.* assemblies before delegating to host.

2. **Module not loading**: Check that the module implements IApplicationPartModule and is properly packed as a NuGet package.

3. **Static assets not found**: Ensure assets are embedded in wwwroot folder and StaticWebAssetBasePath is configured correctly.