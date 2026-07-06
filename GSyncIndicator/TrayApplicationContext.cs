using System.Text;

namespace GSyncIndicator;

/// <summary>
/// Owns the tray icon, the poll timer, and the context menu. The icon is always
/// present so the current G-Sync state is visible even when no game or app is running.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private const int PollIntervalMs = 1000;

    private readonly GSyncMonitor _monitor = new();
    private readonly IconFactory _icons = new();
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;

    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _displaysHeader;
    private readonly ToolStripMenuItem _notifyItem;
    private readonly ToolStripMenuItem _autoStartItem;

    private GSyncState _lastState = (GSyncState)(-1);
    private bool _notifyOnChange = false;

    public TrayApplicationContext()
    {
        _statusItem = new ToolStripMenuItem("Starting…") { Enabled = false };
        _displaysHeader = new ToolStripMenuItem("Displays") { Enabled = false };

        _notifyItem = new ToolStripMenuItem("Notify on state change", null, ToggleNotify)
        {
            CheckOnClick = true,
            Checked = _notifyOnChange
        };
        _autoStartItem = new ToolStripMenuItem("Start with Windows", null, ToggleAutoStart)
        {
            CheckOnClick = true,
            Checked = AutoStart.IsEnabled()
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_displaysHeader);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_notifyItem);
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripMenuItem("Refresh now", null, (_, _) => Tick()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Diagnostics…", null, ShowDiagnostics));
        menu.Items.Add(new ToolStripMenuItem("About", null, ShowAbout));
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));
        menu.Opening += (_, _) => Tick();   // freshest data when the user opens the menu

        _tray = new NotifyIcon
        {
            Icon = _icons.Get(GSyncState.Unavailable),
            Text = "G-Sync indicator — starting…",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => ShowDetailsBalloon();

        _timer = new System.Windows.Forms.Timer { Interval = PollIntervalMs };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        Tick(); // paint an accurate state immediately
    }

    private void Tick()
    {
        GSyncStatus status;
        try
        {
            status = _monitor.Poll();
        }
        catch (Exception ex)
        {
            _tray.Icon = _icons.Get(GSyncState.Unavailable);
            _tray.Text = Clamp("G-Sync: error — " + ex.Message);
            return;
        }

        _tray.Icon = _icons.Get(status.State);
        _tray.Text = Clamp(BuildTooltip(status));
        UpdateMenu(status);

        if (_notifyOnChange && status.State != _lastState && _lastState != (GSyncState)(-1))
            _tray.ShowBalloonTip(3000, "G-Sync", status.ShortLabel, ToolTipIcon.Info);

        _lastState = status.State;
    }

    private void UpdateMenu(GSyncStatus status)
    {
        _statusItem.Text = status.ShortLabel;

        var dd = _displaysHeader.DropDownItems;
        dd.Clear();
        if (status.Displays.Count == 0)
        {
            dd.Add(new ToolStripMenuItem(status.Note ?? "No displays") { Enabled = false });
        }
        else
        {
            foreach (var d in status.Displays)
                dd.Add(new ToolStripMenuItem(DescribeDisplay(d)) { Enabled = false });
        }
    }

    private static string DescribeDisplay(DisplayStatus d)
    {
        string name = $"Display 0x{d.DisplayId:X8}" + (d.IsPrimary ? " (primary)" : "");
        string state = !d.Capable  ? "no G-Sync"
                     : d.ActiveNow ? "ACTIVE"
                                   : "on (idle)";
        return $"{name} — {state}";
    }

    private static string BuildTooltip(GSyncStatus status)
    {
        var sb = new StringBuilder();
        sb.Append(status.ShortLabel);
        if (status.Note is not null)
        {
            sb.Append(" — ").Append(status.Note);
            return sb.ToString();
        }

        int active = status.Displays.Count(x => x.ActiveNow);
        int capable = status.Displays.Count(x => x.Capable);
        int total = status.Displays.Count;
        sb.Append($"\n{total} display(s), {capable} G-Sync-capable, {active} active");
        return sb.ToString();
    }

    private void ShowDetailsBalloon()
    {
        var status = _monitor.Poll();
        var sb = new StringBuilder();
        if (status.Displays.Count == 0)
            sb.Append(status.Note ?? "No displays found.");
        else
            foreach (var d in status.Displays)
                sb.AppendLine(DescribeDisplay(d));
        _tray.ShowBalloonTip(4000, status.ShortLabel, sb.ToString().Trim(), ToolTipIcon.Info);
    }

    private void ToggleNotify(object? sender, EventArgs e)
        => _notifyOnChange = _notifyItem.Checked;

    private void ToggleAutoStart(object? sender, EventArgs e)
        => AutoStart.SetEnabled(_autoStartItem.Checked);

    private DiagnosticsForm? _diagnostics;

    private void ShowDiagnostics(object? sender, EventArgs e)
    {
        if (_diagnostics is null || _diagnostics.IsDisposed)
            _diagnostics = new DiagnosticsForm();
        _diagnostics.Show();
        _diagnostics.WindowState = FormWindowState.Normal;
        _diagnostics.BringToFront();
        _diagnostics.Activate();
    }

    private void ShowAbout(object? sender, EventArgs e)
    {
        MessageBox.Show(
            "G-Sync Taskbar Indicator\n\n" +
            "Shows whether NVIDIA G-Sync / adaptive sync is currently driving your display.\n\n" +
            "Green = active (variable refresh engaged right now)\n" +
            "Amber = G-Sync-capable, ready but idle (no app is using it)\n" +
            "Gray = no G-Sync display / unavailable\n\n" +
            "Detection uses NVAPI's adaptive-sync flip data.",
            "About G-Sync Indicator",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static string Clamp(string s)
    {
        // NotifyIcon.Text has a hard limit of 127 characters.
        s = s.Replace("\r", "");
        return s.Length <= 127 ? s : s[..127];
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _icons.Dispose();
            NvApi.Unload();
        }
        base.Dispose(disposing);
    }
}
