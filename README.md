# G-Sync Taskbar Indicator

A small Windows system-tray (taskbar) indicator that shows whether **NVIDIA G-Sync /
adaptive sync** is currently active on your display.

The icon is **always present** in the notification area, so you can see the state at a
glance even when no game or app is running.

| Icon | State | Meaning |
|------|-------|---------|
| 🟢 Green  | **Active**      | Variable refresh (G-Sync) is engaged **right now** — a game/app is presenting with VRR. |
| 🟠 Amber  | **On (idle)**   | A G-Sync-capable display is present and ready, but VRR isn't engaged at the moment (e.g. sitting on the desktop). |
| ⚪ Gray   | **Unavailable** | No NVIDIA GPU/driver, or no display reports G-Sync / adaptive-sync support. |

> Note: NVIDIA's driver reports adaptive sync as "engaged this instant," not "enabled in
> settings." On the desktop a G-Sync monitor normally shows **amber** and turns **green**
> when a fullscreen (or windowed, depending on your G-Sync mode) app actually drives VRR.

Hover the icon for a summary, **double-click** it for a per-display breakdown, or
right-click for the menu.

## How it works

Windows does not expose a simple "is G-Sync on" flag, so the indicator talks to the
NVIDIA driver directly through **NVAPI** (`nvapi64.dll`):

1. It enumerates the connected displays on your NVIDIA GPU(s).
2. For each display it reads the **adaptive-sync data** via
   `NvAPI_DISP_GetAdaptiveSyncData`.
3. "Active" is inferred by watching the adaptive-sync **flip timestamp**: when variable
   refresh is actually driving a display, that timestamp keeps advancing between polls (it
   is polled once per second); on a static desktop it does not. This is the same signal
   NVIDIA's own on-screen G-Sync indicator reflects.

No configuration files, no admin rights, and no third-party services.

## Requirements

- Windows 10 / 11 (64-bit)
- An NVIDIA GPU with a current driver (provides `nvapi64.dll`)
- A G-Sync / G-Sync Compatible / adaptive-sync monitor with G-Sync enabled in the
  **NVIDIA Control Panel → Display → Set up G-SYNC**
- To build from source: the free [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Install

There are three ways to get it running, easiest first.

### 1. Installer (recommended)

Download `GSyncIndicator-Setup-<version>.exe` from the
[Releases](https://github.com/rickvanoverbeek/gsync-taskbar-indicator/releases) page and run
it. It installs per-user (no administrator prompt), adds a Start-menu shortcut, and offers
to start the app automatically when you sign in. Uninstall from **Settings → Apps** like any
other program.

> No release yet? See **Build the installer yourself** below — it's one command.

### 2. Portable exe

Download `GSyncIndicator-Portable-<version>.exe` from Releases and double-click it — no
installation, no .NET required. Use the tray menu's **Start with Windows** to have it launch
on sign-in.

### 3. Run from source

```powershell
dotnet run --project GSyncIndicator -c Release
```

Requires the free [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

## Build the installer yourself

From the repository root on Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

This publishes a self-contained single-file exe and, if
[Inno Setup 6](https://jrsoftware.org/isdl.php) is installed
(`winget install JRSoftware.InnoSetup`), compiles the installer. Outputs land in `dist\`:

```
dist\GSyncIndicator-Portable-1.0.0.exe     # portable, self-contained
dist\GSyncIndicator-Setup-1.0.0.exe        # installer
```

Pass `-SkipInstaller` to build only the portable exe. Pushing a `v*` tag builds both on CI
and attaches them to a GitHub Release (see `.github/workflows/release.yml`).

> Building on Linux/macOS (for CI or a compile check only — it cannot run there) requires
> the extra flag `-p:EnableWindowsTargeting=true`.

## Start automatically with Windows

Right-click the tray icon and tick **Start with Windows**. This adds a per-user entry
under `HKCU\…\CurrentVersion\Run` (no admin rights needed); untick to remove it.

## Menu

- **Status** — current overall state
- **Displays** — per-display state (`ACTIVE` / `on (idle)` / `off` / `no G-Sync`), with the
  primary display marked
- **Notify on state change** — pop a balloon when the state flips (off by default)
- **Start with Windows** — toggle auto-start
- **Refresh now** — force an immediate poll
- **About** / **Exit**

## Notes & limitations

- Only one instance runs at a time (enforced with a named mutex).
- "Active" reflects whether VRR flips are happening in the last ~1 s. A game that is
  paused/minimized and not presenting may briefly read as **on (idle)**.
- The adaptive-sync query requires a reasonably current NVIDIA driver. On very old drivers
  the indicator falls back to **Unavailable** and the tooltip explains why.
- This is a read-only monitor; it never changes any driver or display setting.

## Project layout

```
├─ GSyncIndicator/
│  ├─ Program.cs                  # entry point + single-instance guard
│  ├─ TrayApplicationContext.cs   # tray icon, poll timer, context menu
│  ├─ GSyncMonitor.cs             # turns raw NVAPI data into a single status
│  ├─ NvApi.cs                    # P/Invoke layer over nvapi64.dll
│  ├─ IconFactory.cs              # draws the colored tray icons at runtime
│  ├─ AutoStart.cs                # "start with Windows" registry toggle
│  └─ appicon.ico                 # application/shortcut icon
├─ installer/GSyncIndicator.iss   # Inno Setup installer script
├─ tools/make_icon.py             # regenerates appicon.ico
├─ build.ps1                      # publish exe + compile installer
└─ .github/workflows/release.yml  # CI: build installer, publish release
```
