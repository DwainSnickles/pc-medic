using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace PC_Medic;

/// <summary>
/// Thin, hand-painted progress strip shown while a scan runs. Adapted from the gradient bar style
/// used elsewhere, but drawn with the suite palette so it sits inside the instrument panel rather
/// than on top of it.
/// </summary>
internal sealed class ScanProgressBar : ProgressBar
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Tone { get; set; } = Theme.Accent;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color TrackColor { get; set; } = Theme.Surface;

    /// <summary>Assigning repaints: the stock setter never asks for one.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new int Value
    {
        get => base.Value;
        set
        {
            int clamped = Math.Clamp(value, Minimum, Maximum);
            if (base.Value == clamped) return;
            base.Value = clamped;
            Invalidate();
        }
    }

    public ScanProgressBar()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        Minimum = 0;
        Maximum = 100;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 4 || Height < 2) return;

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        float radius = Height / 2f;

        using (GraphicsPath track = Theme.Round(rect, radius))
        {
            using var fill = new SolidBrush(TrackColor);
            using var edge = new Pen(Theme.Border);
            g.FillPath(fill, track);
            g.DrawPath(edge, track);
        }

        float percent = Math.Clamp((Value - Minimum) / (float)Math.Max(1, Maximum - Minimum), 0f, 1f);
        if (percent <= 0f) return;

        var bar = new RectangleF(0.5f, 0.5f, Math.Max(radius * 2f, rect.Width * percent), rect.Height);

        using GraphicsPath progress = Theme.Round(bar, radius);
        using var brush = new LinearGradientBrush(bar, Theme.Alpha(Tone, 120), Tone, LinearGradientMode.Horizontal);
        g.FillPath(brush, progress);
    }
}
