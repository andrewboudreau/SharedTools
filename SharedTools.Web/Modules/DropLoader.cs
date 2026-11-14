using System.Reflection;
using System.Runtime.Loader;

using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SharedTools.Web.Modules;

/// <summary>Loads drops from disk into isolated load contexts and runs their ConfigureServices.</summary>
internal sealed class DropLoader(
    IServiceCollection services,
    ModuleRegistry registry,
    ApplicationPartManager partManager,
    ILogger logger)
{
    /// <summary>
    /// Loads every live drop in a managed store. A new drop that fails to load, or that was new when
    /// the host last crashed during startup, is swapped with <c>{id}.prev</c> on disk and the previous
    /// drop is loaded instead. If there is no previous drop, or the previous drop crashes too, a
    /// crashing drop is moved to <c>{id}.disabled</c> so the host can start.
    /// </summary>
    public void LoadStore(ModuleStore store)
    {
        if (!Directory.Exists(store.Root))
        {
            logger.LogInformation("Module root {Root} does not exist; no managed modules loaded", store.Root);
            return;
        }

        registry.Store = store;
        var crashed = store.BootMarkerExists;
        store.WriteBootMarker();

        if (crashed)
        {
            logger.LogWarning("The host did not finish starting last time; new module drops will be rolled back");
        }

        foreach (var id in store.ModuleIds())
        {
            var directory = store.ModuleDirectory(id);
            var dropId = ModuleStore.ReadDropId(directory);
            var lastStatus = store.ReadStatus(id);

            // New = not known to have loaded on this host: no status, pending, or a drop id that is
            // neither the last attempted nor the last serving drop (copied in by hand).
            var isNew = lastStatus is null
                || lastStatus.Status == ModuleDropStatus.Pending
                || (!string.Equals(lastStatus.Drop, dropId, StringComparison.Ordinal)
                    && !string.Equals(lastStatus.Serving, dropId, StringComparison.Ordinal));

            // A pending status with a phase is one this loader wrote when it rolled back; the live drop
            // is already the fallback, so a second failure must not swap back again.
            var isRollback = lastStatus is { Status: ModuleDropStatus.Pending, Phase: not null };

            var state = new ModuleRegistry.PendingStatus { AttemptedDrop = dropId };
            registry.StatusById[id] = state;

            if (crashed && isNew)
            {
                state.Failure = Fail(id, ModuleLoadPhase.Crash, "The host crashed during startup while this drop was new.", dropId);

                if (store.HasPrevious(id) && !isRollback)
                {
                    RollBack(store, id, state);
                }
                else
                {
                    logger.LogWarning("Disabling module {ModuleId}: drop {Drop} crashed the host and has nothing to fall back to", id, dropId);
                    store.Disable(id);
                    store.WriteStatus(id, new ModuleDropStatus(dropId, ModuleDropStatus.Failed,
                        nameof(ModuleLoadPhase.Crash), state.Failure.Error, Serving: null, DateTimeOffset.UtcNow));
                }

                continue;
            }

            if (TryLoad(id, directory, dropId, out var failure))
            {
                state.ServingDrop = dropId;
                continue;
            }

            state.Failure = failure;

            if (isNew && !isRollback && store.HasPrevious(id))
            {
                RollBack(store, id, state);
            }
        }
    }

    /// <summary>
    /// Swaps <c>{id}</c> with <c>{id}.prev</c>, records the rollback on disk before loading anything
    /// (so a crash while loading the previous drop disables it instead of swapping back), then loads it.
    /// </summary>
    private void RollBack(ModuleStore store, string id, ModuleRegistry.PendingStatus state)
    {
        logger.LogWarning("Rolling module {ModuleId} back from drop {Drop} to its previous drop", id, state.AttemptedDrop);
        store.SwapWithPrevious(id);

        var directory = store.ModuleDirectory(id);
        var previousDrop = ModuleStore.ReadDropId(directory);
        store.WriteStatus(id, new ModuleDropStatus(previousDrop, ModuleDropStatus.Pending,
            state.Failure!.Phase.ToString(), $"Rolled back from drop {state.AttemptedDrop}: {state.Failure.Error}",
            Serving: null, DateTimeOffset.UtcNow));

        if (TryLoad(id, directory, previousDrop, out _))
        {
            state.ServingDrop = previousDrop;
        }
    }

    /// <summary>Loads a drop that is not in a managed store (a <c>Modules:Paths</c> entry).</summary>
    public void LoadUnmanaged(string id, string directory)
    {
        if (!Directory.Exists(directory))
        {
            Fail(id, ModuleLoadPhase.Load, $"Directory '{directory}' does not exist.", null);
            return;
        }

        TryLoad(id, directory, ModuleStore.ReadDropId(directory), out _);
    }

    private bool TryLoad(string id, string directory, string? dropId, out ModuleFailure? failure)
    {
        failure = null;

        // Load: find the entry assembly, load it in isolation, find the module type.
        IApplicationPartModule module;
        Assembly assembly;
        try
        {
            var entryPath = FindEntryAssembly(directory);
            var assemblyName = AssemblyName.GetAssemblyName(entryPath).Name!;

            if (IsAlreadyLoaded(assemblyName))
            {
                throw new InvalidOperationException(
                    $"Assembly '{assemblyName}' is already loaded by the host or another module.");
            }

            var context = new ModuleAssemblyLoadContext(entryPath);
            assembly = context.LoadFromAssemblyPath(entryPath);
            module = CreateModule(assembly);

            if (!IsValidName(module.Name))
            {
                throw new InvalidOperationException($"Module name '{module.Name}' is not a valid path segment.");
            }

            if (registry.Modules.Any(m => string.Equals(m.Name, module.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"A module named '{module.Name}' is already loaded.");
            }

            registry.LoadContexts.Add(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Module {ModuleId} failed to load from {Directory}", id, directory);
            failure = Fail(id, ModuleLoadPhase.Load, Describe(ex), dropId);
            return false;
        }

        // ConfigureServices: run against a copy so a throw leaves the host's services untouched.
        IServiceCollection scratch = new ServiceCollection();
        foreach (var descriptor in services)
        {
            scratch.Add(descriptor);
        }

        try
        {
            module.ConfigureServices(scratch);
            ModuleExtensions.AddParts(partManager, assembly);
            module.ConfigureApplicationParts(partManager);
        }
        catch (Exception ex)
        {
            RemoveParts(assembly);
            logger.LogError(ex, "Module {ModuleId} failed in ConfigureServices", id);
            failure = Fail(id, ModuleLoadPhase.ConfigureServices, Describe(ex), dropId);
            return false;
        }

        services.Clear();
        foreach (var descriptor in scratch)
        {
            services.Add(descriptor);
        }

        registry.Modules.Add(module);
        registry.Loaded.Add(new LoadedModule(module, id, directory, dropId));
        logger.LogInformation("Loaded module {ModuleName} ({ModuleId}, drop {Drop})", module.Name, id, dropId ?? "unversioned");
        return true;
    }

    private ModuleFailure Fail(string id, ModuleLoadPhase phase, string error, string? dropId)
    {
        var failure = new ModuleFailure(id, phase, error, dropId);
        registry.Failures.Add(failure);
        return failure;
    }

    private static string FindEntryAssembly(string directory)
    {
        var depsFiles = Directory.GetFiles(directory, "*.deps.json", SearchOption.TopDirectoryOnly);
        if (depsFiles.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one *.deps.json in the drop (the output of dotnet publish), found {depsFiles.Length}.");
        }

        var entryPath = depsFiles[0][..^".deps.json".Length] + ".dll";
        if (!File.Exists(entryPath))
        {
            throw new FileNotFoundException($"The drop has {Path.GetFileName(depsFiles[0])} but no {Path.GetFileName(entryPath)}.");
        }

        return entryPath;
    }

    private bool IsAlreadyLoaded(string assemblyName) =>
        AssemblyLoadContext.Default.Assemblies.Any(a => a.GetName().Name == assemblyName)
        || registry.Loaded.Any(l => l.Module.GetType().Assembly.GetName().Name == assemblyName);

    private static IApplicationPartModule CreateModule(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var first = ex.LoaderExceptions.FirstOrDefault(e => e is not null);
            throw new InvalidOperationException($"Could not load types from {assembly.GetName().Name}: {first?.Message}", ex);
        }

        var moduleTypes = types
            .Where(t => t is { IsClass: true, IsAbstract: false }
                && typeof(IApplicationPartModule).IsAssignableFrom(t)
                && t.GetConstructor(Type.EmptyTypes) is not null)
            .ToList();

        return moduleTypes.Count switch
        {
            1 => (IApplicationPartModule)Activator.CreateInstance(moduleTypes[0])!,
            0 => throw new InvalidOperationException(
                $"{assembly.GetName().Name} has no public class implementing {nameof(IApplicationPartModule)} with a parameterless constructor."),
            _ => throw new InvalidOperationException(
                $"{assembly.GetName().Name} has {moduleTypes.Count} module classes ({string.Join(", ", moduleTypes.Select(t => t.Name))}); a drop must have exactly one."),
        };
    }

    private void RemoveParts(Assembly assembly)
    {
        var parts = partManager.ApplicationParts;
        for (var i = parts.Count - 1; i >= 0; i--)
        {
            if (parts[i] is AssemblyPart ap && ap.Assembly == assembly
                || parts[i] is CompiledRazorAssemblyPart cp && cp.Assembly == assembly)
            {
                parts.RemoveAt(i);
            }
        }
    }

    internal static bool IsValidName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && !name.Contains("..")
        && name.IndexOfAny(['/', '\\']) < 0
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static string Describe(Exception ex) =>
        ex is TargetInvocationException { InnerException: { } inner } ? inner.Message : ex.Message;
}
