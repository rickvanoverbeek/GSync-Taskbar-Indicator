# G-Sync Taskbar Indicator

A small Windows system-tray (taskbar) indicator that shows whether **NVIDIA G-Sync /
adaptive sync** is currently active on your display.

The icon is **always present** in the notification area, so you can see the state at a
glance even when no game or app is running.

| Icon | State | Meaning |
|------|-------|---------|
| 🟢 Green  | **Active**      | Variable refresh (G-Sync) is driving a display **right now** — a game/app is presenting with VRR. |
| 🟠 Amber  | **On (idle)**   | G-Sync is enabled and ready, but nothing is currently using it. |
| ⚪ Gray   | **Off**         | A G-Sync-capable display was found but adaptive sync is turned off for it. |
| ⚫ Dim    | **Unavailable** | No NVIDIA GPU/driver, or no display reports G-Sync support. |

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

## Build & run

From a terminal in the repository root on your Windows machine:

```powershell
# Run directly
dotnet run --project GSyncIndicator -c Release

# …or produce a standalone .exe that does NOT need .NET installed
dotnet publish GSyncIndicator -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true
```

The published single file lands in:

```
GSyncIndicator\bin\Release\net8.0-windows\win-x64\publish\GSyncIndicator.exe
```

Double-click that `.exe` (or the `dotnet run` above) and the icon appears in the tray.

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
GSyncIndicator/
├─ Program.cs                  # entry point + single-instance guard
├─ TrayApplicationContext.cs   # tray icon, poll timer, context menu
├─ GSyncMonitor.cs             # turns raw NVAPI data into a single status
├─ NvApi.cs                    # P/Invoke layer over nvapi64.dll
├─ IconFactory.cs              # draws the colored tray icons at runtime
└─ AutoStart.cs               # "start with Windows" registry toggle
```
