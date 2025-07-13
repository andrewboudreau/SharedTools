# Migration Guide: WebModules to ApplicationParts

This guide helps you migrate from the `IWebModule` interface to the new `IApplicationPartModule` interface that leverages ASP.NET Core's ApplicationParts system.

## Overview of Changes

The new architecture provides better integration with ASP.NET Core's built-in plugin system while maintaining the ability to dynamically download and load NuGet packages at startup.

### Key Benefits

1. **Better MVC Integration**: Controllers and views are automatically discovered through ApplicationParts
2. **Improved Type Discovery**: Uses ASP.NET Core's built-in feature providers
3. **Modern Architecture**: Aligns with ASP.NET Core's recommended patterns for modularity
4. **Maintains Dynamic Loading**: Still supports downloading NuGet packages at runtime

## Migration Steps

### 1. Update Module Interface

**Old Interface:**
```csharp
public class MyModule : IWebModule
{
    public void ConfigureBuilder(WebApplicationBuilder builder) { }
    public void ConfigureApp(WebApplication app) { }
}
```

**New Interface:**
```csharp
public class MyModule : IApplicationPartModule
{
    public string Name => "MyModule";
    
    public void ConfigureServices(IServiceCollection services) { }
    public void ConfigureApplicationParts(ApplicationPartManager applicationPartManager) { }
    public void Configure(WebApplication app) { }
}
```

### 2. Update Service Registration

**Old:**
```csharp
public void ConfigureBuilder(WebApplicationBuilder builder)
{
    builder.Services.AddSingleton<IMyService, MyService>();
}
```

**New:**
```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton<IMyService, MyService>();
}
```

### 3. Update Host Application

**Old:**
```csharp
await builder.AddWebModules(["MyModule"]);
// ...
app.UseWebModules();
```

**New:**
```csharp
await builder.AddApplicationPartModules(["MyModule"]);
// ...
app.UseApplicationPartModules();
```

### 4. Controller Discovery

With ApplicationParts, controllers in your module assembly are automatically discovered. You no longer need to manually register them.

**Old:** Manual registration might have been needed
**New:** Just create controllers normally - they'll be discovered automatically

### 5. Static Assets

Static asset handling remains the same - files in `wwwroot` are served at `/_content/{ModuleName}/`

## Complete Example

### Module Implementation

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using SharedTools.Web.Modules;

namespace MyModule;

public class MyApplicationPartModule : IApplicationPartModule
{
    public string Name => "MyModule";

    public void ConfigureServices(IServiceCollection services)
    {
        // Register your services
        services.AddSingleton<IMyService, MyService>();
        
        // Configure options
        services.AddOptions<MyModuleOptions>()
            .BindConfiguration("MyModule")
            .ValidateDataAnnotations();
    }

    public void ConfigureApplicationParts(ApplicationPartManager applicationPartManager)
    {
        // Usually empty - the framework handles most cases
        // Add custom feature providers here if needed
    }

    public void Configure(WebApplication app)
    {
        // Add module-specific middleware or endpoints
        app.MapGet("/my-module/status", () => "Active");
    }
}
```

### Controller in Module

```csharp
[ApiController]
[Route("api/my-module")]
public class MyModuleController : ControllerBase
{
    // This controller will be automatically discovered!
    [HttpGet]
    public IActionResult Get() => Ok("Hello from MyModule");
}
```

### Host Application

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add MVC services
builder.Services.AddControllersWithViews();

// Add modules
await builder.AddApplicationPartModules(["MyModule", "AnotherModule"]);

var app = builder.Build();

app.UseRouting();
app.MapControllers();

// Configure modules
app.UseApplicationPartModules();

app.Run();
```

## Compatibility

Both systems can coexist during migration:
- Old modules using `IWebModule` continue to work with `AddWebModules()`
- New modules using `IApplicationPartModule` use `AddApplicationPartModules()`
- You can use both in the same application during transition

## Advanced Scenarios

### Custom Feature Providers

```csharp
public void ConfigureApplicationParts(ApplicationPartManager applicationPartManager)
{
    // Add a custom controller feature provider
    applicationPartManager.FeatureProviders.Add(new MyCustomControllerFeatureProvider());
}
```

### Multiple Assemblies

```csharp
public void ConfigureApplicationParts(ApplicationPartManager applicationPartManager)
{
    // Add additional assemblies if your module spans multiple DLLs
    applicationPartManager.ApplicationParts.Add(
        new AssemblyPart(typeof(AdditionalControllers).Assembly));
}
```

## Troubleshooting

1. **Controllers not found**: Ensure your module assembly is added as a ModuleApplicationPart
2. **Services not registered**: Check that ConfigureServices is called before Configure
3. **Static files not served**: Verify files are in wwwroot and embedded as resources

## Benefits of ApplicationParts

1. **Standard ASP.NET Core pattern**: Uses the framework's built-in plugin system
2. **Better performance**: Type discovery is optimized by the framework
3. **Improved debugging**: Better integration with ASP.NET Core diagnostics
4. **Future-proof**: Aligns with the direction of ASP.NET Core