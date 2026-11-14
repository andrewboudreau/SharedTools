# SharedTools - Web Modules for ASP.NET Core

SharedTools lets an ASP.NET Core host load web modules (endpoints, Razor Pages, static files and their
own NuGet dependencies) at startup from **drops**: the zipped output of `dotnet publish`, uploaded to
the host. No runtime NuGet, no network at startup, no package feed.

```
module repo ──dotnet publish──▶ out/ ──zip + PUT──▶ module host (/data/modules/{id}/) ──restart──▶ serving
```

## Key features

- **Drops, not packages.** A module is whatever `dotnet publish` produces. Its dependencies resolve
  from its own `.deps.json` in an isolated `AssemblyLoadContext`; the shared framework and the
  `SharedTools.Web` contract come from the host.
- **Safe updates.** A new drop that fails to load, throws in `ConfigureServices`, or crashes the host
  during startup is rolled back to the previous drop automatically. Outcomes are written to
  `_status/{id}.json` and shown in the module hub.
- **Razor Pages and MVC** through ApplicationParts; **static files** from the drop's `wwwroot/` at
  `/_content/{Name}/`.
- **Module hub** at `/_modules` listing what loaded, which drop is serving, and what failed.

## Projects

| Project | What it is |
|---|---|
| `SharedTools.Web` | The loader and the module contract (`IApplicationPartModule`). NuGet package; no dependencies beyond ASP.NET Core. |
| `SharedTools.ModuleManagement` | The module hub, added to a host with `AddModule<ModuleManagementModule>()`. NuGet package. |
| `ExampleWebModule` | An example module: Razor Pages, `wwwroot`, options, Azure Blob dependency. |
| `ExampleWebApp` | An example host that loads ExampleWebModule from a drop the build publishes. |
| `SharedTools.Tests` (+ `SharedTools.Tests.TestModule`) | Loader and HTTP tests. |
| `templates/` | `dotnet new sharedmodule`. |

## Hosting modules

```csharp
using SharedTools.ModuleManagement;
using SharedTools.Web.Modules;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

builder.AddModules();                             // drops under Modules:Root (default /data/modules) + Modules:Paths
builder.AddModule<ModuleManagementModule>();      // optional: the hub at /_modules

var app = builder.Build();
app.UseStaticFiles();
app.MapRazorPages();
app.UseModules();                                 // /_content/{Name}/ static files, then each module's Configure
app.Run();
```

| Setting | Meaning |
|---|---|
| `Modules:Root` | The managed store. Each `{id}/` directory is a drop. Default `/data/modules`; ignored if missing. |
| `Modules:Paths:{id}` | An unmanaged drop directory, e.g. a local `dotnet publish -o out` folder for development. |
| `Modules:{Name}:*` | A module's own settings, bound with `AddModuleOptions<T>(Name)`. Environment: `Modules__{Name}__Setting`. |

### The store

```
{id}/              live drop (exactly one *.deps.json + its dll, wwwroot/); {id}/.drop = drop id
{id}.prev/         previous drop
{id}.disabled/     a drop that crashed the host with nothing to fall back to
_status/{id}.json  { drop, status: pending|loaded|failed, phase, error, serving, updated }
_incoming/         uploader scratch
_keys/             host data-protection keys
.booting           present while the host is starting
```

An uploader moves the old drop to `{id}.prev`, the new one to `{id}`, writes `{id}/.drop` and
`_status/{id}.json` as `pending`, then restarts the host. `ModuleStore` implements this layout so the
loader and an uploader agree on it.

## Writing a module

```bash
dotnet new install SharedTools.Templates
dotnet new sharedmodule -n MyIdea
```

Or by hand:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">            <!-- or Microsoft.NET.Sdk, see below -->
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <!-- Compile against the contract only; the host supplies it at runtime. -->
    <PackageReference Include="SharedTools.Web" Version="3.0.0" ExcludeAssets="runtime" PrivateAssets="all" />
  </ItemGroup>
  <!-- Microsoft.NET.Sdk only (the Razor SDK publishes wwwroot by itself): -->
  <!-- <ItemGroup><Content Include="wwwroot\**" CopyToPublishDirectory="PreserveNewest" /></ItemGroup> -->
</Project>
```

```csharp
public class MyModule : IApplicationPartModule
{
    public string Name => "MyIdea";                      // home /MyIdea/, static files /_content/MyIdea/

    public void ConfigureServices(IServiceCollection services) =>
        services.AddModuleOptions<MyOptions>(Name).ValidateOnStart();   // Modules:MyIdea:*

    public void Configure(WebApplication app)
    {
        app.MapGet("/MyIdea/", () => Results.File(Path.Combine(this.WebRoot(), "index.html"), "text/html"));
        app.MapGet("/MyIdea/api/hello", () => new { hello = "world" });
    }
}
```

Rules:
- Exactly one class implementing `IApplicationPartModule`, with a parameterless constructor.
- `Name` must be unique on the host and a valid path segment. Override `Home` if the hub should link
  somewhere other than `/{Name}/`.
- If `ConfigureServices` throws, none of its registrations are kept.
- `this.ContentRoot()` and `this.WebRoot()` give the module's drop and `wwwroot` directories.
- Never read the host's configuration with magic strings; use `AddModuleOptions<T>(Name)`.

### Run it locally

```bash
dotnet publish -c Release -o out
```

Then point a host at it: `"Modules": { "Paths": { "MyIdea": "C:/src/MyIdea/out" } }`. Plain
`bin/Debug` is not enough; class-library builds don't copy NuGet dependencies or `wwwroot`.

### Deploy it

`deploy.ps1` (in the template) publishes, zips, `PUT`s the zip to `{host}/api/modules/{id}` with
`X-Api-Key`, and polls until the host reports the drop as loaded or failed. The key comes from
`MODULE_UPLOAD_KEY` (environment or a gitignored `.env`). **It lets the holder run code on the host:**
never commit it or give it to an untrusted agent.

## Build and test

```bash
dotnet build          # also publishes ExampleWebModule to ExampleWebApp/bin/drops/ (eng/ModuleDrops.targets)
dotnet test
cd ExampleWebApp && dotnet run    # Development: loads bin/drops/ExampleWebModule via Modules:Paths
```

## Troubleshooting

Look at `/_modules` or `_status/{id}.json` first: they carry the failing phase and the error.

| Symptom | Likely cause |
|---|---|
| `Expected exactly one *.deps.json` | The drop is a build output or a zip with extra folders; upload `dotnet publish` output. |
| `already loaded by the host or another module` | Two drops (or the host) contain the same entry assembly. |
| `FileNotFoundException` for a dependency | The drop was built with a different SDK or trimmed; republish. |
| Static file 404 | `wwwroot` wasn't published (plain SDK needs the `Content` item) or the URL isn't under `/_content/{Name}/`. |
| Module shows "rolled back" | A newer drop failed; the hub shows why, and the previous drop is serving. |

## License

MIT
