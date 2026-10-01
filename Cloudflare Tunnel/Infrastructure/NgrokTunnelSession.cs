using System.Diagnostics;
using System.Text.Json;
using Cloudflare_Tunnel.Core;

namespace Cloudflare_Tunnel.Infrastructure;

public sealed class NgrokTunnelSession : ProcessTunnelSession
{
    public override TunnelProviderType Provider => TunnelProviderType.Ngrok;

    public NgrokTunnelSession(string ngrokPath, TunnelOptions options)
        : base(BuildStartInfo(ngrokPath, options), options)
    {
    }

    private static ProcessStartInfo BuildStartInfo(string ngrokPath, TunnelOptions options)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ngrokPath,
            Arguments = BuildArguments(options),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(ngrokPath) ?? AppContext.BaseDirectory
        };
        return startInfo;
    }

    internal static string BuildArguments(TunnelOptions options)
    {
        string args = $"http {options.Hostname}:{options.LocalPort} --log=stdout";

        if (!string.IsNullOrWhiteSpace(options.CustomSubdomain))
        {
            string domain = options.CustomSubdomain.Trim().TrimEnd('/');
            if (!domain.Contains("://", StringComparison.Ordinal))
            {
                domain = "https://" + domain;
            }
            args += $" --url={domain}";
        }

        return args;
    }

    protected override void OnStarted()
    {
        // Poll the local ngrok API as a fallback for stdout parsing. Must run
        // after Start(): HasExited throws while the process is unstarted.
        _ = PollNgrokApiAsync();
    }

    protected override void OnOutputLine(string line)
    {
        if (NgrokUrlParser.TryDetect(line, out var uri))
        {
            SetPublicUrl(uri);
        }
    }

    private async Task PollNgrokApiAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(35);

        while (PublicUrl == null && !IsDisposed && DateTime.UtcNow < deadline)
        {
            await Task.Delay(500);
            if (!ProcessIsRunning()) return;

            try
            {
                string json = await client.GetStringAsync("http://127.0.0.1:4040/api/tunnels");
                using var doc = JsonDocument.Parse(json);
                if (NgrokUrlParser.TrySelectTunnel(doc.RootElement, LocalPort, out var parsed))
                {
                    SetPublicUrl(parsed);
                    return;
                }
            }
            catch
            {
                // API not ready yet, continue polling
            }
        }
    }
}
