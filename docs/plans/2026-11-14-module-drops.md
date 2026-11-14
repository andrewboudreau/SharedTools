# Module drops: replace runtime NuGet loading with "build, zip, upload"

Status: approved v2 (2026-11-14), revised after an independent review by Fable. Decisions are in
section 5.
Scope: SharedTools.Web, SharedTools.ModuleManagement, the Scoreboard repo, and the HostingService host.
The host-side work (the `domains/modules` app, gateway, compose, keys) belongs to the hosting-service
session.

## TL;DR

Today, when blogfor.net starts, a hand-written NuGet client downloads the *newest* `SharedTools.Scoreboard`
and its dependency graph from nuget.org, flattens the DLLs into `/tmp` and loads them into an isolated
`AssemblyLoadContext`.

Shipping a change is a relay:
1. Push.
2. A bot bumps the version.
3. Someone dispatches the publish workflow by hand.
4. nuget.org indexes the package.
5. Something restarts the container.

Agents struggle with it, production runs whatever was newest at the last restart, and there is no
rollback.

**Proposal:** a module becomes a *drop*, the zipped `dotnet publish` output (DLL, `deps.json`,
dependencies, `wwwroot/`). An agent clones the module's public repo, publishes, and `PUT`s the zip to a
small module-host app with a dedicated key. The host swaps it into `/data/modules/{id}/`, keeping the
previous drop as `{id}.prev/`, and restarts. At startup it loads from local disk, with no network and
no NuGet.

```
git clone https://github.com/andrewboudreau/Scoreboard && cd Scoreboard
dotnet publish src/Scoreboard -c Release -o out
(cd out && zip -qr ../scoreboard.zip .)
curl -sS -X PUT https://modules.blogfor.net/api/modules/scoreboard \
     -H "X-Api-Key: $MODULE_UPLOAD_KEY" -H "Content-Type: application/zip" --data-binary @scoreboard.zip
# 202 {"id":"scoreboard","drop":"20261114-153012-9f3a1c","restarting":true}
curl -sS https://modules.blogfor.net/api/modules/scoreboard -H "X-Api-Key: $MODULE_UPLOAD_KEY"
# poll (tolerating 502 during the restart) until drop == 20261114-153012-9f3a1c and status is loaded or failed
```

The shape is the same as the belongto.us agent-sites upload (`PUT /api/a/{name}`), which agents already use.

**Split of responsibilities.** This is the main change from v1.
- **SharedTools.Web 3.0 is only a loader:** `AddModules(root)` and `UseModules()`. It has no HTTP surface.
- **The upload endpoint lives in the module-host app** (`HostingService/domains/modules`), adapted from
  the existing `AgentSiteStore`.

A library that ships an upload-and-restart API with code-execution power for one consumer is the kind
of abstraction to avoid.

---

## 1. How it works today

### Pipeline (`AddApplicationPartModules`, `SharedTools.Web/Modules/ApplicationPartModuleExtensions.cs`)

1. **Sources.** Explicit URLs, `nuget.config` from the current directory, or nuget.org.
2. **Resolve.** `NuGetPackageService.ResolveDependencyGraphAsync`, a hand-written breadth-first walk.
   The root package floats to the newest stable version.
3. **Download and flatten.** Only `*.dll` from each package's nearest `lib/<tfm>` group, extracted into
   `/tmp/SharedTools_ApplicationPartModulesCache/{id}.{ver}_flat`, which is wiped each start.
4. **Load.** `ModuleAssemblyLoadContext`:
   - The contract assembly, `Microsoft.AspNetCore.*` and `Microsoft.Extensions.*` go to the host.
   - `System.*` comes from the module directory first.
   - Everything else comes from the module directory.
5. **Discover.** The loader creates each `IApplicationPartModule`, then:
   - calls `ConfigureServices`
   - adds an `AssemblyPart` and a `CompiledRazorAssemblyPart`
   - calls `ConfigureApplicationParts`
6. **Static assets.** Embedded `wwwroot.*` resources are served through `EmbeddedFileProvider` at
   `/_content/{Name}`.
7. **Configure.** `UseApplicationPartModules` calls `Configure(app)`. Failures are captured by phase on
   `ModuleRegistry.Failures`, and the hub shows them.

Production: blogfor.net (SharedTools.Web 2.0.1) loads Scoreboard 0.0.47 and ModuleManagement 2.0.1 from
nuget.org at every container start. No other domain uses runtime modules.

### Findings

All were checked against the code. Fable independently confirmed F1, F3–F4 and F6–F15. F2 and F5 are
partly confirmed.

**Operational**

- **F1. Production runs whatever was newest at the last restart.** The root version floats
  (`NuGetPackageService.cs:66`; blogfor.net `Program.cs:62-65` passes no version). A broken publish
  changes nothing until an unrelated restart, then breaks the site. Rollback means pinning a version
  in HostingService. nuget.org versions are permanent; there are 47 public Scoreboard versions.
- **F2. Every start does version lookups against nuget.org** (`SourceCacheContext { NoCache = true }`).
  `.nupkg` downloads may be reused from the global packages folder, but resolution always needs the
  network.
- **F3. The relay.** It involves the bump bot, a manual dispatch, `NUGET_API_KEY`, indexing delay, and
  a restart that nothing triggers. Scoreboard also still builds on the .NET 10 SDK against
  SharedTools.Web 1.4.4 while the host runs 2.0.1 on .NET 11 (`Scoreboard.csproj:7,19`). That works
  only because the contract is bound by name.

**Correctness of the hand-written resolver**

- **F4. Upgraded dependencies aren't walked.** If a dependency is first seen at version A and later
  raised to B, it is updated but never queued (`NuGetPackageService.cs:137-144`). B's dependencies are
  never walked.
- **F5. It is not NuGet's resolver.** Extraction takes only flattened `lib/` DLLs (`:234-246`), so native
  `runtimes/*` assets and satellite assemblies are dropped. `dotnet publish` gets all of this right.
- **F6. `System.*` loads module-first** (`ModuleAssemblyLoadContext.cs:46-55`).

**Authoring friction**

- **F7. Static assets are embedded resources.** That forces `GetManifestResourceStream` page helpers
  (`ScoreboardModule.cs:66-73`), and nested folder names get mangled.
- **F8. The contract package drags in the NuGet client.** SharedTools.Web ships the contract and the
  NuGet loader together, so every module's publish output contains `NuGet.*.dll` and
  `Newtonsoft.Json.dll`. Verified by the spike.
- **F9. `build/SharedTools.Web.targets` writes `modules-docs/*.md`** into every consumer's project
  folder on each build.
- **F10. The development loop is a `C:\LocalNuGet` feed.** It shows up in `PackageOutputPath` overrides,
  `SelfHostingExtensions.cs:78,86` and a `nuget.config` per host.
- **F11. Scoreboard reads root `BlobStorage:*` keys,** not `Modules:Scoreboard:*`. blogfor.net's
  git-tracked `appsettings.json:10` holds a plaintext storage account key. **Rotate it regardless of
  this plan.**

**Small issues**

- **F12. The bootstrap logger comes from a disposed `LoggerFactory`** (`ApplicationPartModuleExtensions.cs:451,463`).
- **F13. `NuGetPackageService`'s logger is always null,** and it keeps a static mutable logger (`:19,23`).
- **F14. The hub's latest-version check is wrong.** It takes the last flat-container entry, which may
  be a prerelease (`NuGetVersionService.cs:36`).
- **F15. The hub guesses entry points** with `EntryPoint = "/{Name}/"` (`ModuleManagementModule.cs:64`).

**What stays:**
- The `IApplicationPartModule` contract (`Name` plus `Configure`; the other members are default no-ops).
- The basic shape of `ModuleAssemblyLoadContext`.
- `AssemblyDependencyResolver`, which becomes correct once `deps.json` is present.
- Failure capture by phase.
- `AddModuleOptions<T>` with `Modules:{id}`.
- The `/_modules` hub.

---

## 2. Design

### 2.1 The drop

A drop is a zip of `dotnet publish -c Release` output: framework-dependent, no RID.

```
SharedTools.Scoreboard.dll         entry assembly, inferred from the single *.deps.json
SharedTools.Scoreboard.deps.json   required
Azure.Storage.Blobs.dll ...        private dependencies (publish already prunes framework-provided ones)
runtimes/<rid>/native/...          if any
wwwroot/...                        static assets on disk
```

There is no `module.json`. The entry assembly is inferred, and a module that needs a hub link other than
`/{Name}/` overrides a new default interface member, `string Home => $"/{Name}/";`.

**Modules must publish `wwwroot`.** The Razor SDK does it automatically; the spike with ExampleWebModule
showed this. A `Microsoft.NET.Sdk` project such as Scoreboard needs:

```xml
<Content Include="wwwroot\**" CopyToPublishDirectory="PreserveNewest" />
```

The template includes it.

### 2.2 Loader (SharedTools.Web 3.0)

```csharp
builder.AddModules();      // every {Modules:Root}/{id}/ (default /data/modules); skips *.prev, _incoming, _keys
...
app.UseModules();          // /_content/{Name}/ static files + Configure(); replaces UseApplicationPartModules
```

Per module directory, the loader:
1. Infers the entry assembly.
2. Creates `new ModuleAssemblyLoadContext(entry)`.
3. Runs discovery, ApplicationParts and failure capture (the existing code, moved).

**Load-context rule (F6).**
- `SharedTools.Web` always goes to the host.
- For any other assembly the host's shared framework provides, use the host's copy *unless the drop's
  copy has a higher AssemblyVersion*. In that case load the drop's copy, because the default context
  can't satisfy a higher version.
  - "Framework-provided" means a file-name set built from the directories of
    `typeof(object).Assembly` and `typeof(WebApplication).Assembly`.
  - In practice, publish already prunes older framework assemblies, so this rarely triggers.
- Everything else goes through `AssemblyDependencyResolver` (`deps.json`).
- The host app's own NuGet dependencies are not shared.

**`ConfigureServices` isolation.** Each module's `ConfigureServices` runs against a scratch
`ServiceCollection`, and its descriptors are copied into the real one only on success. A throwing
module therefore leaves no half-registered services. The ApplicationParts are added only after that
succeeds too.

**Identity checks.**
- Two directories whose entry assembly or `Name` collide: load the first, fail the second with a clear
  error. The alternative is duplicate routes and `AmbiguousMatchException` at request time.
- A `Name` that collides with a host path is the author's responsibility, and the hub lists each
  module's `Home`.

**On-disk contract** (as implemented in `SharedTools.Web.Modules.ModuleStore`, shared with the uploader):

```
{id}/              live drop; {id}/.drop holds the drop id (travels with swaps)
{id}.prev/         previous drop
{id}.disabled/     a crashing drop with nothing to fall back to (not loaded)
_status/{id}.json  {drop, status: pending|loaded|failed, phase, error, serving, updated}
_incoming/         uploader scratch (same volume, so renames are atomic)
_keys/             host data-protection keys
.booting           one root-level marker: written at boot, cleared on ApplicationStarted
```

Status lives outside `{id}/` because it describes the module id across swaps ("drop X failed, serving
W"). The uploader writes `pending` with `phase: null`; the loader writes `pending` with a phase only
when it has rolled back on its own.

A drop is **new** if its status is missing or `pending`, or its `.drop` matches neither `status.drop`
nor `status.serving`. Only new drops are ever rolled back.

**Crash-loop guard (B3 in the review).** Fallback only covers exceptions the loader catches. A module
can still kill the process after loading, through a hosted service that throws in `StartAsync`, OOM at
the 384 MB cap, or a stack overflow. With `restart: always`, that becomes a loop in which the rollback
API never comes up. The guard:
1. At boot, write the root `.booting`. On `ApplicationStarted`, write `_status/{id}.json` for every id
   (`loaded` or `failed`, with `drop` = attempted and `serving` = what actually serves) and clear it.
2. If `.booting` is already present at boot, the previous boot died. Every **new** drop is treated as
   the cause (drops that already loaded are left alone):
   - with a `.prev`, swap them, record the rollback as `pending` + phase on disk *before* loading the
     previous drop, then load it;
   - with no `.prev`, or if the live drop is itself a rollback (`pending` + phase), move it to
     `{id}.disabled` and write `failed`/`Crash` immediately.
   So the host is back up within two boots, and never swaps back and forth.

Recovery never needs SSH.

**Fallback on caught failures.** If a new drop fails at Load or ConfigureServices, the loader:
- swaps back to `{id}.prev` on disk (no in-memory fallback, so "what's running" always equals `{id}/`);
- records the rollback (`pending` + phase), loads the previous drop, and on start writes
  `failed, serving <prev drop>`.

Failures in `Configure` can't be undone: endpoints may already be mapped. They are reported, and the
module stays in whatever state it reached.

**Static files.** `{id}/wwwroot` is served through `PhysicalFileProvider` at `/_content/{Name}/`. The
embedded-resource path is deleted; there is one consumer, and it moves in the same cut-over.

**Development loop (F10).** `Modules:Paths:Scoreboard = C:\src\Scoreboard\out` points at a
`dotnet publish -o out` folder. Plain `bin/Debug` won't work, because class-library builds don't copy
NuGet dependencies or `wwwroot`. Development and production use the same artifact: `publish`, then
restart the host.

**Not doing:**
- **Hot reload.** Parts, compiled Razor, DI and endpoints can't be unloaded cleanly, and a 2–5 s
  restart is fine.
- **A compatibility gate on the contract version.** There is one author and a three-member interface,
  and a mismatch already surfaces as a captured Load failure.
- **A general `AddModule<T>()`.** A limited one exists for the hub, which the module host references
  at compile time: it adds the assembly's parts only if MVC doesn't already have them, and it does not
  run `ConfigureServices` transactionally (host code that throws should fail the host).

### 2.3 Upload API (in `HostingService/domains/modules`, about 150 lines)

The code is adapted from `AgentSiteStore`: spool, zip-slip check, wrapper-folder strip, backslash
normalization, atomic directory swap.

| Call | Auth | Result |
|---|---|---|
| `GET /api/modules` without a key | none | plain-text how-to for agents (like `GET /a`) |
| `GET /api/modules[/{id}]` | key | per module: `drop`, `status`, phase and error, the `prev` drop |
| `PUT /api/modules/{id}` (raw zip) | key | 202 `{id, drop, restarting:true}`; 400 with a reason |
| `POST /api/modules/{id}/rollback` | key | swaps `{id}` and `{id}.prev`, 202, restart |

**Auth.**
- `X-Api-Key` is compared in fixed time against `Modules:UploadKey`. An unset key returns 401 on every
  write, matching agent-sites.
- **The key equals code execution in the module host.** It is separate from `AgentSites__UploadKey`
  and the `*_PUBLISH_TOKEN` values, and is never handed to sandboxed or third-party agents.
- There is no per-module token.

**Validation, before anything is live:**
- Stream the body to `{root}/_incoming/` on the volume. Not tmpfs, which would count against memory,
  and a rename from there would cross devices.
- Lift Kestrel's 30 MB default for this route only, to 50 MB, as `AgentSitesEndpoints.cs:84-86` does.
  The gateway already allows 110 MB.
- Unpacked size of 200 MB or less, 2000 files or fewer.
- Exactly one `*.deps.json`, with its DLL present.
- An id matching `^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$`.
- The entry assembly name is not already owned by another id.

**Apply:**
1. Extract into `_incoming/{id}`.
2. Delete `{id}.prev`, rename `{id}` to `{id}.prev`, rename `_incoming/{id}` to `{id}`, write
   `{id}/.drop`.
3. Write `_status/{id}.json` as `{drop, status:"pending", phase:null, serving:<current serving>}`.
4. Append to `uploads.log`: time, id, drop, SHA-256, and the client IP from `X-Forwarded-For`.
5. Return 202, then call `IHostApplicationLifetime.StopApplication()` from `Response.OnCompleted`, so
   the response isn't cut off.

Uploads and rollbacks are serialized by one process-wide lock; a concurrent write, or one while the
module is `pending`, gets 409. Rollback swaps through a temporary name and writes `pending` the same
way.

`drop` is `{yyyyMMdd-HHmmss}-{sha6}`. It is unique per upload, so polling works even when the same
commit is uploaded twice.

**Hub.** `SharedTools.ModuleManagement` becomes a compile-time reference of the module-host app. It is
served at `modules.blogfor.net/_modules` and shows drop, status, error and `prev` for each module.
`NuGetVersionService` is removed (F14), and links use `Home` (F15).

### 2.4 Where modules run

**A dedicated module-host container on its own hostname.**

- **Program.cs** is about 20 lines: `AddModules`, the hub, `UseModules`, the upload endpoints. Data
  protection keys persist to `/data/modules/_keys`, so cookies and antiforgery survive restarts on a
  read-only filesystem.
- **Hostname: `modules.blogfor.net`** (or similar, decision D1). It has a single catch-all gateway
  route, so a *new* module needs no gateway edit. Without this, every new module would need a
  HostingService change, which is the relay this plan removes. The upload API and hub live here.
- **Same-origin routes:** two extra routes on blogfor.net, `/Scoreboard/{**rest}` and
  `/_content/Scoreboard/{**rest}`, sent to `modules:8080` with `Order = -1` and the original Host header.
  These are *only for origin continuity* (existing localStorage and bookmarks). New modules simply live
  at `modules.blogfor.net/{Name}/`.
- **Container:**
  - its own ECR image, normal Watchtower updates
  - `restart: always`
  - `~/modules:/data/modules` (owned by uid 1654)
  - `read_only: true` with a small `tmpfs` `/tmp`
  - `cap_drop: [ALL]`
  - `no-new-privileges`
  - the non-root `app` user
  - only the module secrets in its environment, for example `Modules__Scoreboard__BlobConnectionString`

**Why not inside blogfor.net?** That would put a code-execution key beside the blog's publish token,
mailbox, notes and backups, and every module upload would restart the blog. The cost of the dedicated
container is one image, one CI job, one compose service and one idle ASP.NET process on a box built for
density. Running inside blogfor.net is acceptable only as a short-lived stepping stone.

**Tracked separately, not gating this plan** (hosting-service owns these):
- An `edge` network that the module host shares only with `yarp`. Today every container shares one
  network with Postgres, which connects as a superuser.
- Requiring IMDSv2 with hop limit 1. IMDSv1 currently exposes the `ECR_Reader` role to any container.

**Host facts, confirmed by hosting-service on the live box (2026-11-14):**
- YARP supports per-host path routes with `Order = -1`.
- SiteHygiene blocks dot-segments and `/vendor`, `/wp-*` and similar roots. Avoid them for module
  paths.
- `restart: always` restarts a container that exits with code 0.
- The containers are capped at 384 MB.
- The host's `~/docker-compose.yml` is hand-edited and not in git, and env changes need
  `up -d --force-recreate --no-deps <svc>`.

### 2.5 Authoring a module after this

`dotnet new sharedmodule -n MyIdea` produces:
- An SDK project with `SharedTools.Web` 3.x referenced as
  `<PackageReference ... ExcludeAssets="runtime" PrivateAssets="all"/>` (compile only). In 3.0 it has no
  NuGet dependencies (F8).
- An on-disk `wwwroot/` with the `Content`/`CopyToPublishDirectory` item.
- Pages as plain files under `wwwroot/`, served at `/_content/{Name}/...`. Pretty routes are a `MapGet`
  returning a file through a small helper that hands the module an `IFileProvider` for its own drop
  `wwwroot`.
- **One** script, `deploy.ps1` (pwsh runs everywhere), which does `publish`, zip, `PUT` and poll on
  `drop`, tolerating 502s while the host restarts. It reads `MODULE_UPLOAD_KEY` from the environment or a
  gitignored `.env`. A GitHub Action is optional, later.
- No `PackageOutputPath`, `C:\LocalNuGet` or `modules-docs/`.

---

## 3. Phases

Three phases. Each ends in a working blogfor.net.

### Phase 1: SharedTools.Web 3.0 loader, and Scoreboard as a drop (SharedTools and Scoreboard repos)

**SharedTools:**
- [x] `AddModules`/`UseModules`, built on the moved discovery and registration code. Includes:
      - the load-context rule from 2.2
      - a scratch `ServiceCollection`
      - identity checks
      - the `.booting` crash guard and `_status/{id}.json`
      - `.prev` fallback
      - `PhysicalFileProvider` static files
      - `Modules:Paths`
      - `Home` default member
- [x] Delete the runtime NuGet path in the same release:
      - `NuGetPackageService.cs`
      - the NuGet contexts in `ModuleLoadingContext.cs`
      - `NugetLoggerAdapter.cs`
      - the `NuGet.*` package references
      - the `SelfHostingExtensions` local-feed overloads
      - the embedded-asset provider
      - `build/SharedTools.Web.targets` (F9)
      - `ExampleWebApp/nuget.config`
      - every `C:\LocalNuGet`

      Fix F12 and F13 on the way.
- [x] Hub: drop and status display, `Home` links, remove `NuGetVersionService`.
- [x] Tests. The test build publishes ExampleWebModule and a TestModule drop (`eng/ModuleDrops.targets`);
      tests copy them into a temp store and check:
  - its Razor page renders
  - its `/_content` asset is served from disk
  - `Modules:` options bind
  - a duplicate entry assembly is rejected
  - a drop that throws in `ConfigureServices` falls back to `.prev` and leaves no stray services
  - a leftover `.booting` triggers the swap and records `Crash`; with no `.prev`, or a second crash
    after a rollback, the drop is disabled instead of swapped back
- [x] Update the template, `CLAUDE.md`, the README and `Instructions/*.md` for drops. Ship
      SharedTools.Web and ModuleManagement **3.0.0** (built on branch `module-drops`; publishing = merge
      to master, which the publish workflow releases). blogfor.net keeps running 2.0.1 untouched,
      because nothing in production depends on 3.0 yet.

**Scoreboard:**
- [ ] Target `net11.0`, reference SharedTools.Web 3.x compile-only, run CI on the .NET 11 SDK.
- [ ] Use on-disk `wwwroot` (the `Content` item) instead of `EmbeddedResource` and `Page()`. Keep the
      `{{v}}` cache-busting.
- [ ] Config from `Modules:Scoreboard:*` through `AddModuleOptions` (F11).
- [ ] Add `deploy.ps1`.
- [ ] Verify: a local module host with `Modules:Paths` pointed at `out/`, and the existing Playwright
      E2E suite against it.

### Phase 2: module host and cutover (hosting-service session)

- [ ] Scoreboard's blob config goes into the module host's compose env
      (`Modules__Scoreboard__...`). Rotating the old committed key can happen later.
- [ ] Build `domains/modules`:
      - Program.cs from 2.4
      - the upload API from 2.3 (adapted from `AgentSiteStore`)
      - data-protection keys on the volume
      - its own ECR image and CI job
- [ ] Compose service `modules` with the container settings from 2.4, plus `Modules__UploadKey`.
      Locally, `MODULE_UPLOAD_KEY` goes in the gitignored `.env`.
- [ ] Gateway:
      - a catch-all route for `modules.blogfor.net`, with DNS and a certificate
      - the two blogfor.net routes `/Scoreboard/{**rest}` and `/_content/Scoreboard/{**rest}`
- [ ] Do the first upload of the Scoreboard drop with `deploy.ps1`, exactly as an agent would.
- [ ] Remove `AddApplicationPartModules([...])` and the hub from blogfor.net. Pin it to SharedTools.Web
      3.x, or drop the reference if nothing else uses it.
- [ ] Verify in production:
  - `blogfor.net/Scoreboard/`, `/Scoreboard/game` and `/_content/Scoreboard/app.js` return 200.
  - Existing games and stats load.
  - `modules.blogfor.net/_modules` shows the drop.
  - Roll a no-op change forward, roll back, roll forward.
  - A deliberately broken drop (one that throws in a hosted service) self-heals to `.prev` within one
    restart.

### Phase 3: retire the old paths (Scoreboard repo)

- [ ] Delete `ci.yml` (the bump bot) and `tag-and-publish.yml`. Remove the `NUGET_API_KEY` secret.
      Leave existing nuget.org versions as they are.
- [ ] Optional: a `deploy-module.yml` GitHub Action that runs `deploy.ps1` on push, with the key as a
      repository secret.

## 4. Risks

| Risk | Mitigation |
|---|---|
| The upload key is a code-execution credential | Separate key; dedicated, hardened container; `uploads.log` with SHA; rotate through compose with `--force-recreate` |
| A bad drop | Validation before swap; `.prev` fallback on caught failures; `.booting` crash guard for uncaught ones; `rollback` endpoint; `_status/{id}.json` written on `ApplicationStarted` |
| A restart drops in-flight requests | `StopApplication` drains, and the restart takes 2–5 s. It only affects the module host, not the blog. |
| A host image update (Watchtower) breaks a module | Shows as a captured Load failure; the `.prev` fallback applies |
| Agents need to discover the interface | `GET /api/modules` returns plain-text instructions, the same pattern as `GET /a` |

## 5. Decisions (Andrew, 2026-11-14)

- **D1. A dedicated container with as little overhead as possible.**
  - It is set up once as an ordinary new domain app, following HostingService's existing
    new-domain checklist (already done six times). It has its own hostname and a catch-all route,
    so each new module costs nothing at the gateway.
  - The hardening is a few compose lines. The `edge` network and IMDSv2 stay as separate, optional
    follow-ups.
  - The loader doesn't care which host runs it, so falling back to "inside blogfor.net" later is a
    one-line change in that host's Program.cs.
- **D2. Delete runtime NuGet in 3.0.**
- **D3. Push only.** `PUT` a zip, optionally from a GitHub Action.
- **Key rotation is deferred.** The committed storage key is in a *private* repo (HostingService
  visibility checked) for a private site. Rotate it when convenient. It does not gate Phase 2.

## 6. Out of scope

- Hot reload and unloading. Restarting is the design.
- Process-per-module or any orchestration.
- Signing, hash allowlists, a contract-version gate, `module.json`, keeping more than one previous drop.
