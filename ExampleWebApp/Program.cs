using SharedTools.Web.Modules;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMemoryCache()
    .AddRazorPages();

// this extension loads the ProjectGeoShot.Game module and configures the services it provides.
await builder.AddWebModules(["ProjectGeoShot.Game"]);
// See https://www.nuget.org/packages/ProjectGeoShot.Game

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.MapRazorPages();

// This extension method is defined in SharedTools.Web.Modules
app.UseWebModules();

app.Run();
