using System.Net;
using System.Text.RegularExpressions;

namespace SharedTools.Web;

public static class GitHubDownloadExtensions
{
    public static Uri BuildLatestUrl(string repo, string owner)
    {
        return new Uri($"https://github.com/{owner}/{repo}/releases/latest");
    }

    public static Uri BuildReleaseUrl(string repo, string owner, string version, Func<string, string> filenameBuilder)
    {
        return new Uri($"https://github.com/{owner}/{repo}/releases/download/v{version}/{filenameBuilder(version)}");
    }

    public static async Task<(string Version, Uri ReleaseUrl)> GetLatestReleaseVersionAsync(string latestUrl)
    {
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
