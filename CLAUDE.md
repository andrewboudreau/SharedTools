# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Structure

This is a .NET 9.0 solution with two main projects:

- **SharedTools.Web**: Core web utilities and dynamic module loading system
- **SharedTools.Tests**: Testing utilities and shared test resources

Both projects are packaged as NuGet packages with MIT licensing.

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

### Pack NuGet packages
```bash
dotnet pack -c Release
```

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

### Assembly Processing Pipeline

1. **Download/Extract**: Get assemblies from NuGet packages with full dependency resolution
2. **Load**: Use WebModuleLoadContext for isolated loading
3. **Discover**: Find types implementing IWebModule
4. **Register**: Add to ApplicationPartManager for MVC/Razor discovery
5. **Configure**: Call ConfigureBuilder() then ConfigureApp()

### Static Asset Handling

- Embedded `wwwroot` resources are automatically discovered and registered
- NuGet package content files are extracted to the web root
- Uses CompositeFileProvider for file system integration

### Dependencies

- **Microsoft.Extensions.DependencyInjection.Abstractions**: DI abstractions
- **NuGet.Packaging & NuGet.Protocol**: NuGet package handling
- **Microsoft.AspNetCore.App**: Web framework foundation

## Working with Web Modules

When creating or modifying web modules:

1. Implement `IWebModule` interface
2. Use `ConfigureBuilder()` for service registration
3. Use `ConfigureApp()` for middleware/endpoint configuration
4. Include embedded resources in `wwwroot` folder for static assets
5. Compile views into the assembly for Razor support

## Testing

The SharedTools.Tests project provides utilities for testing web modules and shared functionality. Tests should verify module loading, service registration, and proper isolation between modules.