using SharedTools.Web.Modules;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMemoryCache()
    .AddRazorPages();

// Use the new ApplicationPart-based module system
// This will download packages, resolve dependencies, and register them as ApplicationParts
await builder.AddApplicationPartModules([
    "ExampleWebModule", 
    "SharedTools.ModuleManagement"
]);

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();

// Map Razor Pages endpoints (including Areas)
app.MapRazorPages();

// Configure modules - this calls each module's Configure method
app.UseApplicationPartModules();


app.Run();
