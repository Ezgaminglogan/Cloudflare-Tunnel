using System.Collections.Concurrent;
using System.Diagnostics;
using Cloudflare_Tunnel.Core;
using Spectre.Console;
using Spectre.Console.Rendering;
using TextCopy;

namespace Cloudflare_Tunnel.Presentation;

public static class ConsoleViews
{
    private static readonly List<PortPreset> Presets = new()
    {
        new("Next.js / React", 3000, "Standard Node/Next Dev Server"),
        new("Vite / Vue / Svelte", 5173, "Fast HMR Dev Server"),
        new("ASP.NET Core HTTP", 5000, "Default Kestrel Local HTTP"),
        new("FastAPI / Django", 8000, "Uvicorn / Gunicorn / RunServer")
    };

    private enum PortAction { Select, Scan, Custom, Exit, Back }

    private sealed record PortMenuItem(PortAction Action, int Port, string Label);
    private sealed record ProviderMenuItem(TunnelProviderType? Provider, string Label);

    private static readonly Style Highlight = new(Color.FromHex("F38020"), decoration: Decoration.Bold);

    public static TunnelProviderType? PromptProvider()
    {
        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<ProviderMenuItem>()
                .Title("  [bold white]Choose your Tunnel Provider:[/]\n  [grey]Select tunneling infrastructure to route traffic:[/]")
                .PageSize(6)
                .HighlightStyle(Highlight)
                .UseConverter(item => item.Label)
                .AddChoices(
                    new ProviderMenuItem(TunnelProviderType.Cloudflare, "  [bold orange3][[CF]][/] Cloudflare Tunnel (Embedded binary, 100% Free, Zero configuration)"),
                    new ProviderMenuItem(TunnelProviderType.Ngrok, "  [bold cyan][[NGROK]][/] Ngrok Tunnel (Embedded binary, Authtoken required)"),
                    new ProviderMenuItem(null, "  [grey][[<]][/] Back to port selection"))
        );

        return selection.Provider;
    }

    public static async Task<int?> PromptPortSelectionAsync(IPortScanner scanner)
    {
        var menuChoices = Presets
            .Select(p => new PortMenuItem(
                PortAction.Select,
                p.Port,
                $"  [grey]>[/] [bold white]{p.Name}[/] [grey](Port {p.Port})[/] - [dim]{p.Description}[/]"))
            .ToList();

        menuChoices.Add(new PortMenuItem(PortAction.Scan, 0, "  [cyan][[SCAN]][/] Scan active listening ports on this PC"));
        menuChoices.Add(new PortMenuItem(PortAction.Custom, 0, "  [yellow][[CUSTOM]][/] Enter custom port number"));
        menuChoices.Add(new PortMenuItem(PortAction.Exit, 0, "  [red][[EXIT]][/] Exit"));

        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<PortMenuItem>()
                .Title("  [bold white]Select a project or local port to tunnel:[/]")
                .PageSize(10)
                .HighlightStyle(Highlight)
                .UseConverter(item => item.Label)
                .AddChoices(menuChoices)
        );

        return selection.Action switch
        {
            PortAction.Exit => null,
            PortAction.Scan => await ScanAndSelectPortAsync(scanner),
            PortAction.Custom => PromptCustomPort(),
            _ => selection.Port
        };
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
            .AddColumn(new TableColumn("[bold white]Process[/]"))
            .AddColumn(new TableColumn("[bold white]Bound Address[/]"))
            .AddColumn(new TableColumn("[bold green]Protocol[/]"))
            .AddColumn(new TableColumn("[bold grey]Identified Service[/]"));

        foreach (var port in activePorts)
        {
            string processCell = !string.IsNullOrEmpty(port.ProcessName)
                ? $"[bold white]{Markup.Escape(port.ProcessName)}[/][dim grey] ({port.ProcessId})[/]"
                : "[dim grey]unknown[/]";

            table.AddRow(
                $"[bold gold1]{port.Port}[/]",
                processCell,
                $"[white]{Markup.Escape(port.HostAddress)}[/]",
                $"[green]{port.Protocol}[/]",
                $"[grey]{port.Description}[/]"
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        var choices = activePorts
            .Select(p => new PortMenuItem(
                PortAction.Select,
                p.Port,
                $"Port {p.Port} - {(string.IsNullOrEmpty(p.ProcessName) ? p.Description : Markup.Escape(p.ProcessName))} ({Markup.Escape(p.HostAddress)})"))
            .ToList();
        choices.Add(new PortMenuItem(PortAction.Back, 0, "[[<]] Back to Main Menu"));

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<PortMenuItem>()
                .Title("[bold white]Select detected port to expose:[/]")
                .PageSize(15)
                .HighlightStyle(Highlight)
                .UseConverter(item => item.Label)
                .AddChoices(choices)
        );

        return selected.Action == PortAction.Back
            ? await PromptPortSelectionAsync(scanner)
            : selected.Port;
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
        string taskLabel = $"Preparing {provider.DisplayName} engine";

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
                    if (task == null)
                    {
                        task = ctx.AddTask($"[bold white]{taskLabel}[/]", maxValue: Math.Max(p.total, 1));
                        task.IsIndeterminate = p.total <= 0;
                    }
                    else if (task.IsIndeterminate && p.total > 0)
                    {
                        task.IsIndeterminate = false;
                        task.MaxValue = p.total;
                    }

                    if (!task.IsIndeterminate)
                    {
                        task.Value = Math.Min(p.processed, task.MaxValue);
                    }
                });

                binaryPath = await provider.EnsureBinaryAvailableAsync(progressHandler);
                if (task != null)
                {
                    task.IsIndeterminate = false;
                    task.Value = task.MaxValue;
                }
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

        session.OutputReceived -= logCapture;

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

        // 5. Live status strip + rolling traffic log.
        // OutputReceived fires on process I/O threads, so lines are queued and
        // drained here on the UI thread to keep console writes serialized.
        var trafficQueue = new ConcurrentQueue<string>();
        var recentOutput = new Queue<string>();

        Action<string> outputHandler = line =>
        {
            lock (recentOutput)
            {
                if (recentOutput.Count >= 8) recentOutput.Dequeue();
                recentOutput.Enqueue(line);
            }

            if (line.Contains("HTTP", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("connIndex", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("GET", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("POST", StringComparison.OrdinalIgnoreCase))
            {
                trafficQueue.Enqueue(line);
            }
        };

        session.OutputReceived += outputHandler;

        // Drop any keys buffered while the prompts ran so a stray 'q' typed
        // earlier can't instantly kill the fresh tunnel.
        try
        {
            if (!Console.IsInputRedirected)
            {
                while (Console.KeyAvailable) Console.ReadKey(true);
            }
        }
        catch { }

        using var dashboardCts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true; // Prevent app exit, cleanly return to menu
            dashboardCts.Cancel();
        };

        Console.CancelKeyPress += cancelHandler;

        var uptime = Stopwatch.StartNew();
        var liveLog = new Queue<string>();
        int trafficEvents = 0;

        try
        {
            if (Console.IsOutputRedirected)
            {
                await RunPlainDashboardLoopAsync(session, dashboardCts.Token);
            }
            else
            {
                await AnsiConsole.Live(BuildLiveView())
                    .AutoClear(false)
                    .StartAsync(async ctx =>
                    {
                        while (session.IsRunning && !dashboardCts.IsCancellationRequested)
                        {
                            while (trafficQueue.TryDequeue(out var line))
                            {
                                if (liveLog.Count >= 8) liveLog.Dequeue();
                                liveLog.Enqueue(line);
                                trafficEvents++;
                            }

                            ctx.UpdateTarget(BuildLiveView());
                            ctx.Refresh();

                            try
                            {
                                if (Console.KeyAvailable)
                                {
                                    var key = Console.ReadKey(intercept: true);
                                    if (key.Key is ConsoleKey.Q or ConsoleKey.Escape)
                                    {
                                        dashboardCts.Cancel();
                                        break;
                                    }
                                }
                            }
                            catch { }

                            await Task.Delay(150);
                        }
                    });
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            session.OutputReceived -= outputHandler;
        }

        if (dashboardCts.IsCancellationRequested)
        {
            await session.StopAsync();
            AnsiConsole.MarkupLine("[green][[OK]] Tunnel stopped cleanly. Returning to menu...[/]");
        }
        else
        {
            // Process exited on its own — surface that instead of a fake "clean stop".
            AnsiConsole.MarkupLine(session.Status == TunnelStatus.Faulted
                ? "[red][[FAULT]][/] Tunnel process crashed or was closed externally."
                : "[yellow][[!]][/] Tunnel process exited unexpectedly.");
            lock (recentOutput)
            {
                if (recentOutput.Count > 0)
                {
                    AnsiConsole.MarkupLine("[bold yellow]Last engine output:[/]");
                    foreach (var line in recentOutput)
                    {
                        AnsiConsole.MarkupLine($"[dim grey]>[/] [grey]{Markup.Escape(line)}[/]");
                    }
                }
            }
            AnsiConsole.MarkupLine("[grey]Press any key to return to main menu...[/]");
            try
            {
                if (!Console.IsInputRedirected)
                {
                    Console.ReadKey(true);
                }
            }
            catch { }
        }

        await Task.Delay(500);
        return;

        IRenderable BuildLiveView()
        {
            var layout = new Grid();
            layout.AddColumn();

            layout.AddRow(new Markup(
                $"[bold green]● LIVE[/]  [grey]Uptime[/] [bold white]{uptime.Elapsed:hh\\:mm\\:ss}[/]   " +
                $"[grey]Traffic events[/] [bold white]{trafficEvents}[/]   " +
                $"[grey]Engine[/] [bold white]{session.Provider}[/]   " +
                "[bold red][[Q]]/[[Ctrl+C]] disconnect[/]"));

            var logContent = liveLog.Count == 0
                ? "[dim grey]Waiting for edge traffic...[/]"
                : string.Join("\n", liveLog.Select(l => $"[dim grey]{DateTime.Now:HH:mm:ss}[/] [grey]{Markup.Escape(l)}[/]"));

            layout.AddRow(new Panel(new Markup(logContent))
            {
                Header = new PanelHeader("[grey]Edge Traffic Log[/]", Justify.Left),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.FromHex("374151")),
                Padding = new Padding(1, 0, 1, 0)
            });

            return layout;
        }

        async Task RunPlainDashboardLoopAsync(ITunnelSession s, CancellationToken ct)
        {
            while (s.IsRunning && !ct.IsCancellationRequested)
            {
                while (trafficQueue.TryDequeue(out var line))
                {
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss} {line}");
                }
                await Task.Delay(150);
            }
        }
    }
}
