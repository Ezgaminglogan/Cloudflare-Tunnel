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

    public static void RenderBanner()
    {
        try
        {
            if (!Console.IsOutputRedirected)
            {
                Console.Clear();
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
            new Markup($"[bold white on #F38020] DEVELOPED BY [/]  [bold gold1]{DeveloperAttribution}[/]   [dim grey]•[/]   [grey]Release v2.0.0 (Ready)[/]")
        );

        int panelWidth = 96;
        try
        {
            if (!Console.IsOutputRedirected && Console.WindowWidth > 40)
            {
                panelWidth = Math.Min(Console.WindowWidth - 6, 96);
            }
        }
        catch { }

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
