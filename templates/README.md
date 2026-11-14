# SharedTools templates

`dotnet new` templates for scaffolding SharedTools web modules, so a new idea is one
command instead of copy-pasting an existing module and renaming everything.

## Install

From the packaged template (once published):

```bash
dotnet new install SharedTools.Templates
```

Or straight from this folder during development:

```bash
dotnet new install ./templates/SharedTools.Module
```

## Use

```bash
dotnet new sharedmodule -n MyIdea
```

This creates a module project `MyIdea` with:

- a `Module` class implementing `IApplicationPartModule` (home page + endpoint stubs),
- an embedded `wwwroot/index.html` served at `/MyIdea/`,
- packaging set up (`GeneratePackageOnBuild`, local feed output in Debug).

Reference a specific SharedTools.Web version with `--sharedToolsVersion`:

```bash
dotnet new sharedmodule -n MyIdea --sharedToolsVersion 2.0.0
```

Build it, and in Debug it packs to `C:\LocalNuGet` so a host can load it via
`AddApplicationPartModules(["MyIdea"])`.

## Pack the template package

```bash
dotnet pack templates/SharedTools.Templates.csproj
```
