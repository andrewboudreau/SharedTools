// Example usage of SharedTools.WebHost
// This file demonstrates how to use the simplified hosting API

using SharedTools.WebHost;

// Simplest approach - just create and run with modules:
var app = await WebHost.CreateAsync("ExampleWebModule", "SharedTools.ModuleManagement");
app.UseDefaults();
app.Run();

// With command line args:
/*
var app = await WebHost.CreateAsync(args, "ExampleWebModule", "SharedTools.ModuleManagement");
app.UseDefaults();
app.Run();
*/

// With custom middleware configuration while keeping services:
/*
var app = await WebHost.CreateAsync(args, "ExampleWebModule", "SharedTools.ModuleManagement");

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
app.UseApplicationPartModules();

app.Run();
*/

// Traditional approach without SharedTools.WebHost:
/*
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMemoryCache()
    .AddRazorPages();

await builder.AddApplicationPartModules(["ExampleWebModule", "SharedTools.ModuleManagement"]);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.UseApplicationPartModules();

app.Run();
*/