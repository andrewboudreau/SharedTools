using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace SharedTools.Web.Modules;

public static class WebModuleExtensions
{
    /// <summary>
    /// Discovers plugin DLLs in the specified folder, loads them, registers their Razor parts,
    /// merges their static assets, and invokes their ConfigureServices.
    /// </summary>
    public static WebApplicationBuilder AddWebModules(this WebApplicationBuilder builder, string pluginsFolder)
    {
        var env = builder.Environment;
        // Ensure Razor Pages is added to access PartManager
        var partManager = builder.Services.AddRazorPages().PartManager;
        var webModuleInstance = new List<IWebModule>();

        if (Directory.Exists(pluginsFolder))
        {
            foreach (var dll in Directory.GetFiles(pluginsFolder, "*.dll", SearchOption.TopDirectoryOnly))
            {
                // Load assembly in isolated context
                var loadContext = new WebModuleLoadContext(dll);
                var assembly = loadContext.LoadFromAssemblyPath(dll);

                // Register compiled Razor views if present
                var viewsDll = Path.Combine(Path.GetDirectoryName(dll)!, Path.GetFileNameWithoutExtension(dll) + ".Views.dll");
                if (File.Exists(viewsDll))
                {
                    var viewsAssembly = loadContext.LoadFromAssemblyPath(viewsDll);
                    partManager.ApplicationParts.Add(new CompiledRazorAssemblyPart(viewsAssembly));
                }

                // Add the plugin assembly so its pages/controllers are discovered
                partManager.ApplicationParts.Add(new AssemblyPart(assembly));

                // Merge plugin static files (wwwroot) into host's WebRoot
                var embeddedProvider = new ManifestEmbeddedFileProvider(assembly, "wwwroot");
                env.WebRootFileProvider = new CompositeFileProvider(env.WebRootFileProvider, embeddedProvider);

                // Find and initialize plugin startup classes
                var pluginTypes = assembly.GetTypes()
                    .Where(t => typeof(IWebModule).IsAssignableFrom(t)
                                && !t.IsInterface
                                && !t.IsAbstract);

                foreach (var type in pluginTypes)
                {
                    var plugin = (IWebModule)Activator.CreateInstance(type)!;
                    plugin.ConfigureServices(builder.Services);
                    webModuleInstance.Add(plugin);
                }
            }
        }

        // Register plugin instances for later use in Configure
        builder.Services.AddSingleton<IReadOnlyCollection<IWebModule>>(webModuleInstance);
        return builder;
    }

    /// <summary>
    /// Invokes each plugin's Configure method to wire up endpoints and middleware.
    /// </summary>
    public static WebApplication UseWebModules(this WebApplication app)
    {
        var plugins = app.Services.GetRequiredService<IReadOnlyCollection<IWebModule>>();
        foreach (var plugin in plugins)
        {
            plugin.Configure(app);
        }
        return app;
    }
}
