using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Cloudflare_Tunnel.Core;

namespace Cloudflare_Tunnel.Infrastructure;

public class SystemPortScanner : IPortScanner
{
    private sealed record ListenerOwner(int Port, string Address, int Pid);

    public IReadOnlyList<ListeningPortInfo> GetActiveListeners(int minPort = 1000, int maxPort = 65000)
    {
        try
        {
            var listeners = OperatingSystem.IsWindows()
                ? GetWindowsListenerOwners()
                : GetListenersFromIpGlobalProperties();

            var results = new List<ListeningPortInfo>();
            foreach (var l in listeners
                .Where(l => l.Port >= minPort && l.Port <= maxPort)
                .GroupBy(l => l.Port)
                .Select(g => g.First())
                .OrderBy(l => l.Port))
            {
                string processName = "";
                if (l.Pid > 0)
                {
                    try
                    {
                        processName = Process.GetProcessById(l.Pid).ProcessName;
                    }
                    catch
                    {
                        // Process exited between table snapshot and lookup
                    }
                }

                results.Add(new ListeningPortInfo(
                    l.Port,
                    l.Address,
                    "TCP",
                    GetKnownPortDescription(l.Port),
                    l.Pid > 0 ? l.Pid : null,
                    processName
                ));
            }

            return results;
        }
        catch
        {
            // Graceful fallback if permission or socket enumeration fails
            return Array.Empty<ListeningPortInfo>();
        }
    }

    /// <summary>
    /// Enumerates TCP listeners with owning PIDs by parsing `netstat -ano -p tcp`
    /// (the OS's own output — no fragile struct layouts). Sample row:
    /// "  TCP    0.0.0.0:49664    0.0.0.0:0    LISTENING    920"
    /// </summary>
    private static List<ListenerOwner> GetWindowsListenerOwners()
    {
        var owners = new List<ListenerOwner>();

        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "netstat",
            Arguments = "-ano -p tcp",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        if (proc == null) return owners;

        string output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(5000);

        foreach (var line in output.Split('\n'))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || parts[0] != "TCP" || parts[3] != "LISTENING")
            {
                continue;
            }

            // Local endpoint is addr:port (IPv6 addrs arrive bracketed: [::]:443)
            string local = parts[1];
            int sep = local.LastIndexOf(':');
            if (sep < 0 || !int.TryParse(local[(sep + 1)..], out int port))
            {
                continue;
            }

            if (!int.TryParse(parts[4], out int pid))
            {
                continue;
            }

            owners.Add(new ListenerOwner(port, local[..sep].Trim('[', ']'), pid));
        }

        return owners;
    }

    private static List<ListenerOwner> GetListenersFromIpGlobalProperties()
    {
        return IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Select(ep => new ListenerOwner(ep.Port, ep.Address.ToString(), 0))
            .ToList();
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
        8000 => "FastAPI / Django / Laravel serve",
        8080 => "Java Spring / Tomcat / HTTP Alternate",
        8081 or 8888 => "Web Application Alternate",
        9000 => "PHP-FPM / SonarQube",
        3306 => "MySQL / MariaDB",
        _ => "Local Web Service"
    };
}
