using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Cloudflare_Tunnel.Core;

namespace Cloudflare_Tunnel.Infrastructure;

public class SystemPortScanner : IPortScanner
{
    public IReadOnlyList<ListeningPortInfo> GetActiveListeners(int minPort = 1000, int maxPort = 65000)
    {
        var results = new List<ListeningPortInfo>();
        try
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpEndPoints = properties.GetActiveTcpListeners();

            var distinctPorts = tcpEndPoints
                .Where(ep => ep.Port >= minPort && ep.Port <= maxPort)
                .GroupBy(ep => ep.Port)
                .Select(g => g.First())
                .OrderBy(ep => ep.Port);

            foreach (var ep in distinctPorts)
            {
                string desc = GetKnownPortDescription(ep.Port);
                results.Add(new ListeningPortInfo(
                    ep.Port,
                    ep.Address.ToString(),
                    "TCP",
                    desc
                ));
            }
        }
        catch
        {
            // Graceful fallback if permission or socket enumeration fails
        }

        return results;
    }

    public async Task<bool> IsPortRespondingAsync(int port, TimeSpan timeout)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeout);
            await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static string GetKnownPortDescription(int port) => port switch
    {
        3000 => "React / Next.js / Node.js",
        3001 => "Node.js / React alternate",
        4200 => "Angular CLI",
        5000 => "ASP.NET Core HTTP / Flask",
        5001 => "ASP.NET Core HTTPS",
        5173 => "Vite / Vue / Svelte",
        7000 or 7001 or 7100 => "ASP.NET Core Kestrel",
        8000 => "FastAPI / Django / PHP",
        8080 => "Java Spring / Tomcat / HTTP Alternate",
        8081 or 8888 => "Web Application Alternate",
        9000 => "PHP-FPM / SonarQube",
        _ => "Local Web Service"
    };
}
