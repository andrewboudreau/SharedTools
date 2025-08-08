# WebApplicationDefaults

This namespace provides simplified hosting APIs for creating ASP.NET Core applications with dynamic module loading support.

## Key Components

### WebApplicationExtensions
Provides static methods for creating web applications with built-in module support:
- `CreateAsync(args, modules)` - Creates a WebApplication with specified modules
- Automatically configures default services (RazorPages) and loads modules

### WebHostExtensions
Extension methods for configuring applications:
- `AddDefaults()` - Adds default services and loads modules to WebApplicationBuilder
- `UseDefaults()` - Configures default middleware pipeline (error handling, static files, routing, Razor Pages, module activation)

### Benefits
- Test modules in isolation
- Debug modules independently
- Deploy modules as standalone microservices OR as part of a larger application
- No need for separate host applications during development

## Example Usage

```csharp
// Simple module hosting
var app = await WebHost.CreateAsync("Module1", "Module2");
await app.RunAsync();

// With custom configuration
var builder = WebApplication.CreateBuilder(args);
await builder.AddDefaults("Module1", "Module2");
builder.Services.AddCustomService();

var app = builder.Build();
app.UseDefaults();
app.UseCustomMiddleware();
await app.RunAsync();
```