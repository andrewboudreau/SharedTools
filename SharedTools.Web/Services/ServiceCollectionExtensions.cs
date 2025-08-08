using Microsoft.Extensions.DependencyInjection;

namespace SharedTools.Web.Services;

/// <summary>
/// Extension methods for service collection to check and conditionally register services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Checks if a service type is already registered in the service collection.
    /// </summary>
    /// <typeparam name="T">The service type to check</typeparam>
    /// <param name="services">The service collection</param>
    /// <returns>True if the service is registered, false otherwise</returns>
    public static bool IsRegistered<T>(this IServiceCollection services)
    {
        return services.Any(s => s.ServiceType == typeof(T));
    }

    /// <summary>
    /// Simple helper to check if a type is registered without descriptor inspection.
    /// Just checks if the type exists in the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="serviceType">The service type to check for</param>
    /// <returns>True if the type is registered, false otherwise</returns>
    public static bool HasType(this IServiceCollection services, Type serviceType)
    {
        return services.Any(s => s.ServiceType == serviceType);
    }

    /// <summary>
    /// Generic version of HasType for convenience.
    /// </summary>
    /// <typeparam name="T">The service type to check</typeparam>
    /// <param name="services">The service collection</param>
    /// <returns>True if the type is registered, false otherwise</returns>
    public static bool HasType<T>(this IServiceCollection services)
    {
        return services.HasType(typeof(T));
    }

    /// <summary>
    /// Checks if a keyed service is already registered in the service collection.
    /// </summary>
    /// <typeparam name="T">The service type to check</typeparam>
    /// <param name="services">The service collection</param>
    /// <param name="serviceKey">The service key</param>
    /// <returns>True if the keyed service is registered, false otherwise</returns>
    public static bool IsKeyedRegistered<T>(this IServiceCollection services, object serviceKey)
    {
        return services.Any(s => s.ServiceType == typeof(T) &&  Equals(s.ServiceKey, serviceKey));
    }
}