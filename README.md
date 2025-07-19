# SharedTools - Dynamic Web Module Loading for ASP.NET Core

SharedTools is a dynamic web module loading library for ASP.NET Core that enables runtime discovery, download, and loading of web modules from NuGet packages. The key innovation is the ability to load complete web applications (including Razor Pages, static assets, and dependencies) as modular components at startup.

## Overview

SharedTools enables ASP.NET Core applications to dynamically load and integrate new features at runtime. These features, called "Application Part Modules," are packaged as standard NuGet packages and can be stored in any NuGet-compatible repository. This allows for extending applications with new APIs, web pages, services, and static assets without requiring redeployment.

## Key Features

- **Runtime Module Loading**: Load complete ASP.NET Core modules from NuGet at application startup
- **Full Dependency Resolution**: Automatically resolves and downloads all transitive dependencies
- **Assembly Isolation**: Prevents version conflicts between modules and host application
- **Razor Pages Support**: Modules can include compiled Razor Pages and views
- **Static Asset Handling**: Automatic discovery and serving of embedded static files
- **Development Workflow**: Local NuGet feed support for rapid development iteration

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

## Quick Start

### Install from NuGet

```bash
dotnet add package SharedTools.Web
```

### Basic Usage

```csharp
// Program.cs
// Load modules from NuGet (uses nuget.config for source configuration)
await builder.AddApplicationPartModules(["ExampleWebModule", "ProjectGeoShot.Game"]);

var app = builder.Build();

// ... configure pipeline ...

// Activate loaded modules
app.UseApplicationPartModules();
```

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

## Creating Application Part Modules

### Setting Up Your Project File (`.csproj`)

The project file is the foundation of a well-behaved module. It must be configured to correctly declare its dependencies and its relationship with the host application's framework.

Start with the `Microsoft.NET.Sdk.Razor` SDK, which supports both API and UI components.

### Key `.csproj` Configuration

Below is a template for your module's `.csproj` file. Pay close attention to the comments explaining each part.

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">

  <PropertyGroup>
    <!-- IMPORTANT: Target the same .NET version as the host application. -->
    <TargetFramework>net10.0</TargetFramework> 
    
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <!-- This ensures static assets (css, js, images) from your module's wwwroot folder are available. -->
    <StaticWebAssetBasePath>_content/YourModuleName</StaticWebAssetBasePath>

    <!-- NuGet Package Properties: Fill these out for your module. -->
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    <PackageId>YourCompany.YourModuleName</PackageId>
    <Version>1.0.0</Version> 
    <Authors>Your Name</Authors>
    <Description>A brief description of what your module does.</Description>
  </PropertyGroup>

  <!-- ==================================================================== -->
  <!-- CRITICAL: FRAMEWORK AND CONTRACT REFERENCES                          -->
  <!-- ==================================================================== -->
  <ItemGroup>
    <!-- 
      This is the MOST IMPORTANT part. <FrameworkReference> tells the build process
      that the entire ASP.NET Core runtime is provided by the host. 
      This prevents your module from bundling its own copies of ASP.NET Core DLLs,
      which would cause version conflicts and `MissingMethodException` errors.
    -->
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  
  <ItemGroup>
    <!--
      This references the shared IWebModule interface. <PrivateAssets>all</PrivateAssets>
      is essential. It means:
      1. Use this for compilation.
      2. DO NOT list it as a runtime dependency in the final NuGet package.
      This solves the "type identity" problem, ensuring your module and the host
      are using the exact same IWebModule type from memory.
    -->
    <PackageReference Include="SharedTools.Web" Version="1.0.0">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <!-- ==================================================================== -->
  <!-- PRIVATE MODULE DEPENDENCIES                                          -->
  <!-- ==================================================================== -->
  <ItemGroup>
    <!--
      List all other dependencies your module needs here as standard <PackageReference>s.
      These are considered "private" to your module. The runtime loader will download
      them and place them in your module's isolated load context.
    -->
    <PackageReference Include="Azure.Storage.Blobs" Version="12.19.1" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>

</Project>
```

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

## Handling Configuration with the Options Pattern

Your module should **never** access the host's `IConfiguration` directly with magic strings (e.g., `builder.Configuration["MySetting"]`). Instead, use the strongly-typed **Options Pattern**. This creates a clean, validated, and isolated configuration contract.

### Step 1: Define Your Options Class

Create a class that represents the settings your module needs. This class *is* your configuration contract.

**`YourModuleName/ModuleOptions.cs`**
```csharp
using System.ComponentModel.DataAnnotations;

namespace YourCompany.YourModuleName;

public class ModuleOptions
{
    // Define a constant for the expected configuration section name.
    // This makes it easy for the host to configure your module.
    public const string SectionName = "YourModuleName";

    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;

    [Range(1, 100)]
    public int DefaultPageSize { get; set; } = 25;

    public Uri? EndpointUrl { get; set; }
}
```

### Step 2: Register and Use Your Options

In your `IApplicationPartModule` implementation, register your options class and bind it to the configuration section.

**`YourModuleName/Module.cs`**
```csharp
using Microsoft.Extensions.Options;
using SharedTools.Web.Modules;
// ... other usings

public class Module : IApplicationPartModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        // 1. Register your options class with the DI container.
        //    - Bind it to the section defined in your options class.
        //    - Add validation to fail fast at startup if configuration is missing/invalid.
        services.AddOptions<ModuleOptions>()
            .BindConfiguration(ModuleOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // 2. Register your module's services. They can now request IOptions<ModuleOptions>
        //    via constructor injection.
        services.AddHttpClient<MyModuleService>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ModuleOptions>>().Value;
            client.BaseAddress = options.EndpointUrl;
            client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
        });
    }

    public void Configure(WebApplication app)
    {
        // ... configure endpoints or middleware ...
    }
    
    public void ConfigureApplicationParts(ApplicationPartManager partManager)
    {
        // Optional: Advanced ApplicationPart configuration if needed
    }
}
```

### Step 3: Document for the Host Administrator

The host application's administrator can now configure your module in `appsettings.json` with a clear, namespaced structure.

**`HostApp/appsettings.json`**
```json
{
  // ... other host settings ...
  
  "YourModuleName": {
    "ApiKey": "abc-123-your-secret-key",
    "DefaultPageSize": 50,
    "EndpointUrl": "https://api.example.com/v2/"
  }
}
```

## Module Implementation Checklist

-   [ ] Does your `.csproj` use `<FrameworkReference Include="Microsoft.AspNetCore.App" />`?
-   [ ] Does your reference to SharedTools.Web include `<PrivateAssets>all</PrivateAssets>`?
-   [ ] Are all other dependencies standard `<PackageReference>`s?
-   [ ] Is all configuration handled via a strongly-typed `Options` class?
-   [ ] Does your `Options` class include validation attributes (`[Required]`, `[Range]`, etc.)?
-   [ ] Does your `ConfigureServices` method call `.ValidateDataAnnotations().ValidateOnStart()` for your options?
-   [ ] Have you filled out the NuGet package properties in your `.csproj`?

## Core Architecture

### Dynamic Module Loading System

The library provides a complete solution for loading ASP.NET Core web modules from NuGet packages at runtime:

1. **NuGet Integration**: Full NuGet client implementation for package discovery and download
2. **Dependency Resolution**: Recursive resolution of all package dependencies with version conflict handling
3. **Isolated Loading**: Custom AssemblyLoadContext for loading modules with proper dependency isolation
4. **ASP.NET Integration**: Seamless integration with ApplicationParts for Razor Pages and MVC
5. **Asset Management**: Automatic discovery and serving of embedded static assets

### Architectural Overview

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
4. **Isolated Loading**: Use ModuleAssemblyLoadContext to load main assembly with selective delegation
5. **Type Discovery**: Find concrete types implementing IApplicationPartModule interface
6. **ASP.NET Integration**: Register assemblies with ApplicationPartManager for MVC/Razor discovery
7. **Service Configuration**: Call ConfigureServices() for service registration
8. **Pipeline Configuration**: Call Configure() for middleware/endpoint setup

### Static Asset Handling

- Embedded `wwwroot` resources are automatically discovered and registered using ManifestEmbeddedFileProvider
- Uses CompositeFileProvider to combine host and module static assets
- Assets accessible via `_content/{ModuleName}/` path convention

### Configuration Sources

The system supports multiple NuGet configuration approaches:
1. Explicit repository URLs passed to `AddApplicationPartModules()`
2. Discovery from `nuget.config` files in the project directory
3. Fallback to default nuget.org source

### Local Development Setup

The ExampleWebApp includes a `nuget.config` that configures:
- **Local Feed**: `C:\LocalNuGet` for locally built modules
- **Official NuGet**: Standard nuget.org source
- **Visual Studio Offline**: Local Visual Studio packages

This enables testing locally built modules before publishing to public NuGet feeds.

## Troubleshooting

### Common Issues

1. **FileNotFoundException for dependencies**: Usually indicates missing transitive dependencies. The ModuleAssemblyLoadContext now checks module directory first for System.* assemblies before delegating to host.

2. **Module not loading**: Check that the module implements IApplicationPartModule and is properly packed as a NuGet package.

3. **Static assets not found**: Ensure assets are embedded in wwwroot folder and StaticWebAssetBasePath is configured correctly.

## Testing

The SharedTools.Tests project provides utilities for testing web modules and shared functionality. Tests should verify module loading, service registration, and proper isolation between modules.

## Contributing

Contributions are welcome! Please ensure all changes maintain compatibility with the existing module loading system and include appropriate tests.

## License

This project is licensed under the MIT License - see the LICENSE file for details.
