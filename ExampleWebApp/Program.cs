using SharedTools.Web.Modules;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMemoryCache()
    .AddRazorPages();

// See https://github.com/andrewboudreau/SharedTools/tree/master/ExampleWebModule
await builder.AddWebModules(["ExampleWebModule"]);

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
