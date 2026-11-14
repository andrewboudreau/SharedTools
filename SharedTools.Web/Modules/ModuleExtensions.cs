using System.Reflection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SharedTools.Web.Modules;

/// <summary>
/// Loads module drops (zipped <c>dotnet publish</c> output, extracted on disk) at startup.
/// <code>
/// builder.AddModules();   // every drop under Modules:Root (default /data/modules), plus Modules:Paths
/// var app = builder.Build();
/// app.UseModules();       // /_content/{Name}/ static files, then each module's Configure
/// </code>
/// No network, no NuGet: a drop's own dependencies resolve from its <c>.deps.json</c>.
/// </summary>
public static class ModuleExtensions
{
    /// <summary>Config key for the managed module root.</summary>
    public const string RootKey = "Modules:Root";

    /// <summary>Config section of <c>id = directory</c> pairs for unmanaged drops (local development).</summary>
    public const string PathsKey = "Modules:Paths";

    /// <summary>The managed module root used when <see cref="RootKey"/> is not set.</summary>
    public const string DefaultRoot = "/data/modules";

    /// <summary>
    /// Loads every module drop in the managed root (<paramref name="root"/>, else <c>Modules:Root</c>,
    /// else <c>/data/modules</c>) and every directory listed under <c>Modules:Paths</c>.
    /// </summary>
    /// <remarks>
    /// In the managed root, a newly uploaded drop that fails to load, or that crashes the host during
    /// startup, is rolled back to <c>{id}.prev</c> automatically. Outcomes are written to
    /// <c>_status/{id}.json</c> once the host has started.
    /// </remarks>
    public static WebApplicationBuilder AddModules(this WebApplicationBuilder builder, string? root = null)
    {
        var registry = GetOrCreateRegistry(builder.Services);
        var partManager = builder.Services.AddRazorPages().PartManager;

        using var loggerFactory = CreateBootstrapLoggerFactory();
        var loader = new DropLoader(builder.Services, registry, partManager, loggerFactory.CreateLogger("SharedTools.Modules"));

        var contentRoot = builder.Environment.ContentRootPath;
        root ??= builder.Configuration[RootKey] ?? DefaultRoot;
        loader.LoadStore(new ModuleStore(Path.GetFullPath(root, contentRoot)));

        foreach (var entry in builder.Configuration.GetSection(PathsKey).GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(entry.Value))
            {
                loader.LoadUnmanaged(entry.Key, Path.GetFullPath(entry.Value, contentRoot));
            }
        }

        if (registry.Store is not null && !builder.Services.Any(s => s.ImplementationType == typeof(ModuleStatusWriter)))
        {
            builder.Services.AddHostedService<ModuleStatusWriter>();
        }

        return builder;
    }

    /// <summary>
    /// Adds a module the host references at compile time (for example the module hub). It runs the
    /// same lifecycle as a drop, without an isolated load context.
    /// </summary>
    public static WebApplicationBuilder AddModule<TModule>(this WebApplicationBuilder builder)
        where TModule : IApplicationPartModule, new()
    {
        var registry = GetOrCreateRegistry(builder.Services);
        var partManager = builder.Services.AddRazorPages().PartManager;
        var module = new TModule();

        if (registry.Modules.Any(m => string.Equals(m.Name, module.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A module named '{module.Name}' is already registered.");
        }

        module.ConfigureServices(builder.Services);
        AddParts(partManager, typeof(TModule).Assembly);
        module.ConfigureApplicationParts(partManager);

        registry.Modules.Add(module);
        registry.Loaded.Add(new LoadedModule(module, Id: null, Directory: null, Drop: null));
        return builder;
    }

    /// <summary>
    /// Serves each drop's <c>wwwroot/</c> at <c>/_content/{Name}/</c>, then calls every module's
    /// <c>Configure</c>. A module whose <c>Configure</c> throws is reported as failed.
    /// </summary>
    public static WebApplication UseModules(this WebApplication app)
    {
        var registry = app.Services.GetService<ModuleRegistry>();
        if (registry is null)
        {
            return app;
        }

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SharedTools.Modules");

        foreach (var loaded in registry.Loaded.Where(l => l.Directory is not null))
        {
            var webRoot = Path.Combine(loaded.Directory!, "wwwroot");
            if (!Directory.Exists(webRoot))
            {
                continue;
            }

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(webRoot),
                RequestPath = $"/_content/{loaded.Module.Name}"
            });
        }

        foreach (var loaded in registry.Loaded)
        {
            try
            {
                loaded.Module.Configure(app);
                logger.LogInformation("Configured module {ModuleName}", loaded.Module.Name);
            }
            catch (Exception ex)
            {
                // Endpoints mapped before the throw stay mapped; the failure is reported, not undone.
                logger.LogError(ex, "Module {ModuleName} failed in Configure", loaded.Module.Name);
                var failure = new ModuleFailure(loaded.Id ?? loaded.Module.Name, ModuleLoadPhase.Configure, ex.Message, loaded.Drop);
                registry.Failures.Add(failure);
                if (loaded.Id is not null && registry.StatusById.TryGetValue(loaded.Id, out var state))
                {
                    state.Failure ??= failure;
                }
            }
        }

        return app;
    }

    /// <summary>The directory a module's assembly was loaded from (its drop directory).</summary>
    public static string ContentRoot(this IApplicationPartModule module) =>
        Path.GetDirectoryName(module.GetType().Assembly.Location) is { Length: > 0 } dir ? dir : AppContext.BaseDirectory;

    /// <summary>The module's <c>wwwroot</c> directory inside its drop.</summary>
    public static string WebRoot(this IApplicationPartModule module) => Path.Combine(module.ContentRoot(), "wwwroot");

    internal static void AddParts(ApplicationPartManager partManager, Assembly assembly)
    {
        var parts = partManager.ApplicationParts;
        if (!parts.Any(p => p is AssemblyPart ap && ap.Assembly == assembly))
        {
            parts.Add(new AssemblyPart(assembly));
        }
        if (!parts.Any(p => p is CompiledRazorAssemblyPart cp && cp.Assembly == assembly))
        {
            parts.Add(new CompiledRazorAssemblyPart(assembly));
        }
    }

    private static ModuleRegistry GetOrCreateRegistry(IServiceCollection services)
    {
        if (services.FirstOrDefault(s => s.ServiceType == typeof(ModuleRegistry))?.ImplementationInstance is ModuleRegistry existing)
        {
            return existing;
        }

        var registry = new ModuleRegistry();
        services.AddSingleton(registry);
        services.AddSingleton<IReadOnlyCollection<IApplicationPartModule>>(_ => registry.Modules.AsReadOnly());
        return registry;
    }

    private static ILoggerFactory CreateBootstrapLoggerFactory() =>
        LoggerFactory.Create(logging => logging
            .AddConsole(options => options.FormatterName = "terse")
            .AddConsoleFormatter<Logging.TerseConsoleFormatter, Logging.TerseConsoleFormatterOptions>()
            .SetMinimumLevel(LogLevel.Information));
}
