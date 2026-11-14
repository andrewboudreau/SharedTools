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
- a `wwwroot/index.html` served at `/MyIdea/` (and everything in `wwwroot/` at `/_content/MyIdea/`),
- SharedTools.Web referenced compile-only (the host supplies it at runtime),
- `deploy.ps1`, which publishes, uploads and waits for the host to report the drop,
- `.github/workflows/deploy.yml`, which runs `deploy.ps1` on every push to master/main.

Reference a specific SharedTools.Web version with `--sharedToolsVersion`:

```bash
dotnet new sharedmodule -n MyIdea --sharedToolsVersion 3.0.0
```

## Run it locally

Publish, then point any SharedTools host at the output with `Modules:Paths`:

```bash
dotnet publish -c Release -o out
# in the host's appsettings.Development.json:  "Modules": { "Paths": { "MyIdea": "C:/src/MyIdea/out" } }
```

## Deploy it

```bash
pwsh ./deploy.ps1 -Id my-idea
```

The key comes from `MODULE_UPLOAD_KEY` in the environment or a gitignored `.env`. It lets the holder run
code on the module host, so never commit it or hand it to an untrusted agent.

To deploy from GitHub instead, put the key in a `production` environment limited to the default
branch; the setup commands are at the top of `.github/workflows/deploy.yml`. Pushes then deploy
automatically, and the run fails if the host reports the drop as failed.

## Pack the template package

```bash
dotnet pack templates/SharedTools.Templates.csproj
```
