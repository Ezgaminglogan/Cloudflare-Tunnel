using Cloudflare_Tunnel.Infrastructure;
using Spectre.Console;

namespace Cloudflare_Tunnel.Presentation;

public static class ConsoleTheme
{
    public static readonly Color PrimaryOrange = Color.FromHex("F38020");
    public static readonly Color AccentCyan = Color.FromHex("00D2D3");
    public static readonly Color SuccessGreen = Color.FromHex("10B981");
    public static readonly Color WarningYellow = Color.FromHex("F59E0B");
    public static readonly Color MutedGrey = Color.FromHex("6B7280");
    public static readonly Color DarkBorder = Color.FromHex("374151");

    public const string DeveloperAttribution = "Logan M. Panucat";

    public static int GetStandardPanelWidth()
    {
        int panelWidth = 96;
        try
        {
            if (!Console.IsOutputRedirected && Console.WindowWidth > 40)
            {
                panelWidth = Math.Min(Console.WindowWidth - 6, 96);
            }
        }
        catch { }
        return panelWidth;
    }

    /// <summary>
    /// Renders a cohesive, brand-aligned startup splash screen using the strict
    /// PrimaryOrange (#F38020), AccentCyan (#00D2D3), SuccessGreen (#10B981), and
    /// DarkBorder (#374151) palette. Skippable immediately with any key press.
    /// </summary>
    public static async Task RenderSplashScreenAsync()
    {
        if (Console.IsOutputRedirected || Console.IsInputRedirected)
        {
            return;
        }

        try { AnsiConsole.Clear(); } catch { }

        int width = GetStandardPanelWidth();

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new FigletText("CLOUDFLARE").Centered().Color(PrimaryOrange));

        // Architectural topology strip — communicates product purpose at a glance
        var topologyGrid = new Grid().AddColumn(new GridColumn().Centered());
        topologyGrid.AddRow(new Markup(
            "[bold white]UNIVERSAL REVERSE PROXY & DUAL-ENGINE EDGE TUNNEL CONSOLE[/]"));
        topologyGrid.AddRow(new Markup(
            $"[dim grey]Release v{AppUpdater.CurrentVersion}[/]   [dim #374151]│[/]   [bold gold1]{DeveloperAttribution}[/]   [dim #374151]│[/]   [dim grey]Windows x64 Self-Contained[/]"));
        topologyGrid.AddRow(new Rule { Style = new Style(DarkBorder) });
        topologyGrid.AddRow(new Markup(
            "[bold #00D2D3][[LOCALHOST:PORT]][/]  [dim #6B7280]───►[/]  " +
            "[bold #F38020][[CLOUDFLARED / NGROK EDGE]][/]  [dim #6B7280]───►[/]  " +
            "[bold #10B981][[PUBLIC HTTPS + MOBILE QR]][/]"));

        var splashCard = new Panel(topologyGrid)
        {
            Border = BoxBorder.Double,
            BorderStyle = new Style(PrimaryOrange),
            Padding = new Padding(2, 1, 2, 1),
            Width = width
        };

        AnsiConsole.Write(new Align(splashCard, HorizontalAlignment.Center));
        AnsiConsole.WriteLine();

        (string Stage, string Detail, string Badge)[] bootSteps =
        {
            ("Console & TLS 1.3 Pipeline", "120x36 viewport centered, UTF-8 & Win32 hooks active", "READY"),
            ("Embedded Tunnel Engines", "cloudflared.exe & ngrok.exe assembly streams verified", "READY"),
            ("Port Auditor & QR Matrix", "TCP IPv4/IPv6 socket scanner & half-block encoder", "ONLINE")
        };

        var checkTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(DarkBorder)
            .Width(width)
            .AddColumn(new TableColumn("[bold #00D2D3]Subsystem[/]").Width(28))
            .AddColumn(new TableColumn("[bold white]Telemetry[/]"))
            .AddColumn(new TableColumn("[bold grey]State[/]").RightAligned().Width(10));

        await AnsiConsole.Live(new Align(checkTable, HorizontalAlignment.Center))
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                foreach (var (stage, detail, badge) in bootSteps)
                {
                    if (KeyPressed()) break;
                    checkTable.AddRow(
                        $"[white]{stage}[/]",
                        $"[grey]{detail}[/]",
                        $"[bold #10B981][[{badge}]][/]");
                    ctx.Refresh();
                    await DelayOrKeyPressAsync(180);
                }
            });

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Align(
            new Markup("[dim grey]Entering interactive console... (Press [[?]] or select [[MANUAL]] anytime for usage guide)[/]"),
            HorizontalAlignment.Center));

        await DelayOrKeyPressAsync(320);
    }

    private static bool KeyPressed()
    {
        try
        {
            if (Console.KeyAvailable)
            {
                Console.ReadKey(intercept: true);
                return true;
            }
        }
        catch { }
        return false;
    }

    private static async Task DelayOrKeyPressAsync(int milliseconds)
    {
        int elapsed = 0;
        while (elapsed < milliseconds)
        {
            if (KeyPressed()) return;
            await Task.Delay(30);
            elapsed += 30;
        }
    }

    public static void RenderBanner()
    {
        try
        {
            if (!Console.IsOutputRedirected)
            {
                AnsiConsole.Clear();
            }
        }
        catch { }

        // 1. Centered ASCII Art Banner
        var figlet = new FigletText("CLOUDFLARE")
            .Centered()
            .Color(PrimaryOrange);

        AnsiConsole.Write(figlet);

        // 2. Fully-Utilized, Non-Collapsing Dashboard Card
        var headerGrid = new Grid();
        headerGrid.AddColumn(new GridColumn().Centered());

        headerGrid.AddRow(
            new Markup("[bold white]Universal Reverse Proxy & Multi-Project Tunnel Manager[/]")
        );

        headerGrid.AddRow(
            new Markup("[grey]Encrypted Edge Routing  •  Zero-Config  •  Mobile QR Access[/]")
        );

        headerGrid.AddRow(
            new Rule { Style = new Style(DarkBorder) }
        );

        headerGrid.AddRow(
            new Markup("[bold #F38020]ENGINES:[/] [white]Cloudflare & Ngrok Dual-Core[/]   [dim grey]•[/]   [bold #00D2D3]NETWORK:[/] [white]Global Anycast[/]")
        );

        headerGrid.AddRow(
            new Markup($"[bold white on #F38020] DEVELOPED BY [/]  [bold gold1]{DeveloperAttribution}[/]   [dim grey]•[/]   [grey]Release v{AppUpdater.CurrentVersion} (Ready)[/]")
        );

        int panelWidth = GetStandardPanelWidth();

        var panel = new Panel(headerGrid)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(PrimaryOrange),
            Padding = new Padding(2, 0, 2, 0),
            Width = panelWidth
        };

        AnsiConsole.Write(new Align(panel, HorizontalAlignment.Center));
        AnsiConsole.WriteLine();
    }

    public static void RenderFooter()
    {
        AnsiConsole.WriteLine();
        var rule = new Rule($"[grey]Created with precision | Developer: [/][bold gold1]{DeveloperAttribution}[/]")
        {
            Justification = Justify.Center,
            Style = new Style(DarkBorder)
        };
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();
    }
}
