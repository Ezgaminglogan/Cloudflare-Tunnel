using Cloudflare_Tunnel.Core;

namespace Cloudflare_Tunnel.Infrastructure;

public class CloudflareTunnelProvider : ITunnelProvider
{
    private readonly IBinaryManager _binaryManager;
    private string? _cachedBinaryPath;

    public TunnelProviderType Type => TunnelProviderType.Cloudflare;
    public string DisplayName => "Cloudflare Tunnel (Quick Tunnel)";
    public string Description => "100% Free, Zero-Configuration, No account/token required. Generates trycloudflare.com URL.";
    public bool RequiresAuthToken => false;

    public CloudflareTunnelProvider(IBinaryManager binaryManager)
    {
        _binaryManager = binaryManager;
    }

    public async Task<string> EnsureBinaryAvailableAsync(
        IProgress<(long bytesProcessed, long totalBytes, double speedBytesPerSec)>? progress = null,
        CancellationToken ct = default)
    {
        if (_cachedBinaryPath != null && File.Exists(_cachedBinaryPath))
        {
            return _cachedBinaryPath;
        }

        // 1. Check if already extracted in %LOCALAPPDATA%\UniversalTunnel\bin
        string? existing = _binaryManager.FindExistingExecutable("cloudflared");
        if (existing != null)
        {
            _cachedBinaryPath = existing;
            progress?.Report((1, 1, 0));
            return existing;
        }

        // 2. Extract from Embedded Resource
        try
        {
            string extracted = await _binaryManager.ExtractEmbeddedBinaryAsync(
                "cloudflare.cloudflared.exe",
                "cloudflared.exe",
                progress,
                ct
            );
            _cachedBinaryPath = extracted;
            return extracted;
        }
        catch (FileNotFoundException)
        {
            // 3. Fallback: Download from official Cloudflare Releases if embedded resource was omitted
            var downloadUrl = new Uri("https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe");
            string downloaded = await _binaryManager.DownloadBinaryAsync(
                downloadUrl,
                "cloudflared.exe",
                progress,
                ct
            );
            _cachedBinaryPath = downloaded;
            return downloaded;
        }
    }

    public async Task<ITunnelSession> StartTunnelAsync(TunnelOptions options, CancellationToken ct = default)
    {
        string binaryPath = await EnsureBinaryAvailableAsync(ct: ct);
        var session = new CloudflareTunnelSession(binaryPath, options);
        session.Start();
        return session;
    }
}
