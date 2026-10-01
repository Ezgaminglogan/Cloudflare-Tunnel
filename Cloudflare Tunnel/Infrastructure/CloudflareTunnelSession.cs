using System.Diagnostics;
using Cloudflare_Tunnel.Core;

namespace Cloudflare_Tunnel.Infrastructure;

public sealed class CloudflareTunnelSession : ProcessTunnelSession
{
    private readonly CloudflareUrlDetector _urlDetector = new();
    private readonly object _detectLock = new();

    public override TunnelProviderType Provider => TunnelProviderType.Cloudflare;

    public CloudflareTunnelSession(string cloudflaredPath, TunnelOptions options)
        : base(BuildStartInfo(cloudflaredPath, options), options)
    {
    }

    private static ProcessStartInfo BuildStartInfo(string cloudflaredPath, TunnelOptions options) => new()
    {
        FileName = cloudflaredPath,
        Arguments = $"tunnel --url http://{options.Hostname}:{options.LocalPort} --no-autoupdate",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        WorkingDirectory = Path.GetDirectoryName(cloudflaredPath) ?? AppContext.BaseDirectory
    };

    protected override void OnOutputLine(string line)
    {
        lock (_detectLock)
        {
            if (PublicUrl == null && _urlDetector.TryDetect(line, out var uri))
            {
                SetPublicUrl(uri);
            }
        }
    }
}
