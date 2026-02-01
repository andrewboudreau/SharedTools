using System.Text.Json;

namespace SharedTools.ModuleManagement.Services;

public interface INuGetVersionService
{
    Task<string?> GetLatestVersionAsync(string packageId, CancellationToken cancellationToken = default);
    Task<Dictionary<string, string?>> GetLatestVersionsAsync(IEnumerable<string> packageIds, CancellationToken cancellationToken = default);
}

public class NuGetVersionService : INuGetVersionService
{
    private readonly HttpClient httpClient;

    public NuGetVersionService(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<string?> GetLatestVersionAsync(string packageId, CancellationToken cancellationToken = default)
    {
        try
        {
            var id = packageId.ToLowerInvariant();
            var url = $"https://api.nuget.org/v3-flatcontainer/{id}/index.json";
            var response = await httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (doc.RootElement.TryGetProperty("versions", out var versions) && versions.GetArrayLength() > 0)
            {
                return versions[versions.GetArrayLength() - 1].GetString();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<Dictionary<string, string?>> GetLatestVersionsAsync(IEnumerable<string> packageIds, CancellationToken cancellationToken = default)
    {
        var ids = packageIds.ToList();
        var tasks = ids.Select(id => GetLatestVersionAsync(id, cancellationToken)).ToArray();
        var results = await Task.WhenAll(tasks);

        var dict = new Dictionary<string, string?>();
        for (var i = 0; i < ids.Count; i++)
        {
            dict[ids[i]] = results[i];
        }

        return dict;
    }
}
