namespace Cloudflare_Tunnel.Core;

public interface ITunnelSession : IAsyncDisposable
{
    TunnelProviderType Provider { get; }
    int LocalPort { get; }
    Uri? PublicUrl { get; }
    TunnelStatus Status { get; }
    bool IsRunning { get; }

    event Action<string>? OutputReceived;
    event Action<Uri>? PublicUrlAssigned;
    event Action<TunnelStatus>? StatusChanged;

    Task WaitForExitAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
