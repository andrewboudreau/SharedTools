using SharedTools.Web.Modules;

var builder = WebApplication.CreateBuilder(args);

// Use the new ApplicationPart-based module system
// This will download packages, resolve dependencies, and register them as ApplicationParts
await builder.AddApplicationPartModules([
    "ExampleWebModule", 
    "SharedTools.ModuleManagement"
]);

var app = builder.Build();

app.UseStaticFiles();

// Configure modules - this calls each module's Configure method
app.UseApplicationPartModules();

app.Run();
