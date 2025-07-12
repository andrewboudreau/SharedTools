using System.Text.Json;
using Azure.Storage.Blobs;

namespace ExampleWebModule;

public class AzureBlobExampleStorage : IExampleBlobStorage
{
    private readonly BlobContainerClient container;

    public AzureBlobExampleStorage(BlobServiceClient client, string containerName)
    {
        container = client.GetBlobContainerClient(containerName);
    }

    public async Task SaveAsync(ExampleData example, CancellationToken ct = default)
    {
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        var blob = container.GetBlobClient(GetFileName(example.Id));
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, example, cancellationToken: ct);
        stream.Position = 0;
        await blob.UploadAsync(stream, overwrite: true, cancellationToken: ct);
    }

    public async Task<ExampleData?> LoadAsync(Guid exampleId, CancellationToken ct = default)
    {
        var blob = container.GetBlobClient(GetFileName(exampleId));
        if (!await blob.ExistsAsync(ct))
            return null;

        var response = await blob.DownloadAsync(cancellationToken: ct);
        return await JsonSerializer.DeserializeAsync<ExampleData>(response.Value.Content, cancellationToken: ct);
    }

    private static string GetFileName(Guid id) => $"{id}.json";
}
