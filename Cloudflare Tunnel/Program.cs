using System.Text;
using Cloudflare_Tunnel.Core;
using Cloudflare_Tunnel.Infrastructure;
using Cloudflare_Tunnel.Presentation;
using Spectre.Console;

Console.OutputEncoding = Encoding.UTF8;
Console.Title = $"Cloudflare Tunnel Manager | Developed by {ConsoleTheme.DeveloperAttribution}";
ConsoleWindowHelper.CenterAndConfigureWindow();

var binaryManager = new BinaryManager();
var portScanner = new SystemPortScanner();
var cloudflareProvider = new CloudflareTunnelProvider(binaryManager);
var ngrokProvider = new NgrokTunnelProvider(binaryManager);

bool keepRunning = true;

while (keepRunning)
{
    try
    {
        ConsoleTheme.RenderBanner();

        // 1. Select Port or Project
        int? selectedPort = await ConsoleViews.PromptPortSelectionAsync(portScanner);
        if (selectedPort == null)
        {
            keepRunning = false;
            break;
        }

        AnsiConsole.WriteLine();

        // 2. Select Tunnel Provider (Cloudflare or Ngrok)
        var providerType = ConsoleViews.PromptProvider();
        ITunnelProvider provider = providerType == TunnelProviderType.Cloudflare
            ? cloudflareProvider
            : ngrokProvider;

        AnsiConsole.WriteLine();

        // 3. Ensure Binary with Genuine Real-Time Progress Bar
        await ConsoleViews.EnsureBinaryWithRealProgressAsync(provider);

        // 4. Configure Options & Prompt Ngrok token if needed
        string? authToken = null;
        if (providerType == TunnelProviderType.Ngrok)
        {
            authToken = AnsiConsole.Prompt(
                new TextPrompt<string>("[white]Enter Ngrok Authtoken (or press [bold gold1]Enter[/] if already configured):[/]")
                    .AllowEmpty()
            );
        }

        var options = new TunnelOptions(selectedPort.Value, "localhost", AuthToken: authToken);

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
}

ConsoleTheme.RenderFooter();
AnsiConsole.MarkupLine("[bold white]Goodbye! Thank you for using Cloudflare Tunnel Manager.[/]");
