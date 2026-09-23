namespace Cloudflare_Tunnel.Core;

public interface IPortScanner
{
    IReadOnlyList<ListeningPortInfo> GetActiveListeners(int minPort = 1000, int maxPort = 65000);
    Task<bool> IsPortRespondingAsync(int port, TimeSpan timeout);
}
