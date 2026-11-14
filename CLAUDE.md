# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SharedTools loads ASP.NET Core web modules at startup from **drops**: the output of `dotnet publish`
(module dll, its `.deps.json`, its NuGet dependencies, `wwwroot/`), uploaded to a module host as a zip.
Each drop loads in its own `AssemblyLoadContext`; the shared framework and the `SharedTools.Web`
contract come from the host. Since 3.0 there is no runtime NuGet: nothing is downloaded at startup.

The design and its rationale are in `docs/plans/2026-11-14-module-drops.md`. The upload API and the
production module host (`modules.blogfor.net`) live in the HostingService repo (`domains/modules`),
not here.

## Project Structure (.NET 11)

- **SharedTools.Web** — the loader and contract (NuGet package, no dependencies beyond ASP.NET Core)
  - `Modules/IApplicationPartModule.cs` — the contract: `Name`, `Home`, `ConfigureServices`, `ConfigureApplicationParts`, `Configure`
  - `Modules/ModuleExtensions.cs` — `AddModules()`, `AddModule<T>()`, `UseModules()`, `ContentRoot()`/`WebRoot()`
  - `Modules/DropLoader.cs` — per-drop load, transactional `ConfigureServices`, `.prev` fallback, crash recovery
  - `Modules/ModuleStore.cs` — the on-disk store layout shared with uploaders; `ModuleDropStatus`
  - `Modules/ModuleStatusWriter.cs` — writes `_status/{id}.json` and clears `.booting` on ApplicationStarted
  - `Modules/ModuleAssemblyLoadContext.cs` — isolation rule (contract → host; framework → host unless the drop's copy is newer; rest → drop's `.deps.json`)
  - `Modules/ModuleRegistry.cs` — what loaded and what failed (singleton, read by the hub)
  - `Modules/ModuleOptionsExtensions.cs` — `AddModuleOptions<T>(moduleName)` binds `Modules:{Name}`
- **SharedTools.ModuleManagement** — the module hub at `/_modules` (NuGet package; hosts add it with `AddModule<ModuleManagementModule>()`)
- **ExampleWebModule** — example module (Razor SDK, Razor Pages, `wwwroot`, options, Azure.Storage.Blobs)
- **ExampleWebApp** — example host; its build publishes ExampleWebModule to `bin/drops/` and `appsettings.Development.json` loads it via `Modules:Paths`
- **SharedTools.Tests** / **SharedTools.Tests.TestModule** — HTTP tests and loader tests against a temp store; TestModule is a plain-SDK drop whose `ConfigureServices` throws if its drop has a `throw` file
- **eng/ModuleDrops.targets** — publishes `@(ModuleDrop)` projects as drops after build (used by ExampleWebApp and the tests)
- **templates/** — `dotnet new sharedmodule` (module + `deploy.ps1`)

## Commands

```bash
dotnet build
dotnet test
cd ExampleWebApp && dotnet run        # loads bin/drops/ExampleWebModule in Development
dotnet pack -c Release                # SharedTools.Web, SharedTools.ModuleManagement (published by CI on tag)
```

## How loading works

1. `AddModules()` reads `Modules:Root` (default `/data/modules`, skipped if missing) and `Modules:Paths:{id}`.
2. If the store's root `.booting` marker is still present, the last boot crashed: every *new* drop
   (status missing or `pending`, or an unrecognised `.drop`) is rolled back to `{id}.prev`, or moved to
   `{id}.disabled` if there is none or it is already a rollback. Then `.booting` is written.
3. For each drop: the entry assembly is the single `*.deps.json`'s dll; it loads in a
   `ModuleAssemblyLoadContext`; exactly one `IApplicationPartModule` type is instantiated; duplicate
   assemblies or names are rejected.
4. `ConfigureServices` runs against a copy of the host's services and is committed only if it succeeds;
   ApplicationParts are added once.
5. A new drop that fails steps 3–4 is swapped with `{id}.prev` on disk (recorded as `pending` + phase
   first, so a crash can't swap back), and the previous drop is loaded.
6. `UseModules()` serves `{drop}/wwwroot` at `/_content/{Name}/` and calls each `Configure` (failures
   reported, not undone).
7. On ApplicationStarted, `_status/{id}.json` is written for each managed module and `.booting` cleared.

## Writing modules

- Reference `SharedTools.Web` compile-only: `ExcludeAssets="runtime" PrivateAssets="all"` (or
  `Private="false" ExcludeAssets="runtime"` on a ProjectReference). The drop must not ship its own copy.
- `FrameworkReference` to `Microsoft.AspNetCore.App`; any other dependencies are normal PackageReferences.
- Razor SDK publishes `wwwroot` automatically; plain `Microsoft.NET.Sdk` needs
  `<Content Include="wwwroot\**" CopyToPublishDirectory="PreserveNewest" />`.
- Read files through `this.WebRoot()`/`this.ContentRoot()`, never embedded resources.
- Configuration via `AddModuleOptions<T>(Name)` (`Modules:{Name}:*`, env `Modules__{Name}__*`).
- Local testing: `dotnet publish -o out`, then `Modules:Paths:{id}` in a host. `bin/Debug` is not a drop.

## Security

The module upload key (`MODULE_UPLOAD_KEY`) is equivalent to code execution on the module host. Never
commit it, put it in a message, or hand it to a sandboxed or third-party agent. It is separate from
the agent-sites key and the site publish tokens.

## Troubleshooting

Check `/_modules` or `_status/{id}.json` first; they carry the failing phase and error.

- **`Expected exactly one *.deps.json`** — not a publish output, or the zip has a wrapper folder.
- **`already loaded by the host or another module`** — the same entry assembly in two drops, or the host references the module's assembly (tests must use `ReferenceOutputAssembly="false"`).
- **FileNotFoundException for a dependency** — the drop is missing it; republish rather than copying `bin/`.
- **Static file 404** — `wwwroot` not published, or the URL isn't under `/_content/{Name}/`.
