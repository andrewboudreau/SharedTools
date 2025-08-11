using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedTools.Web.Modules;

namespace ExampleWebModule;

/// <summary>
/// Example implementation of an ApplicationPart-based module that demonstrates
/// service registration, configuration binding, and ApplicationPart integration.
/// </summary>
public class ExampleApplicationPartModule : IApplicationPartModule
{
    public string Name => "ExampleWebModule";

    public void ConfigureServices(IServiceCollection services)
    {
        // Configure strongly-typed options from IConfiguration
        services.AddOptions<AzureBlobStorageOptions>()
            .BindConfiguration(AzureBlobStorageOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register module-specific services
        services.AddSingleton<IExampleBlobStorage>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AzureBlobStorageOptions>>().Value;
            var blobServiceClient = new BlobServiceClient(options.ConnectionString);
            return new AzureBlobExampleStorage(blobServiceClient, options.ExamplesContainerName);
        });

        // Module services are already registered
        // Razor Pages support is added by the host application
    }

    public void ConfigureApplicationParts(ApplicationPartManager applicationPartManager)
    {
        // The module's assembly is already added as an ApplicationPart
        // Here we can add additional parts if needed, such as:
        // - Additional assemblies containing controllers
        // - Custom feature providers
        // - Related assemblies with compiled Razor views

        // For this example, we don't need to add anything extra
        // The ApplicationPart already handles our assembly
    }

    public void Configure(WebApplication app)
    {
        // Configure module-specific middleware or endpoints
        // For this example module, we might add:

        // Module-specific endpoints
        app.MapGet("/example-module/info", () => new
        {
            Module = Name,
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Status = "Active"
        });

        // Module's Razor Pages are automatically mapped by the host application
        // They will be accessible under /ExampleWebModule due to the Area structure
    }
}