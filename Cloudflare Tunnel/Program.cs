using System.Text;
using Cloudflare_Tunnel.Core;
using Cloudflare_Tunnel.Infrastructure;
using Cloudflare_Tunnel.Presentation;
using Spectre.Console;

try
{
    Console.OutputEncoding = Encoding.UTF8;
    Console.InputEncoding = Encoding.UTF8;
}
catch { }

try { Console.Title = $"Cloudflare Tunnel Manager | Developed by {ConsoleTheme.DeveloperAttribution}"; } catch { }
ConsoleWindowHelper.CenterAndConfigureWindow();

var binaryManager = new BinaryManager();
var portScanner = new SystemPortScanner();
var cloudflareProvider = new CloudflareTunnelProvider(binaryManager);
var ngrokProvider = new NgrokTunnelProvider(binaryManager);

while (true)
{
    try
    {
        if (!Console.IsOutputRedirected)
        {
            try { AnsiConsole.Clear(); } catch { }
        }

        ConsoleTheme.RenderBanner();

        // 1. Select Port or Project
        int? selectedPort = await ConsoleViews.PromptPortSelectionAsync(portScanner);
        if (selectedPort == null)
        {
            break;
        }

        AnsiConsole.WriteLine();

        // 2. Select Tunnel Provider (Cloudflare or Ngrok), or go back
        var providerType = ConsoleViews.PromptProvider();
        if (providerType == null)
        {
            continue;
        }

        ITunnelProvider provider = providerType == TunnelProviderType.Cloudflare
            ? cloudflareProvider
            : ngrokProvider;

        AnsiConsole.WriteLine();

        // 3. Ensure Binary with Genuine Real-Time Progress Bar
        await ConsoleViews.EnsureBinaryWithRealProgressAsync(provider);

        // 4. Configure Options
        string? authToken = null;
        string? customDomain = null;
        if (providerType == TunnelProviderType.Ngrok)
        {
            string? savedConfig = NgrokTunnelProvider.FindSavedConfigPath();
            if (savedConfig != null)
            {
                AnsiConsole.MarkupLine($"[dim]Using saved ngrok authtoken from {Markup.Escape(savedConfig)}[/]");
            }
            else
            {
                authToken = AnsiConsole.Prompt(
                    new TextPrompt<string>("[white]Enter Ngrok Authtoken (from [link]dashboard.ngrok.com[/]):[/]")
                        .AllowEmpty()
                        .Secret('*')
                );

                if (string.IsNullOrWhiteSpace(authToken))
                {
                    AnsiConsole.MarkupLine("[yellow][[!]][/] No authtoken provided — ngrok requires one to start a tunnel.");
                }
            }

            customDomain = AnsiConsole.Prompt(
                new TextPrompt<string>("[white]Reserved ngrok domain [[optional, e.g. myapp.ngrok-free.dev — Enter to skip]][/]:[/]")
                    .AllowEmpty()
            );
            if (string.IsNullOrWhiteSpace(customDomain))
            {
                customDomain = null;
            }
        }

        var options = new TunnelOptions(selectedPort.Value, "localhost", CustomSubdomain: customDomain, AuthToken: authToken);

        // 5. Start Tunnel Session
        await using var session = await provider.StartTunnelAsync(options);

        // 6. Run Connected Dashboard (Public URL, QR Code, Clipboard, Live logs)
        await ConsoleViews.RunActiveTunnelDashboardAsync(session, options, portScanner);
    }
    catch (OperationCanceledException)
    {
        AnsiConsole.MarkupLine("\n[yellow]Operation cancelled by user.[/]");
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine("\n[bold red]An unexpected error occurred:[/]");
        AnsiConsole.WriteException(ex);

        // With redirected input every interactive prompt re-throws — don't spin forever.
        if (Console.IsInputRedirected)
        {
            break;
        }

        AnsiConsole.MarkupLine("[grey]Press any key to return to main menu...[/]");
        try
        {
            Console.ReadKey(true);
        }
        catch { }
    }
}

ConsoleTheme.RenderFooter();
AnsiConsole.MarkupLine("[bold white]Goodbye! Thank you for using Cloudflare Tunnel Manager.[/]");
