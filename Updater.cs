using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace IslandBar;

public record Release(string Tag, string Url, string Sha256);

// Cherche une version plus récente dans les Releases GitHub. Rien ne s'installe sans un clic sur la notification.
static class Updater
{
    const string Latest = "https://api.github.com/repos/LKDM7/IslandBar/releases/latest";
    const string Downloads = "https://github.com/LKDM7/IslandBar/releases/download/";
    const string Setup = "IslandBar-Setup.exe";

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) }; // l'installateur pèse 46 Mo
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IslandBar");
        return client;
    }

    public static Version Current => typeof(Updater).Assembly.GetName().Version;

    // null tant qu'il n'y a rien de neuf, y compris hors ligne ou si le dépôt est privé (404).
    public static async Task<Release> Check()
    {
        try
        {
            using var response = await Http.GetAsync(Latest);
            return response.IsSuccessStatusCode ? Parse(await response.Content.ReadAsStringAsync(), Current) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
    }

    public static Release Parse(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString();
        if (!IsNewer(tag, current)) return null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != Setup) continue;
            var url = new Uri(asset.GetProperty("browser_download_url").GetString()).AbsoluteUri; // résout les "../"
            var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() : null;
            // Sans empreinte vérifiable ou hors du dépôt, on préfère ne rien proposer.
            if (url.StartsWith(Downloads, StringComparison.Ordinal) && digest?.StartsWith("sha256:", StringComparison.Ordinal) == true)
                return new(tag, url, digest["sha256:".Length..]);
        }
        return null;
    }

    public static bool IsNewer(string tag, Version current) =>
        Version.TryParse(tag?.TrimStart('v'), out var latest)
        && latest.CompareTo(new Version(current.Major, current.Minor, current.Build)) > 0;

    public static bool Verified(byte[] file, string sha256) =>
        Convert.ToHexString(SHA256.HashData(file)).Equals(sha256, StringComparison.OrdinalIgnoreCase);

    // L'installateur ferme l'île, la remplace et la relance.
    public static async Task Install(Release release)
    {
        var file = await Http.GetByteArrayAsync(release.Url);
        if (!Verified(file, release.Sha256)) throw new InvalidDataException("Empreinte de l'installateur incorrecte");
        // shortcut: l'installateur reste dans le dossier temporaire, à supprimer au lancement si l'on veut libérer ces 46 Mo
        var path = Path.Combine(Path.GetTempPath(), Setup);
        await File.WriteAllBytesAsync(path, file);
        Process.Start(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART").Dispose();
    }
}
