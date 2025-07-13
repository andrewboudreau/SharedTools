using SharedTools.Web.Modules;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMemoryCache()
    .AddRazorPages();

// Use the new ApplicationPart-based module system
// This will download packages, resolve dependencies, and register them as ApplicationParts
await builder.AddApplicationPartModules(["ExampleWebModule"]);

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

// Map Razor Pages endpoints
app.MapRazorPages();

// Configure modules - this calls each module's Configure method
app.UseApplicationPartModules();

app.Run();
