using System.Drawing.Drawing2D;

namespace PC_Medic;

/// <summary>
/// Central visual language for PC Medic: deep slate surfaces, cyan instrumentation,
/// monospace telemetry type and small-caps kickers.
/// </summary>
internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(7, 10, 15);
    public static readonly Color Surface = Color.FromArgb(13, 18, 26);
    public static readonly Color Surface2 = Color.FromArgb(18, 25, 35);
    public static readonly Color Border = Color.FromArgb(30, 42, 56);
    public static readonly Color BorderSoft = Color.FromArgb(22, 31, 42);

    public static readonly Color Accent = Color.FromArgb(34, 211, 238);
    public static readonly Color AccentDeep = Color.FromArgb(12, 74, 92);
    public static readonly Color Good = Color.FromArgb(52, 211, 153);
    public static readonly Color Warn = Color.FromArgb(250, 191, 76);
    public static readonly Color Bad = Color.FromArgb(248, 113, 113);
    public static readonly Color Violet = Color.FromArgb(167, 139, 250);

    public static readonly Color Text = Color.FromArgb(230, 239, 248);
    public static readonly Color TextDim = Color.FromArgb(130, 149, 168);
    public static readonly Color TextFaint = Color.FromArgb(82, 99, 116);

    /// <summary>Resting outline for cards and controls: the accent held back, so it frames without shouting.</summary>
    public static readonly Color Edge = Alpha(Accent, 110);

    public static readonly Font Ui;
    public static readonly Font UiBold;
    public static readonly Font UiSmall;
    public static readonly Font Mono;
    public static readonly Font MonoSmall;
    public static readonly Font Numeric;
    public static readonly Font Kicker;
    public static readonly Font H1;
    public static readonly Font H2;
    public static readonly Font Icon;

    static Theme()
    {
        string ui = FirstAvailable("Segoe UI Variable Text", "Segoe UI", "Tahoma");
        string mono = FirstAvailable("Cascadia Mono", "Consolas", "Lucida Console");
        string icon = FirstAvailable("Segoe Fluent Icons", "Segoe MDL2 Assets", ui);

        Ui = new Font(ui, 9.75f);
        UiBold = new Font(ui, 9.75f, FontStyle.Bold);
        UiSmall = new Font(ui, 8.5f);
        Mono = new Font(mono, 9f);
        MonoSmall = new Font(mono, 8.25f);
        Numeric = new Font(mono, 15f, FontStyle.Bold);
        Kicker = new Font(mono, 7.5f, FontStyle.Bold);
        H1 = new Font(ui, 19f, FontStyle.Bold);
        H2 = new Font(ui, 12.5f, FontStyle.Bold);
        Icon = new Font(icon, 11.5f);
    }

    public static bool MonoAvailable => Mono.Name != "Microsoft Sans Serif";

    public static string FirstAvailable(params string[] names)
    {
        FontFamily[] families = FontFamily.Families;
        foreach (string name in names)
        {
            foreach (FontFamily family in families)
            {
                if (string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return family.Name;
                }
            }
        }

        return "Microsoft Sans Serif";
    }

    public static Color Alpha(Color color, int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), color);

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
        if (d <= 1f || r.Width <= 0 || r.Height <= 0)
        {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.X, r.Y, d, d, 180f, 90f);
        path.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
        path.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    /// <summary>Filled instrument panel with hairline border and a short accent tick.</summary>
    public static void PaintSurface(Graphics g, RectangleF rect, float radius, Color fill, Color border, Color? tick = null)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = Round(rect, radius);
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
        }

        using (var pen = new Pen(border, 1f))
        {
            g.DrawPath(pen, path);
        }

        if (tick is { } tickColor)
        {
            using var tickPen = new Pen(Alpha(tickColor, 170), 2f);
            g.DrawLine(tickPen, rect.X + 16f, rect.Y + 0.5f, rect.X + 58f, rect.Y + 0.5f);
        }
    }

    public static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? string.Empty;
        return string.Concat(text.AsSpan(0, Math.Max(0, max - 1)), "\u2026");
    }

    public static string Bytes(ulong bytes)
    {
        double value = bytes;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit >= 3 ? $"{value:0.0} {units[unit]}" : $"{value:0} {units[unit]}";
    }

    public static string Duration(TimeSpan span)
    {
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes:00}m";
        return $"{span.Minutes:00}m {span.Seconds:00}s";
    }
}

/// <summary>Dark colour table so context menus match the app instead of using system light chrome.</summary>
internal sealed class DarkMenuColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Theme.Surface2;

    public override Color ImageMarginGradientBegin => Theme.Surface2;

    public override Color ImageMarginGradientMiddle => Theme.Surface2;

    public override Color ImageMarginGradientEnd => Theme.Surface2;

    public override Color MenuBorder => Theme.Border;

    public override Color MenuItemBorder => Theme.Alpha(Theme.Accent, 120);

    public override Color MenuItemSelected => Theme.Alpha(Theme.Accent, 30);

    public override Color MenuItemSelectedGradientBegin => Theme.Alpha(Theme.Accent, 30);

    public override Color MenuItemSelectedGradientEnd => Theme.Alpha(Theme.Accent, 30);

    public override Color SeparatorDark => Theme.Border;

    public override Color SeparatorLight => Theme.Border;
}
