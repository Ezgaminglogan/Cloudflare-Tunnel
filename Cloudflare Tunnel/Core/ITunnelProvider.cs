namespace Cloudflare_Tunnel.Core;

public interface ITunnelProvider
{
    TunnelProviderType Type { get; }
    string DisplayName { get; }
    string Description { get; }
    bool RequiresAuthToken { get; }

    Task<string> EnsureBinaryAvailableAsync(IProgress<(long bytesProcessed, long totalBytes, double speedBytesPerSec)>? progress = null, CancellationToken ct = default);
    Task<ITunnelSession> StartTunnelAsync(TunnelOptions options, CancellationToken ct = default);
}
