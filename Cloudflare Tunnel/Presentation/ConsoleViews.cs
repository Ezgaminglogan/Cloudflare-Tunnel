using System.Diagnostics;
using Cloudflare_Tunnel.Core;
using Spectre.Console;
using TextCopy;

namespace Cloudflare_Tunnel.Presentation;

public static class ConsoleViews
{
    private static readonly List<PortPreset> Presets = new()
    {
        new("Next.js / React", 3000, "Frontend SPA / SSR", "Standard Node/Next Dev Server"),
        new("Vite / Vue / Svelte", 5173, "Modern Frontend", "Fast HMR Dev Server"),
        new("ASP.NET Core HTTP", 5000, "Backend / API", "Default Kestrel Local HTTP"),
        new("FastAPI / Django", 8000, "Python Web / API", "Uvicorn / Gunicorn / RunServer")
    };

    public static TunnelProviderType PromptProvider()
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("  [bold white]Choose your Tunnel Provider:[/]\n  [grey]Select tunneling infrastructure to route traffic:[/]")
                .PageSize(5)
                .HighlightStyle(new Style(Color.FromHex("F38020"), decoration: Decoration.Bold))
                .AddChoices(new[]
                {
                    "  [bold orange3][[CF]][/] Cloudflare Tunnel (Embedded binary, 100% Free, Zero configuration)",
                    "  [bold cyan][[NGROK]][/] Ngrok Tunnel (Embedded binary, Authtoken required)"
                })
        );

        return choice.Contains("[CF]") ? TunnelProviderType.Cloudflare : TunnelProviderType.Ngrok;
    }

    public static async Task<int?> PromptPortSelectionAsync(IPortScanner scanner)
    {
        var menuChoices = new List<string>();

        foreach (var p in Presets)
        {
            menuChoices.Add($"  [grey]>[/] [bold white]{p.Name}[/] [grey](Port {p.Port})[/] - [dim]{p.Description}[/]");
        }

        menuChoices.Add("  [cyan][[SCAN]][/] Scan active listening ports on this PC");
        menuChoices.Add("  [yellow][[CUSTOM]][/] Enter custom port number");
        menuChoices.Add("  [red][[EXIT]][/] Exit");

        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("  [bold white]Select a project or local port to tunnel:[/]")
                .PageSize(10)
                .HighlightStyle(new Style(Color.FromHex("F38020"), decoration: Decoration.Bold))
                .AddChoices(menuChoices)
        );

        if (selection.Contains("[EXIT]"))
        {
            return null;
        }

        if (selection.Contains("[SCAN]"))
        {
            return await ScanAndSelectPortAsync(scanner);
        }

        if (selection.Contains("[CUSTOM]"))
        {
            return PromptCustomPort();
        }

        // Match selected preset
        var match = Presets.FirstOrDefault(p => selection.Contains($"Port {p.Port}"));
        return match?.Port ?? 3000;
    }

    public static async Task<int?> ScanAndSelectPortAsync(IPortScanner scanner)
    {
        AnsiConsole.MarkupLine("\n[cyan]Auditing active network sockets...[/]");

        IReadOnlyList<ListeningPortInfo> activePorts = Array.Empty<ListeningPortInfo>();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync("Detecting running local servers...", async _ =>
            {
                await Task.Delay(200); // brief moment for socket enumeration
                activePorts = scanner.GetActiveListeners(1000, 65000);
            });

        if (activePorts.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow][[!]][/] No active listening TCP ports detected on this PC.");
            return PromptCustomPort();
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.FromHex("374151"))
            .AddColumn(new TableColumn("[bold cyan]Port[/]").Centered())
            .AddColumn(new TableColumn("[bold white]Bound Address[/]"))
            .AddColumn(new TableColumn("[bold green]Protocol[/]"))
            .AddColumn(new TableColumn("[bold grey]Identified Service[/]"));

        foreach (var port in activePorts)
        {
            table.AddRow(
                $"[bold gold1]{port.Port}[/]",
                $"[white]{port.HostAddress}[/]",
                $"[green]{port.Protocol}[/]",
                $"[grey]{port.Description}[/]"
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        var choices = activePorts.Select(p => $"Port {p.Port} - {p.Description} ({p.HostAddress})").ToList();
        choices.Add("[[<]] Back to Main Menu");

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold white]Select detected port to expose:[/]")
                .HighlightStyle(new Style(Color.FromHex("F38020"), decoration: Decoration.Bold))
                .AddChoices(choices)
        );

        if (selected.Contains("Back to Main Menu"))
        {
            return await PromptPortSelectionAsync(scanner);
        }

        string portStr = selected.Split(' ')[1];
        return int.TryParse(portStr, out int portNum) ? portNum : 5000;
    }

    public static int PromptCustomPort()
    {
        return AnsiConsole.Prompt(
            new TextPrompt<int>("[bold white]Enter local port number (1-65535):[/]")
                .PromptStyle("gold1")
                .ValidationErrorMessage("[red]Please enter a valid port between 1 and 65535.[/]")
                .Validate(port => port is >= 1 and <= 65535)
        );
    }

    public static async Task<string> EnsureBinaryWithRealProgressAsync(ITunnelProvider provider)
    {
        string binaryPath = "";

        await AnsiConsole.Progress()
            .AutoRefresh(true)
            .AutoClear(false)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn() { CompletedStyle = new Style(Color.FromHex("10B981")) },
                new PercentageColumn(),
                new DownloadedColumn(),
                new TransferSpeedColumn(),
                new SpinnerColumn(Spinner.Known.Dots)
            })
            .StartAsync(async ctx =>
            {
                ProgressTask? task = null;

                var progressHandler = new Progress<(long processed, long total, double speed)>(p =>
                {
                    if (task == null && p.total > 0)
                    {
                        string action = provider.Type == TunnelProviderType.Cloudflare
                            ? "Extracting embedded Cloudflare engine"
                            : "Extracting embedded Ngrok engine";

                        task = ctx.AddTask($"[bold white]{action}[/]", maxValue: p.total);
                    }

                    if (task != null)
                    {
                        task.Value = p.processed;
                    }
                });

                binaryPath = await provider.EnsureBinaryAvailableAsync(progressHandler);
                if (task != null) task.Value = task.MaxValue;
            });

        return binaryPath;
    }

    public static async Task RunActiveTunnelDashboardAsync(ITunnelSession session, TunnelOptions options, IPortScanner scanner)
    {
        AnsiConsole.WriteLine();

        // 1. Check if local target port is actually reachable
        bool isResponding = await scanner.IsPortRespondingAsync(options.LocalPort, TimeSpan.FromMilliseconds(800));
        if (!isResponding)
        {
            AnsiConsole.MarkupLine($"[yellow][[!]][/] Note: No active HTTP service detected at http://localhost:{options.LocalPort} yet.");
            AnsiConsole.MarkupLine("[dim]The tunnel will remain ready, but requests will return 502 until your local server starts.[/]\n");
        }
        else
        {
            AnsiConsole.MarkupLine($"[green][[OK]][/] Verified active local service listening on port {options.LocalPort}\n");
        }

        // 2. Wait for Public URL assignment with status spinner
        var startupLogs = new List<string>();
        Action<string> logCapture = line =>
        {
            lock (startupLogs)
            {
                if (startupLogs.Count > 10) startupLogs.RemoveAt(0);
                startupLogs.Add(line);
            }
        };
        session.OutputReceived += logCapture;

        Uri? publicUrl = session.PublicUrl;
        if (publicUrl == null)
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("gold1"))
                .StartAsync("Registering secure tunnel endpoint with edge network...", async ctx =>
                {
                    var sw = Stopwatch.StartNew();
                    while (session.PublicUrl == null && session.IsRunning && sw.Elapsed < TimeSpan.FromSeconds(30))
                    {
                        await Task.Delay(200);
                    }
                    publicUrl = session.PublicUrl;
                });
        }

        if (publicUrl == null)
        {
            AnsiConsole.MarkupLine("[red][[ERROR]][/] Failed to retrieve public tunnel URL. Tunnel process terminated early.");
            lock (startupLogs)
            {
                if (startupLogs.Count > 0)
                {
                    AnsiConsole.MarkupLine("\n[bold yellow]Engine Output Diagnostics:[/]");
                    foreach (var line in startupLogs)
                    {
                        AnsiConsole.MarkupLine($"[dim grey]>[/] [yellow]{Markup.Escape(line)}[/]");
                    }
                }
            }
            AnsiConsole.MarkupLine("\n[grey]Press any key to return to main menu...[/]");
            try
            {
                if (!Console.IsInputRedirected)
                {
                    Console.ReadKey(true);
                }
            }
            catch { }
            return;
        }

        session.OutputReceived -= logCapture;

        // 3. Auto-copy to clipboard
        bool copied = false;
        try
        {
            await ClipboardService.SetTextAsync(publicUrl.ToString());
            copied = true;
        }
        catch { }

        // 4. Render Live Connected Dashboard
        try
        {
            if (!Console.IsOutputRedirected)
            {
                AnsiConsole.Clear();
            }
        }
        catch { }
        ConsoleTheme.RenderBanner();

        var infoTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.FromHex("F38020"))
            .AddColumn(new TableColumn("[bold white]Property[/]").Width(14))
            .AddColumn(new TableColumn("[bold white]Details[/]").NoWrap());

        infoTable.AddRow("[bold cyan]Provider[/]", $"[bold white]{session.Provider}[/]");
        infoTable.AddRow("[bold cyan]Local Target[/]", $"[bold white]http://{options.Hostname}:{options.LocalPort}[/]");
        infoTable.AddRow("[bold cyan]Public URL[/]", $"[bold green on black] {publicUrl} [/]");
        infoTable.AddRow("[bold cyan]Clipboard[/]", copied ? "[bold green][[OK]] Auto-copied to clipboard![/]" : "[grey]Copy manually from above[/]");
        infoTable.AddRow("[bold cyan]Status[/]", "[bold black on green] ONLINE & ROUTING [/]");
        infoTable.AddRow("[bold cyan]Protocol[/]", "[dim]HTTPS / TLS 1.3 Edge Proxy[/]");
        infoTable.AddRow("[bold cyan]Traffic Mode[/]", "[dim]Zero-Trust Full Duplex Stream[/]");
        infoTable.AddRow("[bold cyan]Developer[/]", $"[bold gold1]{ConsoleTheme.DeveloperAttribution}[/]");

        var qrPanel = QrCodeRenderer.RenderQrCode(publicUrl.ToString(), "[[QR]] MOBILE ACCESS");

        var dashboardLayout = new Grid();
        dashboardLayout.AddColumn(new GridColumn().PadRight(2));
        dashboardLayout.AddColumn(new GridColumn().NoWrap());
        dashboardLayout.AddRow(infoTable, qrPanel);

        AnsiConsole.Write(new Panel(dashboardLayout)
        {
            Header = new PanelHeader("[bold green][[ONLINE]] TUNNEL ACTIVE[/]", Justify.Left),
            Border = BoxBorder.Double,
            BorderStyle = new Style(Color.FromHex("10B981")),
            Padding = new Padding(1, 1, 1, 1)
        });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold white]Instructions:[/] Share the Public URL or scan the QR code to open your local project in any web browser.");
        AnsiConsole.MarkupLine("[bold red]Press [[Q]] or [[Ctrl+C]] to disconnect tunnel and return to menu.[/]\n");

        // 5. Real-time stream of traffic logs or wait for exit
        var logRule = new Rule("[grey]Edge Traffic Log[/]") { Justification = Justify.Left, Style = new Style(Color.FromHex("374151")) };
        AnsiConsole.Write(logRule);

        Action<string> trafficLogger = line =>
        {
            // Filter noise, format HTTP requests nicely
            if (line.Contains("HTTP", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("connIndex", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("GET", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("POST", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine($"[dim grey]{DateTime.Now:HH:mm:ss}[/] [grey]{Markup.Escape(line)}[/]");
            }
        };

        session.OutputReceived += trafficLogger;

        using var dashboardCts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true; // Prevent app exit, cleanly return to menu
            dashboardCts.Cancel();
        };

        Console.CancelKeyPress += cancelHandler;

        try
        {
            // Keyboard & cancellation listener loop
            while (session.IsRunning && !dashboardCts.IsCancellationRequested)
            {
                try
                {
                    if (!Console.IsInputRedirected && Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key is ConsoleKey.Q or ConsoleKey.Escape)
                        {
                            AnsiConsole.MarkupLine("\n[yellow]Stopping tunnel...[/]");
                            dashboardCts.Cancel();
                            break;
                        }
                    }
                }
                catch { }

                await Task.Delay(100);
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            session.OutputReceived -= trafficLogger;
        }

        AnsiConsole.MarkupLine("[green][[OK]] Tunnel stopped cleanly. Returning to menu...[/]");
        await session.StopAsync();
        await Task.Delay(500);
    }
}
