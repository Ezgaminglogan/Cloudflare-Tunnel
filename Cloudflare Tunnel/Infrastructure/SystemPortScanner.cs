using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Cloudflare_Tunnel.Core;

namespace Cloudflare_Tunnel.Infrastructure;

public class SystemPortScanner : IPortScanner
{
    private const int AF_INET = 2;
    private const int AF_INET6 = 23;
    private const int TcpTableOwnerPidListener = 3;
    private const int MibTcpStateListen = 2;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref uint pdwSize, bool bOrder, uint ulAf, int tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint State;
        public uint LocalPort;
        public uint OwningPid;
    }

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
    /// Enumerates IPv4 + IPv6 TCP listeners with their owning PIDs via the
    /// Win32 GetExtendedTcpTable API (equivalent to `netstat -ano`).
    /// </summary>
    private static List<ListenerOwner> GetWindowsListenerOwners()
    {
        var owners = new List<ListenerOwner>();
        ReadTcpTable(AF_INET, isV6: false, owners);
        ReadTcpTable(AF_INET6, isV6: true, owners);
        return owners;
    }

    private static void ReadTcpTable(uint addressFamily, bool isV6, List<ListenerOwner> results)
    {
        uint bufferSize = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, addressFamily, TcpTableOwnerPidListener, 0);
        if (bufferSize == 0) return;

        IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            if (GetExtendedTcpTable(buffer, ref bufferSize, true, addressFamily, TcpTableOwnerPidListener, 0) != 0)
            {
                return;
            }

            uint rowCount = (uint)Marshal.ReadInt32(buffer);
            int rowSize = isV6 ? Marshal.SizeOf<MibTcp6RowOwnerPid>() : Marshal.SizeOf<MibTcpRowOwnerPid>();
            IntPtr rowPtr = IntPtr.Add(buffer, sizeof(uint)); // rows follow dwNumEntries

            for (uint i = 0; i < rowCount; i++, rowPtr = IntPtr.Add(rowPtr, rowSize))
            {
                uint state;
                uint rawPort;
                int pid;
                string address;

                if (isV6)
                {
                    var row = Marshal.PtrToStructure<MibTcp6RowOwnerPid>(rowPtr);
                    state = row.State;
                    rawPort = row.LocalPort;
                    pid = (int)row.OwningPid;
                    address = row.LocalAddr != null ? new IPAddress(row.LocalAddr).ToString() : "::";
                }
                else
                {
                    var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);
                    state = row.State;
                    rawPort = row.LocalPort;
                    pid = (int)row.OwningPid;
                    address = new IPAddress(BitConverter.GetBytes(row.LocalAddr)).ToString();
                }

                if (state != MibTcpStateListen) continue;

                // Port DWORD holds the port in network byte order in its low 16 bits.
                int port = ((int)rawPort & 0xFF) << 8 | ((int)rawPort >> 8 & 0xFF);
                results.Add(new ListenerOwner(port, address, pid));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
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
        8000 => "FastAPI / Django / PHP",
        8080 => "Java Spring / Tomcat / HTTP Alternate",
        8081 or 8888 => "Web Application Alternate",
        9000 => "PHP-FPM / SonarQube",
        _ => "Local Web Service"
    };
}
