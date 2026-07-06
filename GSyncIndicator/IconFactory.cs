using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace GSyncIndicator;

/// <summary>
/// Draws tray icons at runtime (no binary assets to ship). One icon is cached per
/// <see cref="GSyncState"/>; the native HICON handles are destroyed on <see cref="Dispose"/>.
/// </summary>
internal sealed class IconFactory : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private readonly Dictionary<GSyncState, (Icon icon, IntPtr handle)> _cache = new();

    public Icon Get(GSyncState state)
    {
        if (_cache.TryGetValue(state, out var cached))
            return cached.icon;

        (Color fill, bool ring) = state switch
        {
            GSyncState.Active => (Color.FromArgb(46, 204, 64),   true),   // green
            GSyncState.Ready  => (Color.FromArgb(255, 176, 0),   false),  // amber
            GSyncState.Off    => (Color.FromArgb(128, 128, 128), false),  // gray
            _                 => (Color.FromArgb(90, 90, 90),    false),  // dim gray
        };

        var (icon, handle) = Build(fill, ring, muted: state is GSyncState.Off or GSyncState.Unavailable);
        _cache[state] = (icon, handle);
        return icon;
    }

    private static (Icon, IntPtr) Build(Color fill, bool ring, bool muted)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var rect = new Rectangle(4, 4, size - 8, size - 8);

            // Soft radial fill for a bit of depth.
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(rect);
                using var brush = new PathGradientBrush(path)
                {
                    CenterColor = Lighten(fill, 0.25f),
                    SurroundColors = new[] { fill }
                };
                g.FillEllipse(brush, rect);
            }

            // Border.
            using (var pen = new Pen(Darken(fill, muted ? 0.15f : 0.35f), 2f))
                g.DrawEllipse(pen, rect);

            // A pulsing ring for the active state to make it pop in the tray.
            if (ring)
            {
                using var glow = new Pen(Color.FromArgb(140, Lighten(fill, 0.5f)), 2f);
                g.DrawEllipse(glow, Rectangle.Inflate(rect, 2, 2));
            }
        }

        IntPtr handle = bmp.GetHicon();
        // Clone so the managed Icon owns its own copy and stays valid after we keep the handle.
        Icon icon = (Icon)Icon.FromHandle(handle).Clone();
        return (icon, handle);
    }

    private static Color Lighten(Color c, float amt) => Color.FromArgb(
        c.A,
        (int)Math.Min(255, c.R + (255 - c.R) * amt),
        (int)Math.Min(255, c.G + (255 - c.G) * amt),
        (int)Math.Min(255, c.B + (255 - c.B) * amt));

    private static Color Darken(Color c, float amt) => Color.FromArgb(
        c.A,
        (int)(c.R * (1 - amt)),
        (int)(c.G * (1 - amt)),
        (int)(c.B * (1 - amt)));

    public void Dispose()
    {
        foreach (var (icon, handle) in _cache.Values)
        {
            icon.Dispose();
            if (handle != IntPtr.Zero) DestroyIcon(handle);
        }
        _cache.Clear();
    }
}
