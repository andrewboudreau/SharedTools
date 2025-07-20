# SharedTools Planning and Architecture

This document captures architectural decisions, conventions, and future ideas for the SharedTools module system.

## Core Design Principles

1. **Minimal Core**: The core library (SharedTools.Web) should have the absolute smallest set of dependencies and provide only essential module loading functionality
2. **Convention Over Configuration**: Use consistent conventions to reduce the need for explicit configuration
3. **Runtime Composition**: Modules are discovered and loaded at startup from NuGet packages
4. **Isolation by Default**: Each module gets its own AssemblyLoadContext for dependency isolation

## Conventions

### Module Entry Points
- Primary entry point: `/{ModuleName}/` 
  - If assembly name ends with "Module", strip that suffix
  - Example: `ExampleWebModule.dll` → `/ExampleWeb/`
- Future: Admin entry point: `/{ModuleName}/admin` (when needed)

### Static Assets
- Module static assets served from: `/_content/{ModuleName}/`
- Embedded in module assembly under `wwwroot` folder

### Module Naming
- NuGet package name = Module assembly name
- Leverage NuGet's unique naming requirements for uniqueness

## Architecture Decisions

### Module vs Shared Components

**Modules** (Runtime Loaded)
- Implement `IApplicationPartModule` interface
- Loaded dynamically at startup from NuGet
- Self-contained with their own dependencies
- Cannot directly reference other modules

**Shared Components** (Design-Time Dependencies)
- Standard NuGet packages referenced at compile time
- Provide contracts/bridges for inter-module communication
- Enable composition without coupling
- Examples: shared DTOs, event contracts, service interfaces

### Inter-Module Communication
- Modules should NOT directly reference each other
- Communication through shared bridge components:
  ```
  ModuleA → SharedContracts ← ModuleB
  ```
- Enables loose coupling and independent deployment

## Future Ideas

### Near Term

1. **Route Discovery**
   - Parse loaded assemblies to discover Razor Pages and controller routes
   - Build route inventory for each module automatically
   - Display discovered routes in Module Management UI

2. **Module Metadata Attributes**
   ```csharp
   [ModuleMetadata(
       DisplayName = "Example Module",
       Description = "...",
       Author = "...",
       Tags = ["demo", "example"]
   )]
   public class ExampleModule : IApplicationPartModule
   ```

3. **Module Health Checks**
   - Standard health check endpoint per module
   - Aggregate health status in management UI

### Medium Term

1. **Module Dependencies**
   - Declare dependencies between modules
   - Ensure load order respects dependencies
   - Prevent loading if dependencies missing

2. **Module Configuration**
   - Standardized configuration section per module
   - UI for viewing/editing module configuration
   - Configuration validation

3. **Event Bus / Message Broker**
   - Shared component for pub/sub between modules
   - In-process initially, could extend to distributed

### Long Term

1. **Hot Reload (with caveats)**
   - Limited support for reloading modules
   - Require module to implement IDisposable
   - Track active requests and block during reload
   - May require process restart for stability

2. **Module Marketplace**
   - Central registry of available modules
   - One-click install from management UI
   - Version compatibility checking

3. **Multi-tenant Module Isolation**
   - Different module sets per tenant
   - Tenant-specific module configuration
   - Shared modules with tenant isolation

## Technical Considerations

### AssemblyLoadContext Management
- Currently using `isCollectible: false` for stability
- Store references to prevent premature collection
- Future hot-reload would require `isCollectible: true` with careful lifecycle management

### Performance
- Module loading happens at startup (acceptable delay)
- Consider lazy loading for rarely-used modules
- Monitor memory usage with many modules

### Security
- Modules run with full application permissions
- Consider module signing/verification
- Audit module access to sensitive resources

## Module Development Guidelines

1. **Keep Modules Focused**: Single responsibility per module
2. **Minimize Dependencies**: Only essential NuGet packages
3. **Use Shared Contracts**: For any cross-module needs
4. **Follow Conventions**: Entry points, static assets, naming
5. **Document Public APIs**: If exposing services to other modules via shared contracts

## Rejected Ideas

1. **Direct Module-to-Module References**: Would create tight coupling and versioning nightmares
2. **Dynamic Route Prefixing**: Adds complexity, convention-based paths are simpler
3. **Module Scripting/Plugins**: Full .NET modules provide better tooling and performance

## Notes

- ASP.NET Core Areas are optional - modules can use them but don't have to
- Similar architectural patterns: Orleans, ASP.NET Core Areas, Orchard Core
- Priority is stability over features like hot-reload