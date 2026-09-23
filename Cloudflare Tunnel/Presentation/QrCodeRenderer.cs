using System.Text;
using Net.Codecrete.QrCodeGenerator;
using Spectre.Console;

namespace Cloudflare_Tunnel.Presentation;

public static class QrCodeRenderer
{
    /// <summary>
    /// Renders an ultra-compact, high-contrast, scannable QR code using Unicode half-blocks (▀).
    /// Each character cell represents two vertical modules, cutting height in half and
    /// producing a square, non-wrapping matrix compatible with all Windows consoles.
    /// </summary>
    public static Panel RenderQrCode(string text, string title = "[[QR]] MOBILE ACCESS")
    {
        var qr = QrCode.EncodeText(text, QrCode.Ecc.Low);
        int border = 2; // 2-module quiet zone for reliable mobile phone camera detection
        int size = qr.Size;
        int totalWidth = size + (border * 2);

        var sb = new StringBuilder();

        for (int y = -border; y < size + border; y += 2)
        {
            for (int x = -border; x < size + border; x++)
            {
                bool topDark = (x >= 0 && x < size && y >= 0 && y < size) && qr.GetModule(x, y);
                bool bottomDark = (x >= 0 && x < size && (y + 1) >= 0 && (y + 1) < size) && qr.GetModule(x, y + 1);

                // Phone cameras require dark modules (black) on light background (white).
                // Using Unicode upper-half block '▀' (\u2580):
                // Top half is Foreground, Bottom half is Background.
                if (topDark && bottomDark)
                {
                    sb.Append("[#000000 on #000000] [/]");
                }
                else if (!topDark && !bottomDark)
                {
                    sb.Append("[#FFFFFF on #FFFFFF] [/]");
                }
                else if (topDark && !bottomDark)
                {
                    sb.Append("[#000000 on #FFFFFF]▀[/]");
                }
                else // (!topDark && bottomDark)
                {
                    sb.Append("[#FFFFFF on #000000]▀[/]");
                }
            }
            sb.AppendLine();
        }

        var markup = new Markup(sb.ToString().TrimEnd());
        return new Panel(markup)
        {
            Header = new PanelHeader($"[bold gold1]{title}[/]", Justify.Center),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.FromHex("F38020")),
            Padding = new Padding(1, 0, 1, 0),
            Width = totalWidth + 4 // 2 border lines + 2 padding spaces
        };
    }
}
