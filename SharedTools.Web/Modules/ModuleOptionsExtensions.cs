using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SharedTools.Web.Modules;

/// <summary>
/// Helpers for binding a module's strongly-typed options from its own configuration namespace.
/// </summary>
public static class ModuleOptionsExtensions
{
    /// <summary>
    /// The root configuration section under which each module's settings live. A module's
    /// options bind from <c>Modules:{moduleId}</c>, keeping each module's configuration
    /// isolated from the host and from other modules (no shared flat keys).
    /// </summary>
    public const string ConfigurationRoot = "Modules";

    /// <summary>
    /// Binds <typeparamref name="TOptions"/> from the module's own configuration section
    /// (<c>Modules:{moduleId}</c>) and validates it with data annotations. Returns the
    /// <see cref="OptionsBuilder{TOptions}"/> so callers can chain further configuration
    /// (for example <c>.ValidateOnStart()</c>).
    /// </summary>
    /// <typeparam name="TOptions">The options type to bind.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="moduleId">The module's id; typically its <c>Name</c>.</param>
    public static OptionsBuilder<TOptions> AddModuleOptions<TOptions>(
        this IServiceCollection services, string moduleId)
        where TOptions : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        return services.AddOptions<TOptions>()
            .BindConfiguration($"{ConfigurationRoot}:{moduleId}")
            .ValidateDataAnnotations();
    }
}
