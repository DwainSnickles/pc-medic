using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PC_Medic;

/// <summary>
/// The PC Medic mark: a cyan instrument plate with a diagnostic pulse trace.
/// Drawn in code so it stays crisp at any size and needs no image asset.
/// </summary>
internal static class Brand
{
    // Normalised pulse trace: flat line, sharp spike, settle, flat line.
    private static readonly PointF[] Pulse =
    {
        new(0.20f, 0.55f),
        new(0.33f, 0.55f),
        new(0.42f, 0.33f),
        new(0.54f, 0.74f),
        new(0.63f, 0.55f),
        new(0.80f, 0.55f),
    };

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>Builds a window/taskbar icon from the vector mark, so no .ico asset is required.</summary>
    public static Icon CreateIcon(int size = 64)
    {
        using var bitmap = new Bitmap(size, size);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            PaintMark(g, new RectangleF(1f, 1f, size - 2f, size - 2f));
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public static void PaintMark(Graphics g, RectangleF bounds)
    {
        float size = Math.Min(bounds.Width, bounds.Height);
        if (size < 6) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;

        float x = bounds.X + ((bounds.Width - size) / 2f);
        float y = bounds.Y + ((bounds.Height - size) / 2f);
        var plate = new RectangleF(x + (size * 0.05f), y + (size * 0.05f), size * 0.9f, size * 0.9f);

        using (GraphicsPath platePath = Theme.Round(plate, size * 0.19f))
        {
            using (var fill = new LinearGradientBrush(
                       plate,
                       Color.FromArgb(19, 30, 41),
                       Color.FromArgb(6, 9, 14),
                       90f))
            {
                g.FillPath(fill, platePath);
            }

            using (var edge = new Pen(Theme.Alpha(Theme.Accent, 215), Math.Max(1f, size * 0.05f)))
            {
                g.DrawPath(edge, platePath);
            }
        }

        var points = new PointF[Pulse.Length];
        for (int i = 0; i < Pulse.Length; i++)
        {
            points[i] = new PointF(
                plate.X + (Pulse[i].X * plate.Width),
                plate.Y + (Pulse[i].Y * plate.Height));
        }

        using (var trace = new Pen(Theme.Accent, Math.Max(1.2f, size * 0.062f)))
        {
            trace.LineJoin = LineJoin.Round;
            trace.StartCap = LineCap.Round;
            trace.EndCap = LineCap.Round;
            g.DrawLines(trace, points);
        }

        using var node = new SolidBrush(Theme.Alpha(Theme.Accent, 200));
        float dot = Math.Max(1.6f, size * 0.055f);
        g.FillEllipse(node, points[0].X - (dot / 2f), points[0].Y - (dot / 2f), dot, dot);
        g.FillEllipse(node, points[^1].X - (dot / 2f), points[^1].Y - (dot / 2f), dot, dot);
    }
}

/// <summary>Sidebar logo plate. Paints the brand mark only; no image files involved.</summary>
internal sealed class BrandMark : SkinnedControl
{
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 6 || Height < 6) return;
        Brand.PaintMark(e.Graphics, new RectangleF(0f, 0f, Width, Height));
    }
}
