# Cloudflare & Ngrok Dual-Engine Tunnel Manager

<p align="center">
  <img src="Cloudflare%20Tunnel/icon.png" alt="Cloudflare Tunnel Logo" width="160" height="160" />
</p>

<p align="center">
  <strong>Universal Reverse Proxy & Multi-Project Edge Tunneling Console for Windows</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white" alt=".NET 10.0" />
  <img src="https://img.shields.io/badge/C%23-13.0-239120?style=flat-square&logo=csharp&logoColor=white" alt="C# 13" />
  <img src="https://img.shields.io/badge/Platform-Windows%20x64-0078D6?style=flat-square&logo=windows&logoColor=white" alt="Windows" />
  <img src="https://img.shields.io/badge/Engines-Cloudflare%20%2B%20Ngrok-F38020?style=flat-square&logo=cloudflare&logoColor=white" alt="Cloudflare & Ngrok" />
  <img src="https://img.shields.io/badge/UI-Spectre.Console-blueviolet?style=flat-square" alt="Spectre.Console" />
</p>

---

## Overview

**Cloudflare & Ngrok Dual-Engine Tunnel Manager** is a high-craft terminal application built with **.NET 10** and **Spectre.Console**. It enables instant, secure public HTTPS access to local web development servers and APIs from anywhere in the world — without complex firewall setup, port forwarding, or public IP addresses.

Both **Cloudflare** (`cloudflared.exe`) and **Ngrok** (`ngrok.exe`) are **directly embedded inside the compiled application assembly**. The executable is fully self-contained, launches centered on your screen, and runs without requiring external binary downloads.

---

## Key Features

- ⚡ **Dual Tunneling Engines**:
  - **Cloudflare Quick Tunnels**: Zero-configuration, account-free instant tunnels routed through Cloudflare's global Anycast edge network.
  - **Ngrok Edge Tunnels**: Production-grade tunnels with persisted authtoken configuration and full HTTP traffic inspection.
- 📦 **Embedded Engine Binaries**:
  - `cloudflared.exe` and `ngrok.exe` are embedded inside the executable assembly.
  - Extracted on first run to `%LOCALAPPDATA%\UniversalTunnel\bin\` with real-time transfer progress and throughput indicator (`MB/s`).
  - Subsequent runs detect the cached binaries and start instantly.
- 🪟 **Automatic Window Centering & Sizing**:
  - Restores any maximized window state to normal windowed mode.
  - Sized optimally to **120 columns × 36 rows** for full side-by-side dashboard visibility without wrapping.
  - Centers itself on the primary/active monitor's working area (accounting for the Windows taskbar).
  - Disables the Maximize button and window resize frame to maintain pixel-perfect layout alignment.
- 📱 **Ultra-Compact Mobile QR Code**:
  - Uses dual-pixel Unicode half-block encoding (`▀`, 2 modules per character cell).
  - Shrinks QR code dimensions to a crisp **37 columns × 19 rows** with a standard 2-module quiet zone.
  - 24-bit high-contrast RGB black/white rendering for instant scanning with smartphone cameras on local WiFi or mobile networks.
- 📋 **Automatic Clipboard Sync**:
  - Copies generated public HTTPS tunnel URLs straight to the Windows clipboard upon connection.
- 🔍 **Active Port Scanner & Presets**:
  - Automatically scans and lists local TCP ports currently in `Listen` state on your system.
  - Quick presets for popular stacks:
    - **Next.js / React** (Port `3000`)
    - **Vite / Vue / Svelte** (Port `5173`)
    - **ASP.NET Core HTTP** (Port `5000`)
    - **FastAPI / Django** (Port `8000`)
    - **Custom Port Entry**
- 🎨 **Modern Cyber Terminal Aesthetics**:
  - Neon cyan, orange, and purple Spectre.Console theme.
  - Centered ASCII Figlet header banner and balanced technical specification card.
  - Dedicated developer attribution badge: **Developed by Logan M. Panucat**.
- 🛑 **Graceful Disconnection Loop**:
  - Press `[Q]` or `[Ctrl+C]` at any time while the tunnel is running to cleanly disconnect child processes and return smoothly to the main menu without freezing or crashing the terminal.
- 🚀 **Self-Contained Single-File Output**:
  - The application icon (`app.ico`) is embedded directly in the Win32 PE binary header and loaded dynamically via `ExtractIconEx`.
  - Zero loose `.ico` or support files in your publish output folder.

---

## Architecture & Project Structure

```
Cloudflare Tunnel/
├── Core/
│   ├── ITunnelSession.cs            # Abstraction for tunnel lifecycle and output streaming
│   └── TunnelConfig.cs              # Tunnel configuration models and engine options
├── Infrastructure/
│   ├── BinaryManager.cs             # Embedded assembly resource streaming and caching
│   ├── CloudflareTunnelSession.cs   # cloudflared process manager and URL regex parser
│   ├── NgrokTunnelSession.cs        # ngrok process manager, authtoken setup, API reader
│   ├── PortDetector.cs              # Win32 IPGlobalProperties TCP port scanner
│   └── ConsoleWindowHelper.cs       # Win32 User32/Shell32 window centering, icon & styles
├── Presentation/
│   ├── ConsoleTheme.cs              # Spectre.Console color scheme, Figlet banner & info card
│   ├── ConsoleViews.cs              # Interactive menu selection, progress bars & dashboard
│   └── QrCodeRenderer.cs            # Ultra-compact Unicode half-block QR code renderer
├── app.ico                          # Multi-resolution application icon (16x16 to 256x256)
├── icon.png                         # High-resolution application brand artwork
├── Program.cs                       # Application entry point and interactive loop
└── Cloudflare Tunnel.csproj         # .NET 10 project definition with embedded binaries
```

---

## Prerequisites

- **Operating System**: Windows 10 or Windows 11 (64-bit)
- **Runtime / SDK**: [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (or higher)
- **IDE**: Visual Studio 2022+ (v17.12+) or Visual Studio Code with C# Dev Kit

---

## Getting Started

### 1. Clone the Repository
```bash
git clone https://github.com/your-username/cloudflare-tunnel.git
cd "cloudflare-tunnel"
```

### 2. Build the Project
```powershell
dotnet build "Cloudflare Tunnel\Cloudflare Tunnel.csproj"
```

### 3. Run Locally
```powershell
dotnet run --project "Cloudflare Tunnel\Cloudflare Tunnel.csproj"
```

---

## Publishing as a Single-File Executable

You can compile the entire application into a standalone Windows `.exe` that requires no .NET runtime installation on the target machine:

```powershell
dotnet publish "Cloudflare Tunnel\Cloudflare Tunnel.csproj" `
  -c Release `
  -r win-x64 `
  --self-contained `
  -p:PublishSingleFile=true `
  -o "publish\win-x64"
```

The resulting `publish\win-x64\Cloudflare Tunnel.exe` contains:
- The compiled C# application and .NET 10 runtime
- Embedded `cloudflared.exe` binary
- Embedded `ngrok.exe` binary
- Native Win32 application icon inside the PE resources
- **Zero external loose files needed**

---

## Usage Guide

1. **Launch the Application**:
   Run `Cloudflare Tunnel.exe`. The console window will automatically center itself on your screen, apply the cyber theme, and display the dashboard banner.

2. **Select Local Port**:
   - Choose from running detected local servers, standard dev presets (3000, 5173, 5000, 8000), or enter a custom port number.

3. **Choose Tunnel Engine**:
   - **Cloudflare**: No registration required. Connects immediately and provisions a `*.trycloudflare.com` edge URL.
   - **Ngrok**: First-time users will be prompted for an Ngrok authtoken (obtainable free from [dashboard.ngrok.com](https://dashboard.ngrok.com)). Tokens are saved to `%LOCALAPPDATA%\UniversalTunnel\config.json` for all future sessions.

4. **Monitor Active Tunnel**:
   - The Public HTTPS URL is displayed and copied to your clipboard.
   - A high-contrast Unicode QR code is displayed side-by-side for instant mobile phone testing.
   - Real-time HTTP request traffic is logged at the bottom of the dashboard.

5. **Disconnect & Return**:
   - Press **`Q`** or **`Ctrl+C`** to gracefully stop the tunnel and return back to the port selection menu.
   - Select **Exit** from the menu or press **`Ctrl+C`** at the prompt to close the application.

---

## Configuration & Cache Locations

All extracted runtime binaries and saved settings are isolated under:
```
%LOCALAPPDATA%\UniversalTunnel\
├── bin\
│   ├── cloudflared.exe
│   └── ngrok.exe
└── config.json
```

---

## Credits & Author

- **Author**: **Logan M. Panucat**
- **Powered by**:
  - [Spectre.Console](https://spectreconsole.net/)
  - [Cloudflare Tunnels](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/)
  - [Ngrok](https://ngrok.com/)
  - [Net.Codecrete.QrCodeGenerator](https://github.com/manuelbl/QrCodeGenerator)
  - [TextCopy](https://github.com/CopyText/TextCopy)

---

## License

This project is licensed under the [MIT License](LICENSE).
