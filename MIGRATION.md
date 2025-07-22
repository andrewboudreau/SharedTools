# Migration Guide: Converting ASP.NET Core Projects to SharedTools Modules

This guide helps you convert existing ASP.NET Core projects into SharedTools modules that can be dynamically loaded from NuGet packages.

## Quick Reference for Claude Code

**Module Creation Checklist:**
1. Update .csproj: Change SDK, add SharedTools.Web, configure packaging
2. Create module class implementing IApplicationPartModule
3. Update namespaces to match module name
4. Fix static asset paths to use /_content/{ModuleName}/
5. Update API paths to /{ModuleName}/api/
6. Clean up old Program.cs and extension files
7. Create host application for testing

**Common Patterns:**
- Module namespace: `{ModuleName}` (without "Module" suffix)
- Package ID: `{ModuleName}Module` or just `{ModuleName}`
- Entry point: `/{ModuleName}/`
- API endpoints: `/{ModuleName}/api/*`
- Static assets: `/_content/{PackageId}/*`

## Overview

SharedTools modules are self-contained ASP.NET Core components that:
- Implement the `IApplicationPartModule` interface
- Are packaged as NuGet packages
- Can include Minimal APIs, Razor Pages, static assets, and dependencies
- Are loaded dynamically at application startup

## Migration Steps

### 1. Project File Configuration

#### For Projects WITHOUT Razor Pages (Minimal API only)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    <PackageId>YourModuleName</PackageId>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <!-- Framework reference for ASP.NET Core -->
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    
    <!-- SharedTools.Web reference (not included in package) -->
    <PackageReference Include="SharedTools.Web" Version="1.0.0">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    
    <!-- Your other dependencies (will be included in package) -->
    <PackageReference Include="Azure.Storage.Blobs" Version="12.25.0" />
  </ItemGroup>

  <!-- Include static assets as embedded resources -->
  <ItemGroup>
    <EmbeddedResource Include="wwwroot\**\*" />
  </ItemGroup>
</Project>
```

#### For Projects WITH Razor Pages

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    <PackageId>YourModuleName</PackageId>
    <Version>1.0.0</Version>
    
    <!-- Razor compilation settings -->
    <RazorCompileOnBuild>true</RazorCompileOnBuild>
    <RazorCompileOnPublish>true</RazorCompileOnPublish>
    
    <!-- Static asset base path for module -->
    <StaticWebAssetBasePath>_content/YourModuleName</StaticWebAssetBasePath>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    
    <PackageReference Include="SharedTools.Web" Version="1.0.0">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    
    <PackageReference Include="Azure.Storage.Blobs" Version="12.25.0" />
  </ItemGroup>
</Project>
```

#### Important: Static Asset Configuration

**Embedded resources are the only supported approach for static assets.** All modules must use embedded resources for their static content:

```xml
<EmbeddedResource Include="wwwroot\**\*" />
```

This approach provides:
- Single file deployment - everything in the DLL
- No extraction step - faster module loading  
- Atomic deployment - guaranteed file availability
- Better for containerized environments

**Do not use StaticWebAssetBasePath** as static web assets extraction has been removed from the module loading system.

### 2. Implement IApplicationPartModule

Create a module class that implements `IApplicationPartModule`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using SharedTools.Web.Modules;

namespace YourModuleName;

public class YourModule : IApplicationPartModule
{
    public string Name => "Your Module Display Name";

    public void ConfigureServices(IServiceCollection services)
    {
        // Register your services
        services.AddSingleton<IYourService, YourService>();
        
        // If using Azure Blob Storage
        services.Configure<YourBlobStorageOptions>(options =>
        {
            options.ConnectionString = "UseDevelopmentStorage=true";
            options.ContainerName = "your-container";
        });
        services.AddSingleton<IYourBlobStorage, YourBlobStorage>();
        
        // If using Razor Pages
        services.AddRazorPages();
    }

    public void Configure(WebApplication app)
    {
        // Map your Minimal API endpoints
        app.MapGet("/YourModuleName/", () => "Welcome to Your Module!");
        
        app.MapGet("/YourModuleName/api/data", async (IYourService service) =>
        {
            var data = await service.GetDataAsync();
            return Results.Ok(data);
        });
        
        // If using Razor Pages, they're automatically mapped
        // through the ApplicationPartManager
    }

    public void ConfigureApplicationParts(ApplicationPartManager partManager)
    {
        // Usually empty - the framework handles this
        // Override only if you need custom ApplicationPart configuration
    }
}
```

### 3. Organize Static Assets

Place static files in the `wwwroot` folder:

```
YourModuleName/
├── wwwroot/
│   ├── css/
│   │   └── module.css
│   ├── js/
│   │   └── module.js
│   └── images/
│       └── logo.png
├── YourModule.cs
└── YourModuleName.csproj
```

These will be served from `/_content/YourModuleName/css/module.css` etc.

**Important**: Always use the `/_content/{ModuleName}/` URL pattern to reference static assets. The SharedTools framework automatically maps embedded resources and static files to this location. Do NOT try to serve embedded resources directly through custom endpoints.

### 4. Azure Blob Storage Setup

If your module uses Azure Blob Storage:

```csharp
// Options class
public class YourBlobStorageOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
}

// Interface
public interface IYourBlobStorage
{
    Task<string> UploadAsync(Stream content, string blobName);
    Task<Stream> DownloadAsync(string blobName);
}

// Implementation
public class YourBlobStorage : IYourBlobStorage
{
    private readonly BlobContainerClient containerClient;

    public YourBlobStorage(IOptions<YourBlobStorageOptions> options)
    {
        var blobServiceClient = new BlobServiceClient(options.Value.ConnectionString);
        containerClient = blobServiceClient.GetBlobContainerClient(options.Value.ContainerName);
        containerClient.CreateIfNotExists();
    }

    public async Task<string> UploadAsync(Stream content, string blobName)
    {
        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(content, overwrite: true);
        return blobClient.Uri.ToString();
    }

    public async Task<Stream> DownloadAsync(string blobName)
    {
        var blobClient = containerClient.GetBlobClient(blobName);
        var response = await blobClient.DownloadStreamingAsync();
        return response.Value.Content;
    }
}
```

### 5. Razor Pages Setup (if needed)

For Razor Pages, organize them following the convention:

```
YourModuleName/
├── Pages/
│   ├── YourModuleName/         # Matches the entry point convention
│   │   ├── Index.cshtml
│   │   ├── Index.cshtml.cs
│   │   └── Details.cshtml
│   ├── _ViewImports.cshtml
│   └── _ViewStart.cshtml
```

Or use Areas for better organization:

```
YourModuleName/
├── Areas/
│   └── YourModuleName/
│       └── Pages/
│           ├── Index.cshtml
│           ├── Index.cshtml.cs
│           ├── _ViewImports.cshtml
│           └── _ViewStart.cshtml
```

### 6. Entry Points and Routing

Modules follow these conventions:
- Primary entry point: `/{ModuleName}/`
- API endpoints: `/{ModuleName}/api/*`
- Static assets: `/_content/{ModuleName}/*`

If your assembly name ends with "Module", it's stripped from the URL:
- `YourWebModule.dll` → `/YourWeb/`

### 7. Local Development and Testing

Configure local NuGet feed for development:

```xml
<!-- In your .csproj for Debug builds -->
<PropertyGroup Condition="'$(Configuration)'=='Debug'">
  <PackageOutputPath>C:\LocalNuGet</PackageOutputPath>
</PropertyGroup>
```

Create/update `nuget.config` in your solution root:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="Local" value="C:\LocalNuGet" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

### 8. Common Migration Patterns

#### From Controller to Minimal API

Before (Controller):
```csharp
[ApiController]
[Route("api/[controller]")]
public class DataController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromServices] IDataService service)
    {
        var data = await service.GetAllAsync();
        return Ok(data);
    }
}
```

After (Minimal API in module):
```csharp
public void Configure(WebApplication app)
{
    app.MapGet("/YourModuleName/api/data", async (IDataService service) =>
    {
        var data = await service.GetAllAsync();
        return Results.Ok(data);
    });
}
```

#### Static File References

Before:
```html
<link rel="stylesheet" href="~/css/site.css" />
<script src="~/js/app.js"></script>
<img src="~/images/logo.png" />
```

After (in module):
```html
<link rel="stylesheet" href="~/_content/YourModuleName/css/site.css" />
<script src="~/_content/YourModuleName/js/app.js"></script>
<img src="~/_content/YourModuleName/images/logo.png" />
```

**Note**: The `/_content/{ModuleName}/` path is automatically handled by SharedTools. You don't need to create any endpoints for serving these files.

### 9. Testing Your Module

1. Build and pack your module:
   ```bash
   dotnet pack -c Debug
   ```

2. Create a test host application:
   ```csharp
   var builder = WebApplication.CreateBuilder(args);
   
   // Load your module
   await builder.AddApplicationPartModules(["YourModuleName"]);
   
   var app = builder.Build();
   
   app.UseStaticFiles();
   app.UseApplicationPartModules();
   
   app.Run();
   ```

3. Run and navigate to `/{YourModuleName}/`

### 10. Troubleshooting

#### Module Not Loading
- Ensure `IApplicationPartModule` is implemented
- Check that the assembly is in the NuGet package
- Verify NuGet feed configuration
- Clear NuGet cache: `dotnet nuget locals all --clear`
- Enable detailed logging:
  ```json
  {
    "Logging": {
      "LogLevel": {
        "SharedTools.Web.Modules": "Debug"
      }
    }
  }
  ```

#### Static Assets Not Found
- For non-Razor SDK: Ensure `<EmbeddedResource Include="wwwroot\**\*" />`
- For Razor SDK: Check `StaticWebAssetBasePath` setting
- Verify files are in `wwwroot` folder
- Always use `/_content/{ModuleName}/` URLs - don't create custom endpoints for static files
- Check that resource names match: `{AssemblyName}.{ResourcePath.Replace('/', '.')}`

#### Dependency Issues
- All dependencies must be in the NuGet package
- Use `<PrivateAssets>all</PrivateAssets>` only for SharedTools.Web
- Check for version conflicts with host application

#### Razor Pages Not Working
- Must use `Microsoft.NET.Sdk.Razor` SDK
- Ensure `RazorCompileOnBuild` is true
- Check that pages follow naming conventions

#### Common Build Errors

**Missing Type References**
- Error: `The type or namespace name 'IHttpContextAccessor' could not be found`
- Solution: Add missing using directives:
  ```csharp
  using Microsoft.AspNetCore.Http;
  using Microsoft.Extensions.Logging;
  ```

**Results Not Found**
- Error: `The name 'Results' does not exist in the current context`
- Solution: Add `using Microsoft.AspNetCore.Http;`

## Module Configuration Best Practices

### Use Prefixed Configuration Sections
To avoid conflicts between modules, prefix your configuration:

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.Configure<YourModuleOptions>(options =>
    {
        var config = services.BuildServiceProvider()
            .GetRequiredService<IConfiguration>()
            .GetSection($"Modules:{Name}");
        
        config.Bind(options);
    });
}
```

**appsettings.json**:
```json
{
  "Modules": {
    "YourModule": {
      "ConnectionString": "...",
      "ApiKey": "..."
    }
  }
}
```

## Best Practices

1. **Keep Modules Focused**: One feature area per module
2. **Minimize Dependencies**: Only essential packages
3. **Use Conventions**: Follow naming and routing patterns
4. **Isolate Configuration**: Module-specific settings only
5. **Document Dependencies**: List all required services
6. **Test in Isolation**: Verify module works standalone
7. **Version Carefully**: Follow semantic versioning
8. **Configure StaticWebAssetBasePath**: Always set this property in your project file for static asset support
9. **Always use `/_content/{ModuleName}/` for static assets**: Don't create custom endpoints

## Example Module Structure

```
YourModuleName/
├── wwwroot/
│   ├── css/
│   ├── js/
│   └── images/
├── Pages/ (optional)
│   └── YourModuleName/
│       └── Index.cshtml
├── Services/
│   ├── IYourService.cs
│   └── YourService.cs
├── YourModule.cs
├── YourModuleName.csproj
└── README.md
```

This structure provides a clean, self-contained module that can be easily packaged and loaded by SharedTools.

## Creating a Host Application for Testing

Since modules are now libraries rather than runnable applications, you'll need a host application to load and test them. Here's how to create one:

### Quick Start: Minimal Host Application

1. **Create a new ASP.NET Core Web Application**:
   ```bash
   dotnet new web -n ModuleHost
   cd ModuleHost
   ```

2. **Add SharedTools.Web reference**:
   ```bash
   dotnet add reference ../SharedTools.Web/SharedTools.Web.csproj
   ```
   
   Or if using the NuGet package:
   ```bash
   dotnet add package SharedTools.Web
   ```

3. **Create a simple Program.cs**:
   ```csharp
   using SharedTools.Web.Modules;

   var builder = WebApplication.CreateBuilder(args);

   // Add basic services
   builder.Services.AddRazorPages();

   // Load your module(s)
   await builder.AddApplicationPartModules(["YourModuleName"]);

   var app = builder.Build();

   // Basic middleware
   app.UseStaticFiles();
   app.UseRouting();

   // Map endpoints
   app.MapRazorPages();

   // Activate modules
   app.UseApplicationPartModules();

   app.Run();
   ```

4. **Add nuget.config** to use local packages:
   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <configuration>
     <packageSources>
       <add key="Local" value="C:\LocalNuGet" />
       <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
     </packageSources>
   </configuration>
   ```

### Full-Featured Host Application

For a more complete development experience, create a host with additional features:

**Program.cs**:
```csharp
using SharedTools.Web.Modules;

var builder = WebApplication.CreateBuilder(args);

// Core services
builder.Services
    .AddMemoryCache()
    .AddRazorPages();

// Load multiple modules
var modulesToLoad = builder.Configuration
    .GetSection("Modules")
    .Get<string[]>() ?? ["YourModuleName"];

await builder.AddApplicationPartModules(modulesToLoad);

var app = builder.Build();

// Environment-specific configuration
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Middleware pipeline
app.UseStaticFiles();
app.UseRouting();

// Map endpoints
app.MapRazorPages();

// Add a simple home page
app.MapGet("/", () => Results.Redirect("/_modules"));

// Activate all loaded modules
app.UseApplicationPartModules();

app.Run();
```

**appsettings.json**:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Modules": [
    "YourModuleName",
    "SharedTools.ModuleManagement"
  ]
}
```

**launchSettings.json**:
```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "http://localhost:5000",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

### Development Workflow

1. **Build your module**:
   ```bash
   cd YourModuleName
   dotnet pack -c Debug  # Outputs to C:\LocalNuGet
   ```

2. **Run the host**:
   ```bash
   cd ModuleHost
   dotnet run
   ```

3. **Navigate to your module**:
   - Open browser to `http://localhost:5000/{YourModuleName}/`
   - Or if using ModuleManagement: `http://localhost:5000/_modules`

4. **Iterate**:
   - Make changes to your module
   - Rebuild: `dotnet pack -c Debug`
   - Restart host: `Ctrl+C` then `dotnet run`

### Tips for Host Applications

- **Keep it minimal**: The host should only provide infrastructure
- **Use configuration**: Load module names from appsettings.json
- **Include ModuleManagement**: Helps visualize loaded modules
- **Add health checks**: Useful for monitoring module status
- **Consider logging**: Add structured logging for debugging
- **Use HTTPS in production**: Add `app.UseHttpsRedirection()`

This approach separates concerns: modules contain features, the host provides infrastructure.

## Debugging Tips

### View Loaded Assemblies
Add this endpoint to your host for debugging:
```csharp
app.MapGet("/_debug/assemblies", () => 
{
    var assemblies = AppDomain.CurrentDomain.GetAssemblies()
        .Where(a => !a.IsDynamic)
        .Select(a => new { a.FullName, a.Location })
        .OrderBy(a => a.FullName);
    return Results.Json(assemblies);
});
```

### Module Discovery Issues
If your module isn't being discovered:
1. Check the package exists: `dir C:\LocalNuGet\*.nupkg`
2. Verify the module class is public and implements IApplicationPartModule
3. Check the assembly contains the module: use a tool like ILSpy or dotPeek
4. Look for loading errors in the debug logs

## Project Structure Templates

### Minimal API Module Template
```
YourModule/
├── YourModule.cs              # IApplicationPartModule implementation
├── YourModule.csproj          # SDK: Microsoft.NET.Sdk (with StaticWebAssetBasePath)
├── Services/                  # Business logic
│   ├── IYourService.cs
│   └── YourService.cs
├── Models/                    # Data models
│   └── YourModel.cs
└── wwwroot/                   # Static assets (served via StaticWebAssetBasePath)
    ├── css/
    ├── js/
    └── images/
```

### Razor Pages Module Template
```
YourModule/
├── YourModule.cs              # IApplicationPartModule implementation
├── YourModule.csproj          # SDK: Microsoft.NET.Sdk.Razor
├── Areas/
│   └── YourModule/
│       └── Pages/
│           ├── _ViewStart.cshtml
│           ├── _ViewImports.cshtml
│           └── Index.cshtml
├── Services/
└── wwwroot/
```