using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Cloudflare_Tunnel.Infrastructure;

public sealed record ReleaseInfo(
    string TagName,
    string HtmlUrl,
    string? ExeDownloadUrl,
    string ReleaseNotes = "");

public static class AppUpdater
{
    public const string CurrentVersion = "2.1.0";
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/Ezgaminglogan/Cloudflare-Tunnel/releases/latest";

    internal static bool IsNewerVersion(string currentVersion, string latestTag)
    {
        string cleanCurrent = currentVersion.Trim().TrimStart('v', 'V');
        string cleanLatest = latestTag.Trim().TrimStart('v', 'V');

        int dashIdx = cleanLatest.IndexOf('-');
        if (dashIdx > 0) cleanLatest = cleanLatest[..dashIdx];

        return Version.TryParse(cleanCurrent, out var cur) &&
               Version.TryParse(cleanLatest, out var lat) &&
               lat > cur;
    }

    internal static bool TryParseRelease(JsonElement root, [NotNullWhen(true)] out ReleaseInfo? release)
    {
        release = null;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("tag_name", out var tagProp))
        {
            return false;
        }

        string? tagName = tagProp.GetString();
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return false;
        }

        string htmlUrl = root.TryGetProperty("html_url", out var urlProp) && !string.IsNullOrWhiteSpace(urlProp.GetString())
            ? urlProp.GetString()!
            : "https://github.com/Ezgaminglogan/Cloudflare-Tunnel/releases";

        string? exeUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string? name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                string? downloadUrl = asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;

                if (!string.IsNullOrWhiteSpace(name) &&
                    !string.IsNullOrWhiteSpace(downloadUrl) &&
                    name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    exeUrl = downloadUrl;
                    break;
                }
            }
        }

        release = new ReleaseInfo(tagName, htmlUrl, exeUrl, FormatReleaseNotes(root));
        return true;
    }

    private static string FormatReleaseNotes(JsonElement root)
    {
        string rawBody = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
        var cleanLines = rawBody
            .Split('\n')
            .Select(l => l.Trim().TrimStart('#').Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Take(8)
            .ToList();

        if (cleanLines.Count > 0)
        {
            return string.Join(Environment.NewLine, cleanLines);
        }

        string title = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString()?.Trim() ?? "" : "";
        return !string.IsNullOrWhiteSpace(title)
            ? title
            : "Performance improvements, bug fixes, and engine updates.";
    }

    public static void CleanupOldUpdateBinary()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                string oldPath = exePath + ".old";
                if (File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                }
            }
        }
        catch { }
    }

    public static async Task<ReleaseInfo?> FetchLatestReleaseAsync(CancellationToken ct = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Cloudflare-Tunnel-Manager/2.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            string json = await client.GetStringAsync(LatestReleaseApiUrl, ct);
            using var doc = JsonDocument.Parse(json);
            return TryParseRelease(doc.RootElement, out var release) ? release : null;
        }
        catch
        {
            return null;
        }
    }

    public static async Task CheckOnStartupAsync()
    {
        var release = await FetchLatestReleaseAsync();
        if (release != null && IsNewerVersion(CurrentVersion, release.TagName))
        {
            ConsoleWindowHelper.ShowInfoMessageBox(
                $"A new update ({release.TagName}) is available!\n\n" +
                $"Current Version: v{CurrentVersion}\n" +
                $"Latest Version:  {release.TagName}\n\n" +
                $"What's in this update:\n{release.ReleaseNotes}\n\n" +
                "Select [UPDATE] from the main menu to download and install it.",
                "Cloudflare Tunnel Manager — Update Available");
        }
    }

    public static async Task<(bool UpdatedInPlace, string Message)> ApplySelfUpdateAsync(
        ReleaseInfo release,
        IProgress<(long bytesProcessed, long totalBytes, double speedBytesPerSec)>? progress = null,
        CancellationToken ct = default)
    {
        string? exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath) &&
            File.Exists(exePath) &&
            !Path.GetFileName(exePath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(release.ExeDownloadUrl))
        {
            string tempNewPath = exePath + ".new";
            string backupOldPath = exePath + ".old";

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Cloudflare-Tunnel-Manager/2.0");

                using var response = await client.GetAsync(release.ExeDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? -1;
                await using var stream = await response.Content.ReadAsStreamAsync(ct);

                await using (var dest = new FileStream(tempNewPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
                {
                    var sw = Stopwatch.StartNew();
                    long bytesProcessed = 0;
                    byte[] buffer = new byte[128 * 1024];
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                    {
                        await dest.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                        bytesProcessed += bytesRead;
                        double speed = sw.Elapsed.TotalSeconds > 0.05 ? bytesProcessed / sw.Elapsed.TotalSeconds : 0;
                        progress?.Report((bytesProcessed, totalBytes, speed));
                    }
                }

                // Windows allows renaming a currently running executable on NTFS
                if (File.Exists(backupOldPath))
                {
                    try { File.Delete(backupOldPath); } catch { }
                }
                File.Move(exePath, backupOldPath);
                File.Move(tempNewPath, exePath);

                return (true, $"Updated executable to {release.TagName}. Restart the application to run the new version.");
            }
            finally
            {
                try { if (File.Exists(tempNewPath)) File.Delete(tempNewPath); } catch { }
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo(release.HtmlUrl) { UseShellExecute = true });
        }
        catch { }

        return (false, $"Opened GitHub Release {release.TagName} in your browser.");
    }
}
