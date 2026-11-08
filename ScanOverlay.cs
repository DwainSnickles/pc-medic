using System.Drawing.Drawing2D;

namespace PC_Medic;

/// <summary>
/// Centred loading plate for the scan pages. It sits over the findings area while a pass runs and
/// reports the percentage with a filling bar, so an empty card reads as work in progress.
/// </summary>
internal sealed class ScanOverlay : SkinnedControl
{
    private readonly System.Windows.Forms.Timer _pulse = new() { Interval = 250 };
    private int _percent;
    private bool _pulsing;

    public ScanOverlay()
    {
        BackColor = Theme.Surface;
        _pulse.Tick += OnPulse;
    }

    /// <summary>A real percentage stops the estimate: whoever reports it knows better.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Percent
    {
        get => _percent;
        set
        {
            _pulsing = false;
            _pulse.Stop();
            SetPercent(value);
        }
    }

    /// <summary>
    /// Shows the plate for work that reports nothing of its own — an external process such as winget —
    /// easing the number toward a ceiling so the bar never claims completion before the work ends.
    /// </summary>
    public void Begin(int percent = 4)
    {
        _percent = Math.Clamp(percent, 0, 100);
        _pulsing = true;
        _pulse.Start();
        Visible = true;
        BringToFront();
        Invalidate();
    }

    public void End()
    {
        _pulsing = false;
        _pulse.Stop();
        Visible = false;
    }

    private void SetPercent(int value)
    {
        int clamped = Math.Clamp(value, 0, 100);
        if (_percent == clamped) return;
        _percent = clamped;
        Invalidate();
    }

    private void OnPulse(object? sender, EventArgs e)
    {
        if (!_pulsing || _percent >= 90) return;

        int next = _percent + Math.Max(1, (int)((90 - _percent) * 0.04));
        SetPercent(Math.Min(next, 90));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _pulse.Dispose();
        base.Dispose(disposing);
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
