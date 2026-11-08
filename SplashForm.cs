using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace PC_Medic;

/// <summary>
/// Brief launch card: the brand plate, wordmark and an indeterminate boot sweep.
/// Painted in code, sized to the mark, and closed by the caller once startup is done.
/// </summary>
internal sealed class SplashForm : Form
{
    private const int BootMilliseconds = 1150;
    private const int CardWidth = 412;
    private const int CardHeight = 190;

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private float _progress;

    public SplashForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(CardWidth, CardHeight);
        BackColor = Theme.Bg;
        Font = Theme.Ui;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;

        try
        {
            Icon = Brand.CreateIcon(64);
        }
        catch
        {
            // The mark is cosmetic; a missing icon must never block launch.
        }

        _timer.Tick += (_, _) =>
        {
            _progress = Math.Min(1f, (float)(_clock.Elapsed.TotalMilliseconds / BootMilliseconds));
            Invalidate();
        };
        _timer.Start();
    }

    /// <summary>True once the minimum boot window has elapsed.</summary>
    public bool Finished => _progress >= 1f;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var card = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        Theme.PaintSurface(g, card, 12f, Theme.Surface, Theme.Border, Theme.Accent);

        Brand.PaintMark(g, new RectangleF(30f, 46f, 60f, 60f));

        TextRenderer.DrawText(g, "PC MEDIC", Theme.H1, new Point(106, 50), Theme.Text, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(
            g,
            "SYSTEM OPTIMIZER",
            Theme.Kicker,
            new Point(108, 84),
            Theme.Alpha(Theme.Accent, 215),
            TextFormatFlags.NoPrefix);

        using (var rule = new Pen(Theme.BorderSoft))
        {
            g.DrawLine(rule, 30, 124, Width - 30, 124);
        }

        TextRenderer.DrawText(
            g,
            _progress >= 1f ? "CONSOLE READY" : "INITIALIZING TELEMETRY",
            Theme.Kicker,
            new Point(30, 136),
            _progress >= 1f ? Theme.Good : Theme.TextDim,
            TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(
            g,
            "PORTABLE BUILD 1.0",
            Theme.Kicker,
            new Rectangle(30, 136, Width - 60, 14),
            Theme.TextDim,
            TextFormatFlags.Right | TextFormatFlags.NoPrefix);

        var track = new RectangleF(30f, Height - 30f, Width - 60f, 3f);
        using (var backdrop = new SolidBrush(Theme.BorderSoft))
        {
            g.FillRectangle(backdrop, track);
        }

        float sweep = Math.Max(0.02f, _progress);
        var fill = new RectangleF(track.X, track.Y, track.Width * sweep, track.Height);
        using (var brush = new LinearGradientBrush(
                   new RectangleF(track.X, track.Y, Math.Max(2f, track.Width * sweep), track.Height),
                   Theme.Alpha(Theme.AccentDeep, 255),
                   Theme.Accent,
                   0f))
        {
            g.FillRectangle(brush, fill);
        }

        using (var head = new SolidBrush(Theme.Accent))
        {
            g.FillEllipse(head, fill.Right - 2f, track.Y - 1.5f, 6f, 6f);
        }

        if (_progress >= 1f)
        {
            _timer.Stop();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}
