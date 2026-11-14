using System.Collections.Concurrent;
using System.Diagnostics;
using Cloudflare_Tunnel.Core;
using Cloudflare_Tunnel.Infrastructure;
using Spectre.Console;
using Spectre.Console.Rendering;
using TextCopy;

namespace Cloudflare_Tunnel.Presentation;

public static class ConsoleViews
{
    private static readonly List<PortPreset> Presets = new()
    {
        new("Next.js / React", 3000, "Standard Node / Next.js Dev Server"),
        new("Vite / Vue / Svelte", 5173, "Fast HMR Frontend Dev Server"),
        new("Angular / Astro", 4200, "Angular CLI Dev Server"),
        new("ASP.NET Core HTTP", 5000, "Default Kestrel Local HTTP"),
        new("Python / PHP (Django, FastAPI, Laravel)", 8000, "Uvicorn / RunServer / artisan serve"),
        new("Spring Boot / Go / HTTP Alt", 8080, "Standard Alternate HTTP Port")
    };

    private static Table NewMenuTable(params string[] headers)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(ConsoleTheme.DarkBorder);

        foreach (string header in headers)
        {
            table.AddColumn(new TableColumn(header));
        }

        return table;
    }

    private static void WriteCentered(IRenderable renderable)
        => AnsiConsole.Write(new Align(renderable, HorizontalAlignment.Center));

    private static int PromptNumber(string prompt, int max)
    {
        return AnsiConsole.Prompt(
            new TextPrompt<int>($"[bold white]{prompt}[/]")
                .PromptStyle("gold1")
                .ValidationErrorMessage($"[red]Enter a number between 0 and {max}.[/]")
                .Validate(n => n >= 0 && n <= max)
        );
    }

    public static TunnelProviderType? PromptProvider(int selectedPort)
    {
        WriteCentered(new Markup($"[bold white]Choose Tunnel Provider[/]  [dim grey]•[/]  [cyan]Target:[/] [bold gold1]http://localhost:{selectedPort}[/]"));
        AnsiConsole.WriteLine();

        var table = NewMenuTable("[bold cyan]#[/]", "[bold white]Engine[/]", "[bold grey]Details[/]");
        table.AddRow("[bold gold1]1[/]", "[bold orange3][[CF]][/] Cloudflare Tunnel", "[grey]Embedded binary, 100% Free, Zero configuration[/]");
        table.AddRow("[bold gold1]2[/]", "[bold cyan][[NGROK]][/] Ngrok Tunnel", "[grey]Embedded binary, Authtoken required[/]");
        table.AddRow("[dim grey]0[/]", "[grey][[<]][/] [grey]Back to port selection[/]", "");
        WriteCentered(table);
        AnsiConsole.WriteLine();

        return PromptNumber("Select provider number:", 2) switch
        {
            1 => TunnelProviderType.Cloudflare,
            2 => TunnelProviderType.Ngrok,
            _ => null
        };
    }

    public static async Task<int?> PromptPortSelectionAsync(IPortScanner scanner)
    {
        while (true)
        {
            ConsoleTheme.RenderBanner();
            WriteCentered(new Markup("[bold white]Select a project or local port to tunnel[/]"));
            AnsiConsole.WriteLine();

            var table = NewMenuTable("[bold cyan]#[/]", "[bold white]Project[/]", "[bold white]Port[/]", "[bold grey]Description[/]");
            int number = 1;
            foreach (var p in Presets)
            {
                table.AddRow($"[bold gold1]{number++}[/]", $"[bold white]{p.Name}[/]", $"[white]{p.Port}[/]", $"[dim]{p.Description}[/]");
            }
            int scanNumber = number++;
            int customNumber = number++;
            int updateNumber = number++;
            table.AddRow($"[bold gold1]{scanNumber}[/]", "[cyan][[SCAN]][/]", "—", "[grey]Scan active listening ports on this PC[/]");
            table.AddRow($"[bold gold1]{customNumber}[/]", "[yellow][[CUSTOM]][/]", "—", "[grey]Enter custom port number[/]");
            table.AddRow($"[bold gold1]{updateNumber}[/]", "[green][[UPDATE]][/]", $"v{AppUpdater.CurrentVersion}", "[grey]Check for updates & install latest release[/]");
            table.AddRow("[dim grey]0[/]", "[red][[EXIT]][/]", "—", "[grey]Exit[/]");
            WriteCentered(table);
            AnsiConsole.WriteLine();

            int choice = PromptNumber("Select number:", number - 1);
            if (choice == 0) return null;

            if (choice == scanNumber)
            {
                int? scanned = await ScanAndSelectPortAsync(scanner);
                if (scanned != null) return scanned;
                continue;
            }

            if (choice == customNumber)
            {
                int? custom = PromptCustomPort();
                if (custom != null) return custom;
                continue;
            }

            if (choice == updateNumber)
            {
                await CheckAndInstallUpdateAsync();
                continue;
            }

            return Presets[choice - 1].Port;
        }
    }

    public static async Task CheckAndInstallUpdateAsync()
    {
        AnsiConsole.WriteLine();
        ReleaseInfo? release = null;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync("Checking GitHub Releases for updates...", async _ =>
            {
                release = await AppUpdater.FetchLatestReleaseAsync();
            });

        if (release == null)
        {
            AnsiConsole.MarkupLine("[yellow][[!]][/] Could not reach GitHub Releases (or no releases published yet).");
            ConsoleWindowHelper.ShowInfoMessageBox(
                $"You are currently running v{AppUpdater.CurrentVersion}.\n\nNo published GitHub Release was found yet.",
                "Cloudflare Tunnel Manager — Update Check");
            await WaitForKeyIfInteractiveAsync();
            return;
        }

        if (!AppUpdater.IsNewerVersion(AppUpdater.CurrentVersion, release.TagName))
        {
            AnsiConsole.MarkupLine($"[green][[OK]][/] You are running the latest version ([bold white]v{AppUpdater.CurrentVersion}[/]).");
            ConsoleWindowHelper.ShowInfoMessageBox(
                $"You are already running the latest version (v{AppUpdater.CurrentVersion}).",
                "Cloudflare Tunnel Manager — Up to Date");
            await WaitForKeyIfInteractiveAsync();
            return;
        }

        AnsiConsole.MarkupLine($"[bold green][[UPDATE AVAILABLE]][/] New release [bold gold1]{Markup.Escape(release.TagName)}[/] found (current: [grey]v{AppUpdater.CurrentVersion}[/]).");
        AnsiConsole.Write(new Panel(new Markup($"[white]{Markup.Escape(release.ReleaseNotes)}[/]"))
        {
            Header = new PanelHeader($"[bold gold1]What's New in {Markup.Escape(release.TagName)}[/]", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(ConsoleTheme.PrimaryOrange),
            Padding = new Padding(1, 0, 1, 0)
        });
        AnsiConsole.WriteLine();

        ConsoleWindowHelper.ShowInfoMessageBox(
            $"Update {release.TagName} is available!\n\n" +
            $"Current Version: v{AppUpdater.CurrentVersion}\n" +
            $"Latest Version:  {release.TagName}\n\n" +
            $"What's in this update:\n{release.ReleaseNotes}\n\n" +
            "Click OK to return to the console and install the update.",
            "Cloudflare Tunnel Manager — Update Available");

        if (!AnsiConsole.Confirm($"[bold white]Download and install {Markup.Escape(release.TagName)} now?[/]", defaultValue: true))
        {
            return;
        }

        bool updatedInPlace = false;
        string statusMessage = "";

        await AnsiConsole.Progress()
            .AutoRefresh(true)
            .AutoClear(false)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn() { CompletedStyle = new Style(ConsoleTheme.SuccessGreen) },
                new PercentageColumn(),
                new DownloadedColumn(),
                new TransferSpeedColumn(),
                new SpinnerColumn(Spinner.Known.Dots)
            })
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"[bold white]Downloading {Markup.Escape(release.TagName)}[/]", maxValue: 1);
                var progressHandler = new Progress<(long processed, long total, double speed)>(p =>
                {
                    if (p.total <= 0)
                    {
                        task.IsIndeterminate = true;
                        return;
                    }
                    task.IsIndeterminate = false;
                    task.MaxValue = p.total;
                    task.Value = Math.Min(p.processed, p.total);
                });

                (updatedInPlace, statusMessage) = await AppUpdater.ApplySelfUpdateAsync(release, progressHandler);
                task.IsIndeterminate = false;
                task.Value = task.MaxValue;
            });

        AnsiConsole.MarkupLine($"[bold green][[OK]][/] {Markup.Escape(statusMessage)}");
        ConsoleWindowHelper.ShowInfoMessageBox(
            statusMessage,
            updatedInPlace ? "Cloudflare Tunnel Manager — Update Installed" : "Cloudflare Tunnel Manager — Release Opened");
        await WaitForKeyIfInteractiveAsync();
    }

    private static Task WaitForKeyIfInteractiveAsync()
    {
        if (!Console.IsInputRedirected)
        {
            AnsiConsole.MarkupLine("\n[grey]Press any key to return to main menu...[/]");
            try { Console.ReadKey(true); } catch { }
        }
        return Task.CompletedTask;
    }

    // Runtimes that almost always host a dev web server when they listen on a port.
    private static readonly HashSet<string> DevRuntimeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node", "dotnet", "python", "pythonw", "php", "java", "javaw",
        "ruby", "perl", "bun", "deno", "nginx", "httpd", "apache",
        "iisexpress", "w3wp"
    };

    // Well-known dev-server ports — catches compiled servers (Go, Rust, etc.)
    // whose process names can't be whitelisted.
    private static readonly HashSet<int> DevPorts = new()
    {
        3000, 3001, 4200, 5000, 5001, 5173, 5174, 5500, 5501,
        7000, 7001, 7100, 8000, 8080, 8081, 8888, 9000, 1313, 4321
    };

    // OS core services + databases — never HTTP-tunnelable, always noise.
    private static readonly HashSet<string> SystemProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "svchost", "lsass", "wininit", "winlogon", "services",
        "smss", "csrss", "spoolsv", "vmms", "WmiPrvSE", "SearchIndexer",
        "sqlservr", "mysqld", "mariadbd", "postgres", "redis-server", "mongod", "memcached"
    };

    private static bool IsLikelyDevServer(ListeningPortInfo p) =>
        (p.ProcessName.Length > 0 && DevRuntimeNames.Contains(p.ProcessName)) ||
        DevPorts.Contains(p.Port);

    private static bool IsSystemProcess(ListeningPortInfo p) =>
        p.ProcessId is 0 or 4 ||
        (p.ProcessName.Length > 0 && SystemProcessNames.Contains(p.ProcessName));

    public static async Task<int?> ScanAndSelectPortAsync(IPortScanner scanner)
    {
        AnsiConsole.MarkupLine("\n[cyan]Auditing active network sockets...[/]");

        IReadOnlyList<ListeningPortInfo> allListeners = Array.Empty<ListeningPortInfo>();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync("Detecting running local servers...", async _ =>
            {
                await Task.Delay(200); // brief moment for socket enumeration
                allListeners = scanner.GetActiveListeners(1000, 65000);
            });

        if (allListeners.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow][[!]][/] No active listening TCP ports detected on this PC.");
            return PromptCustomPort();
        }

        // Prefer dev servers; fall back to non-system processes; last resort is everything.
        var activePorts = allListeners.Where(IsLikelyDevServer).ToList();
        if (activePorts.Count == 0)
        {
            activePorts = allListeners.Where(l => !IsSystemProcess(l)).ToList();
            if (activePorts.Count > 0)
            {
                AnsiConsole.MarkupLine("[dim grey]No framework dev servers detected — showing non-system listeners.[/]");
            }
        }
        if (activePorts.Count == 0)
        {
            activePorts = allListeners.ToList();
            AnsiConsole.MarkupLine("[dim grey]No dev servers detected — showing all listeners.[/]");
        }

        int hiddenCount = allListeners.Count - activePorts.Count;
        if (hiddenCount > 0)
        {
            AnsiConsole.MarkupLine($"[dim grey]({hiddenCount} system-service listener(s) hidden — use Custom for anything else)[/]");
        }

        var table = NewMenuTable(
            "[bold cyan]#[/]",
            "[bold cyan]Port[/]",
            "[bold white]Process[/]",
            "[bold white]Bound Address[/]",
            "[bold green]Protocol[/]",
            "[bold grey]Identified Service[/]");

        for (int i = 0; i < activePorts.Count; i++)
        {
            var port = activePorts[i];
            string processCell = !string.IsNullOrEmpty(port.ProcessName)
                ? $"[bold white]{Markup.Escape(port.ProcessName)}[/][dim grey] ({port.ProcessId})[/]"
                : "[dim grey]unknown[/]";

            table.AddRow(
                $"[bold gold1]{i + 1}[/]",
                $"[bold gold1]{port.Port}[/]",
                processCell,
                $"[white]{Markup.Escape(port.HostAddress)}[/]",
                $"[green]{port.Protocol}[/]",
                $"[grey]{port.Description}[/]"
            );
        }
        table.AddRow("[dim grey]0[/]", "[grey][[<]][/]", "[grey]Back[/]", "", "", "");

        WriteCentered(new Markup("[bold white]Detected listening ports[/]"));
        AnsiConsole.WriteLine();
        WriteCentered(table);
        AnsiConsole.WriteLine();

        int choice = PromptNumber("Select number of the port to expose (0 to go back):", activePorts.Count);
        return choice == 0 ? null : activePorts[choice - 1].Port;
    }

    public static int? PromptCustomPort()
    {
        int port = AnsiConsole.Prompt(
            new TextPrompt<int>("[bold white]Enter local port number (1-65535, or 0 to go back):[/]")
                .PromptStyle("gold1")
                .ValidationErrorMessage("[red]Please enter 0 to go back, or a valid port between 1 and 65535.[/]")
                .Validate(p => p is >= 0 and <= 65535)
        );
        return port == 0 ? null : port;
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
                new ProgressBarColumn() { CompletedStyle = new Style(ConsoleTheme.SuccessGreen) },
                new PercentageColumn(),
                new DownloadedColumn(),
                new TransferSpeedColumn(),
                new SpinnerColumn(Spinner.Known.Dots)
            })
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"[bold white]{taskLabel}[/]", maxValue: 1);

                var progressHandler = new Progress<(long processed, long total, double speed)>(p =>
                {
                    if (p.total <= 0)
                    {
                        task.IsIndeterminate = true;
                        return;
                    }

                    task.IsIndeterminate = false;
                    task.MaxValue = p.total;
                    task.Value = Math.Min(p.processed, p.total);
                });

                binaryPath = await provider.EnsureBinaryAvailableAsync(progressHandler);
                task.IsIndeterminate = false;
                task.Value = task.MaxValue;
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

        // 2. Wait for Public URL assignment with status spinner (Ctrl+C cancels cleanly)
        using var dashboardCts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true; // Prevent app exit, cleanly return to menu
            dashboardCts.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

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
        try
        {
            if (publicUrl == null)
            {
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("gold1"))
                    .StartAsync("Registering secure tunnel endpoint with edge network...", async _ =>
                    {
                        var sw = Stopwatch.StartNew();
                        while (session.PublicUrl == null &&
                               session.IsRunning &&
                               !dashboardCts.IsCancellationRequested &&
                               sw.Elapsed < TimeSpan.FromSeconds(30))
                        {
                            await Task.Delay(200);
                        }
                        publicUrl = session.PublicUrl;
                    });
            }
        }
        finally
        {
            session.OutputReceived -= logCapture;
        }

        if (dashboardCts.IsCancellationRequested)
        {
            Console.CancelKeyPress -= cancelHandler;
            await session.StopAsync();
            AnsiConsole.MarkupLine("\n[yellow]Tunnel startup cancelled. Returning to menu...[/]");
            await Task.Delay(400);
            return;
        }

        if (publicUrl == null)
        {
            Console.CancelKeyPress -= cancelHandler;
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
        ConsoleTheme.RenderBanner();

        var infoTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(ConsoleTheme.PrimaryOrange)
            .AddColumn(new TableColumn("[bold white]Property[/]").Width(14))
            .AddColumn(new TableColumn("[bold white]Details[/]").NoWrap());

        infoTable.AddRow("[bold cyan]Provider[/]", $"[bold white]{session.Provider}[/]");
        infoTable.AddRow("[bold cyan]Local Target[/]", $"[bold white]http://{options.Hostname}:{options.LocalPort}[/]");
        infoTable.AddRow("[bold cyan]Public URL[/]", $"[bold green on black] {publicUrl} [/]");
        infoTable.AddRow("[bold cyan]Clipboard[/]", copied ? "[bold green][[OK]] Auto-copied to clipboard![/]" : "[grey]Press [[C]] to copy URL[/]");
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
            BorderStyle = new Style(ConsoleTheme.SuccessGreen),
            Padding = new Padding(1, 1, 1, 1)
        });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold white]Shortcuts:[/] [bold cyan][[O]][/] Open in browser   [bold cyan][[C]][/] Copy URL   [bold red][[Q]] / [[Esc]] / [[Ctrl+C]][/] Disconnect & return to menu\n");

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

        var uptime = Stopwatch.StartNew();
        var liveLog = new Queue<string>();
        int trafficEvents = 0;
        string lastActionNotice = "";

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
                                    if (key.Key == ConsoleKey.C)
                                    {
                                        try
                                        {
                                            await ClipboardService.SetTextAsync(publicUrl.ToString());
                                            lastActionNotice = $"[green]Copied URL ({DateTime.Now:HH:mm:ss})[/]";
                                        }
                                        catch { }
                                    }
                                    else if (key.Key == ConsoleKey.O)
                                    {
                                        try
                                        {
                                            Process.Start(new ProcessStartInfo(publicUrl.ToString()) { UseShellExecute = true });
                                            lastActionNotice = $"[cyan]Opened in browser ({DateTime.Now:HH:mm:ss})[/]";
                                        }
                                        catch { }
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

            string noticeSuffix = string.IsNullOrEmpty(lastActionNotice) ? "" : $"   {lastActionNotice}";
            layout.AddRow(new Markup(
                $"[bold green]● LIVE[/]  [grey]Uptime[/] [bold white]{uptime.Elapsed:hh\\:mm\\:ss}[/]   " +
                $"[grey]Traffic events[/] [bold white]{trafficEvents}[/]   " +
                $"[grey]Engine[/] [bold white]{session.Provider}[/]   " +
                $"[cyan][[O]] open[/]  [cyan][[C]] copy[/]  [bold red][[Q]] disconnect[/]{noticeSuffix}"));

            var logContent = liveLog.Count == 0
                ? "[dim grey]Waiting for edge traffic...[/]"
                : string.Join("\n", liveLog.Select(l => $"[dim grey]{DateTime.Now:HH:mm:ss}[/] [grey]{Markup.Escape(l)}[/]"));

            layout.AddRow(new Panel(new Markup(logContent))
            {
                Header = new PanelHeader("[grey]Edge Traffic Log[/]", Justify.Left),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(ConsoleTheme.DarkBorder),
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
