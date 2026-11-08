using System.Drawing.Drawing2D;

namespace PC_Medic;

/// <summary>
/// Centred loading plate for the scan pages. It sits over the findings card while a pass runs and
/// reports the percentage with a filling bar, so an empty card reads as work in progress.
/// </summary>
internal sealed class ScanOverlay : SkinnedControl
{
    private int _percent;

    public ScanOverlay()
    {
        BackColor = Theme.Surface;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Percent
    {
        get => _percent;
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            if (_percent == clamped) return;
            _percent = clamped;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 120 || Height < 80) return;

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var plate = new SolidBrush(BackColor))
        {
            g.FillRectangle(plate, ClientRectangle);
        }

        const int LineHeight = 34;
        const int Gap = 20;
        const int BarHeight = 8;

        int top = (Height - (LineHeight + Gap + BarHeight)) / 2;

        // Two runs of one line: the label stays quiet, the number carries the accent.
        const string Caption = "LOADING ";
        string value = $"{_percent}%";
        Size captionSize = TextRenderer.MeasureText(Caption, Theme.Numeric);
        Size valueSize = TextRenderer.MeasureText(value, Theme.Numeric);
        int left = (Width - captionSize.Width - valueSize.Width) / 2;

        TextRenderer.DrawText(
            g,
            Caption,
            Theme.Numeric,
            new Rectangle(left, top, captionSize.Width + 6, LineHeight),
            Theme.TextDim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(
            g,
            value,
            Theme.Numeric,
            new Rectangle(left + captionSize.Width, top, valueSize.Width + 6, LineHeight),
            Theme.Accent,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        int barWidth = Math.Clamp(Width - 120, 160, 360);
        var bar = new RectangleF((Width - barWidth) / 2f, top + LineHeight + Gap, barWidth, BarHeight);

        using (GraphicsPath track = Theme.Round(bar, BarHeight / 2f))
        {
            using var fill = new SolidBrush(Theme.BorderSoft);
            using var edge = new Pen(Theme.Border);
            g.FillPath(fill, track);
            g.DrawPath(edge, track);
        }

        if (_percent <= 0) return;

        var filled = new RectangleF(bar.X, bar.Y, Math.Max(BarHeight, bar.Width * (_percent / 100f)), bar.Height);

        using GraphicsPath progress = Theme.Round(filled, BarHeight / 2f);
        using var brush = new LinearGradientBrush(
            filled,
            Theme.Alpha(Theme.Accent, 120),
            Theme.Accent,
            LinearGradientMode.Horizontal);
        g.FillPath(brush, progress);
    }
}
