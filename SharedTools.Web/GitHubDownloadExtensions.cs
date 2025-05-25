using System.Net;
using System.Text.RegularExpressions;

namespace SharedTools.Web;

public record GitHubWebModuleResource(string Owner, string Repo, Func<string, string> FilenameBuilder, string? Version = null);

public static class GitHubDownloadExtensions
{
    public static Uri BuildLatestUrl(GitHubWebModuleResource resource)
    {
        return new Uri($"https://github.com/{resource.Owner}/{resource.Repo}/releases/latest");
    }

    public static Uri BuildReleaseUrl(GitHubWebModuleResource resource)
    {
        if (string.IsNullOrEmpty(resource.Version))
        {
            throw new ArgumentException("Version must be provided to build a specific release URL.", nameof(resource));
        }
        return new Uri($"https://github.com/{resource.Owner}/{resource.Repo}/releases/download/v{resource.Version}/{resource.FilenameBuilder(resource.Version)}");
    }

    public static async Task<(string Version, Uri ReleaseUrl)> GetLatestReleaseVersionAsync(GitHubWebModuleResource resource)
    {
        var latestUrl = BuildLatestUrl(resource);
        // Create a handler to prevent auto-redirect
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var httpClient = new HttpClient(handler);

        var response = await httpClient.GetAsync(latestUrl);
        if (response.StatusCode == HttpStatusCode.Redirect || response.StatusCode == HttpStatusCode.MovedPermanently)
        {
            var redirectUrl = response.Headers.Location?.ToString()
                ?? throw new InvalidOperationException($"latestUrl returned HttpStatusCode={response.StatusCode} but location header is empty. Url=\"{latestUrl}\"");

            var match = Regex.Match(redirectUrl, @"tag/v(?<version>\d+\.\d+\.\d+)");
            if (match.Success)
            {
                return (Version: match.Groups["version"].Value, ReleaseUrl: new Uri(redirectUrl));
            }

            throw new InvalidOperationException($"latestUrl did redirect but couldn't parse the version from. Redirect=\"{redirectUrl}\" Url=\"{latestUrl}\"");
        }

        throw new InvalidOperationException($"latestUrl returned HttpStatusCode={response.StatusCode}. Url=\"{latestUrl}\"");
    }
}
