namespace Cloudflare_Tunnel.Core;

public enum TunnelProviderType
{
    Cloudflare,
    Ngrok
}

public enum TunnelStatus
{
    Stopped,
    Starting,
    Online,
    Faulted
}
