using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cloudflare_Tunnel.Infrastructure;

/// <summary>
/// Stateful detector for cloudflared quick-tunnel URLs. cloudflared prints the
/// URL inside an ASCII box and can wrap the hostname across lines when stdout
/// is piped, so the last few lines are kept and re-matched with the box
/// separators stripped.
/// </summary>
internal sealed class CloudflareUrlDetector
{
    internal static readonly Regex UrlRegex = new(
        @"https://[a-zA-Z0-9-]+\.trycloudflare\.com",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NoiseRegex = new(@"[\s|]+", RegexOptions.Compiled);

    private readonly Queue<string> _recentLines = new();

    public bool TryDetect(string line, [NotNullWhen(true)] out Uri? url)
    {
        url = null;
        var match = UrlRegex.Match(line);

        if (!match.Success)
        {
            _recentLines.Enqueue(line);
            if (_recentLines.Count > 4) _recentLines.Dequeue();
            match = UrlRegex.Match(NoiseRegex.Replace(string.Concat(_recentLines), ""));
        }

        if (match.Success && Uri.TryCreate(match.Value, UriKind.Absolute, out var uri))
        {
            url = uri;
            return true;
        }
        return false;
    }
}

internal static class NgrokUrlParser
{
    internal static readonly Regex UrlRegex = new(
        @"https://[a-zA-Z0-9-]+(\.[a-zA-Z0-9-]+)*\.ngrok(-free)?\.(app|dev|io|com)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool TryDetect(string line, [NotNullWhen(true)] out Uri? url)
    {
        url = null;
        var match = UrlRegex.Match(line);
        if (match.Success && Uri.TryCreate(match.Value, UriKind.Absolute, out var uri))
        {
            url = uri;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Picks the public https URL for our tunnel out of a `GET /api/tunnels`
    /// response. When a tunnel advertises config.addr it must target our local
    /// port — otherwise a second ngrok agent sharing the 127.0.0.1:4040 API
    /// could hand us the wrong URL.
    /// </summary>
    public static bool TrySelectTunnel(JsonElement root, int localPort, [NotNullWhen(true)] out Uri? url)
    {
        url = null;
        if (!root.TryGetProperty("tunnels", out var tunnels) || tunnels.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        Uri? fallback = null;
        foreach (var tunnel in tunnels.EnumerateArray())
        {
            if (!tunnel.TryGetProperty("public_url", out var urlProp)) continue;

            string? candidate = urlProp.GetString();
            if (string.IsNullOrEmpty(candidate) ||
                !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                !Uri.TryCreate(candidate, UriKind.Absolute, out var parsed))
            {
                continue;
            }

            // Prefer the tunnel that explicitly forwards to our local port.
            if (tunnel.TryGetProperty("config", out var config) &&
                config.ValueKind == JsonValueKind.Object &&
                config.TryGetProperty("addr", out var addrProp))
            {
                string? addr = addrProp.GetString();
                if (addr != null && addr.Contains($":{localPort}"))
                {
                    url = parsed;
                    return true;
                }
                continue;
            }

            fallback ??= parsed;
        }

        url = fallback;
        return url != null;
    }
}
