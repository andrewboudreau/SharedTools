using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SharedTools.Web.Modules;

/// <summary>
/// Once the host has started (so every module's <c>Configure</c> has run and the server is listening),
/// writes <c>_status/{id}.json</c> for each managed module and clears the store's boot marker.
/// </summary>
internal sealed class ModuleStatusWriter(
    ModuleRegistry registry,
    IHostApplicationLifetime lifetime,
    ILogger<ModuleStatusWriter> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStarted.Register(WriteStatuses);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void WriteStatuses()
    {
        if (registry.Store is not { } store)
        {
            return;
        }

        try
        {
            foreach (var (id, state) in registry.StatusById)
            {
                store.WriteStatus(id, new ModuleDropStatus(
                    Drop: state.AttemptedDrop,
                    Status: state.Failure is null ? ModuleDropStatus.Loaded : ModuleDropStatus.Failed,
                    Phase: state.Failure?.Phase.ToString(),
                    Error: state.Failure?.Error,
                    Serving: state.ServingDrop,
                    Updated: DateTimeOffset.UtcNow));
            }

            store.ClearBootMarker();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write module status to {Root}", store.Root);
        }
    }
}
