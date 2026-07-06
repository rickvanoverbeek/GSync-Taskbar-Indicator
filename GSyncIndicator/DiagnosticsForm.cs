using System.Reflection;

namespace GSyncIndicator;

/// <summary>A copyable, refreshable dump of what NVAPI reports — for troubleshooting.</summary>
internal sealed class DiagnosticsForm : Form
{
    private readonly TextBox _text;

    public DiagnosticsForm()
    {
        Text = "G-Sync Indicator — Diagnostics";
        Width = 640;
        Height = 520;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        ShowInTaskbar = true;

        _text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9f),
            BackColor = Color.White
        };

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8)
        };
        var close = new Button { Text = "Close", Width = 90, Height = 28 };
        close.Click += (_, _) => Close();
        var copy = new Button { Text = "Copy", Width = 90, Height = 28 };
        copy.Click += (_, _) => { try { Clipboard.SetText(_text.Text); } catch { } };
        var refresh = new Button { Text = "Refresh", Width = 90, Height = 28 };
        refresh.Click += (_, _) => Populate();
        var save = new Button { Text = "Save…", Width = 90, Height = 28 };
        save.Click += (_, _) => Save();
        panel.Controls.Add(close);
        panel.Controls.Add(copy);
        panel.Controls.Add(save);
        panel.Controls.Add(refresh);

        Controls.Add(_text);
        Controls.Add(panel);

        Populate();
    }

    private void Populate()
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
        _text.Text =
            $"G-Sync Taskbar Indicator {version} — diagnostics" + Environment.NewLine +
            $"OS: {Environment.OSVersion.VersionString}, 64-bit process: {Environment.Is64BitProcess}" +
            Environment.NewLine + new string('-', 60) + Environment.NewLine +
            NvApi.BuildReport();
        _text.Select(0, 0);
    }

    private void Save()
    {
        using var dlg = new SaveFileDialog
        {
            FileName = "gsync-diagnostics.txt",
            Filter = "Text file (*.txt)|*.txt"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            try { File.WriteAllText(dlg.FileName, _text.Text); } catch { }
        }
    }
}
