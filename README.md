# Creating a Web Module for the Dynamic Web Platform

This guide provides the essential tips, patterns, and code snippets for creating a new Web Module. Following these standards is crucial for ensuring your module loads correctly and operates reliably within the host application.

## 1. Overview

A Web Module is a self-contained feature packaged as a NuGet package. It can add new APIs, web pages, background services, and other functionality to the main web application at runtime, without requiring a server restart. Our platform achieves this by dynamically loading your module's NuGet package and integrating it into the ASP.NET Core pipeline.

## 2. Setting Up Your Project File (`.csproj`)

The project file is the foundation of a well-behaved module. It must be configured to correctly declare its dependencies and its relationship with the host application's framework.

Start with the `Microsoft.NET.Sdk.Razor` SDK, which supports both API and UI components.

### Key `.csproj` Configuration

Below is a template for your module's `.csproj` file. Pay close attention to the comments explaining each part.

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">

  <PropertyGroup>
    <!-- IMPORTANT: Target the same .NET version as the host application. -->
    <TargetFramework>net8.0</TargetFramework> 
    
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
    <PackageReference Include="SharedTools.Web.Abstractions" Version="1.0.0">
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

## 3. Handling Configuration with the Options Pattern

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

In your `IWebModule` implementation, register your options class and bind it to the configuration section.

**`YourModuleName/Module.cs`**
```csharp
using Microsoft.Extensions.Options;
// ... other usings

public class Module : IWebModule
{
    public void ConfigureBuilder(WebApplicationBuilder builder)
    {
        // 1. Register your options class with the DI container.
        //    - Bind it to the section defined in your options class.
        //    - Add validation to fail fast at startup if configuration is missing/invalid.
        builder.Services.AddOptions<ModuleOptions>()
            .BindConfiguration(ModuleOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // 2. Register your module's services. They can now request IOptions<ModuleOptions>
        //    via constructor injection.
        builder.Services.AddHttpClient<MyModuleService>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ModuleOptions>>().Value;
            client.BaseAddress = options.EndpointUrl;
            client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
        });
    }

    public void ConfigureApp(WebApplication app)
    {
        // ... configure endpoints or middleware ...
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

## 4. Module Implementation Checklist

-   [ ] Does your `.csproj` use `<FrameworkReference Include="Microsoft.AspNetCore.App" />`?
-   [ ] Does your reference to the shared contract package include `<PrivateAssets>all</PrivateAssets>`?
-   [ ] Are all other dependencies standard `<PackageReference>`s?
-   [ ] Is all configuration handled via a strongly-typed `Options` class?
-   [ ] Does your `Options` class include validation attributes (`[Required]`, `[Range]`, etc.)?
-   [ ] Does your `ConfigureBuilder` method call `.ValidateDataAnnotations().ValidateOnStart()` for your options?
-   [ ] Have you filled out the NuGet package properties in your `.csproj`?

By following these guidelines, you will create modules that are robust, maintainable, and integrate seamlessly into our dynamic web platform.


### Architectural Overview: A Dynamic NuGet-Based Web Module System

Here is a detailed overview of the system you've built. This documentation covers the goals, key components, and the process flow.

#### 1. High-Level Goal

The system's primary goal is to enable an ASP.NET Core web application to dynamically load and integrate new features at runtime. These features, called "Web Modules," are packaged as standard NuGet packages and can be stored in any NuGet-compatible repository. This allows for extending a running application with new APIs, web pages, services, and static assets without requiring a full redeployment or server restart.

#### 2. Core Architectural Components

1.  **The Host Application:**
    *   An ASP.NET Core web application.
    *   It is responsible for initiating the module loading process.
    *   It defines the list of top-level module package IDs to load from its `appsettings.json` or other configuration sources.
    *   It provides the "shared framework," including ASP.NET Core itself and the core module contract.

2.  **The Shared Contract (`SharedTools.Web.Abstractions` / `SharedTools.Web`):**
    *   A minimal NuGet package containing the `IWebModule` interface.
    *   This is the **only** thing that is truly shared between the host and all modules. It defines the bootstrap contract: `ConfigureBuilder(WebApplicationBuilder builder)` and `ConfigureApp(WebApplication app)`.
    *   It may also contain strongly-typed `Options` classes used for configuration contracts between the host and modules.

3.  **Web Modules (`ProjectGeoShot.Game`, etc.):**
    *   Standard .NET Razor Class Library projects packaged as `.nupkg` files.
    *   They implement the `IWebModule` interface.
    *   They reference the shared contract package with `<PrivateAssets>all</PrivateAssets>` to ensure they can compile against the interface without trying to bundle it.
    *   They reference the ASP.NET Core framework via `<FrameworkReference Include="Microsoft.AspNetCore.App" />` to ensure they don't package framework DLLs.
    *   All other dependencies (e.g., `Azure.Storage.Blobs`, `Newtonsoft.Json`) are standard `<PackageReference>`s, making them "private" to the module.

4.  **The Runtime Module Loader (`WebModuleExtensions`):**
    *   A static extension class that contains the core logic for the entire system.
    *   It is responsible for the entire end-to-end loading process.

5.  **The Isolated Load Context (`WebModuleLoadContext`):**
    *   A custom `System.Runtime.Loader.AssemblyLoadContext`.
    *   Its purpose is to create an isolated "sandbox" for each loaded module and its private dependencies.
    *   It overrides the `Load(AssemblyName)` method to implement a critical delegation strategy:
        *   If an assembly is a **shared framework component** (e.g., `Microsoft.AspNetCore.*`, `Microsoft.Extensions.*`) or the **shared contract**, it delegates loading to the host's default context.
        *   If an assembly is a **private dependency**, it uses an `AssemblyDependencyResolver` to load it from the plugin's local directory.

#### 3. The Runtime Loading Process (Step-by-Step)

This flow is initiated by a call to `builder.AddWebModules(...)` in the host's `Program.cs`.

1.  **Dependency Resolution:**
    *   For a given module ID (e.g., "ProjectGeoShot.Game"), the loader connects to the configured NuGet repositories.
    *   It calls `ResolveDependencyGraphAsync` to recursively find all required dependencies (e.g., `Azure.Core`, `Azure.Storage.Blobs`). This produces a complete list of all NuGet packages that need to be installed.

2.  **Downloading:**
    *   The loader iterates through the full list of packages and downloads each one as a stream from the NuGet repository.

3.  **"Flat Directory" Extraction:**
    *   A temporary, unique directory is created for the module and its dependencies (e.g., `C:\Temp\...\ProjectGeoShot.Game.0.1.22_flat\`).
    *   The loader iterates through each downloaded package stream.
    *   Using a `PackageArchiveReader`, it inspects the contents and extracts **only the necessary `.dll` files** from the most compatible framework folder (e.g., `/lib/net8.0/`) into the single "flat" directory.
    *   This ensures all of a module's private dependencies are located in the same folder as the main module DLL.

4.  **Isolated Assembly Loading:**
    *   A new instance of `WebModuleLoadContext` is created, pointing to the main module DLL inside the flat directory.
    *   The main module assembly is loaded into this new context via `loadContext.LoadFromAssemblyPath(...)`.

5.  **Type Discovery and Instantiation:**
    *   The loader uses reflection (`assembly.GetTypes()`) to find all public, concrete classes within the loaded assembly that implement the `IWebModule` interface.
    *   **Crucially, this type check succeeds** because the `WebModuleLoadContext` has delegated the loading of the `IWebModule` interface's assembly to the host, ensuring both host and plugin are using the exact same `Type` instance.

6.  **Module Configuration:**
    *   An instance of the module class is created using `Activator.CreateInstance()`.
    *   The module's `ConfigureBuilder(builder)` method is called.
    *   Inside this method, the module configures its own services, often using the **Options Pattern** to bind to a dedicated section of the host's `IConfiguration`.
    *   **This works without `MissingMethodException`** because the `WebModuleLoadContext` has also delegated the loading of all `Microsoft.AspNetCore.*` assemblies to the host, ensuring type compatibility.

7.  **Integration with ASP.NET Core:**
    *   The module's assembly is added to the host's `ApplicationPartManager`. This allows ASP.NET Core's built-in discovery mechanisms to find Controllers, Razor Pages, and Views from the loaded module.
    *   After the host application is built (`app = builder.Build()`), the module's `ConfigureApp(app)` method is called, allowing it to register middleware or endpoints.
