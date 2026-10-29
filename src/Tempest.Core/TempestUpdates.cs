using System.Net.Http.Headers;
using System.Text.Json;

namespace Tempest.Core;

public static class TempestUpdates
{
    public const string ReleasesUrl = "https://github.com/SxGoldenxS/Tempest/releases/latest";
    static readonly HttpClient client = CreateClient();

    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Tempest", "0.3"));
        return http;
    }

    public static async Task<Version?> Check(Version installed, CancellationToken token)
    {
        var json = await client.GetStringAsync("https://api.github.com/repos/SxGoldenxS/Tempest/releases/latest", token);
        return NewerVersion(json, installed);
    }

    public static Version? NewerVersion(string releaseJson, Version installed)
    {
        using var json = JsonDocument.Parse(releaseJson);
        var release = json.RootElement;
        if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean() ||
            release.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean()) return null;
        if (!release.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String) return null;
        var value = tag.GetString()!;
        const string prefix = "tempest-v";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || !Version.TryParse(value[prefix.Length..], out var latest)) return null;
        // Assembly versions include a revision; release tags use major.minor.patch.
        var current = new Version(installed.Major, installed.Minor, Math.Max(0, installed.Build));
        return latest > current ? latest : null;
    }
}
