using SharedTools.ModuleManagement;
using SharedTools.Web.Modules;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// Load module drops: every drop under Modules:Root (default /data/modules) plus each
// Modules:Paths entry. In Development, appsettings.Development.json points ExampleWebModule
// at bin/drops/ExampleWebModule, which the build publishes.
builder.AddModules();

// The module hub is referenced at compile time and served at /_modules.
builder.AddModule<ModuleManagementModule>();

var app = builder.Build();

app.UseStaticFiles();
app.MapRazorPages();

// Serves /_content/{Name}/ for each drop, then calls each module's Configure.
app.UseModules();

app.Run();
