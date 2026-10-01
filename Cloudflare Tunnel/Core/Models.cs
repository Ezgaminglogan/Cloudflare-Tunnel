namespace Cloudflare_Tunnel.Core;

public record TunnelOptions(
    int LocalPort,
    string Hostname = "localhost",
    string? CustomSubdomain = null,
    string? AuthToken = null
);

public record ListeningPortInfo(
    int Port,
    string HostAddress,
    string Protocol,
    string Description = "",
    int? ProcessId = null,
    string ProcessName = ""
);

public record PortPreset(
    string Name,
    int Port,
    string Description
);
