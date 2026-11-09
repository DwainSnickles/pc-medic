using System.Drawing.Drawing2D;

namespace PC_Medic;

/// <summary>Small dark dialog for checking, downloading and installing a new PC Medic build.</summary>
internal sealed class SelfUpdateForm : Form
{
    private const int WmNcHitTest = 0x0084;
    private const int HtCaption = 2;
    private const int HtClient = 1;

    private readonly Label _currentValue = new();
    private readonly Label _latestValue = new();
    private readonly Label _notes = new();
    private readonly Label _status = new();
    private readonly NeonButton _check = new() { Text = "CHECK AGAIN", Size = new Size(132, 32) };
    private readonly NeonButton _install = new() { Text = "DOWNLOAD & INSTALL", Size = new Size(186, 32), Primary = true };
    private readonly NeonButton _close = new() { Text = "CLOSE", Size = new Size(96, 32) };

    private readonly ScanOverlay _loading = new() { Visible = false };
    private SelfUpdateInfo? _info;
    private bool _busy;

    public SelfUpdateForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 372);
        BackColor = Theme.Bg;
        Font = Theme.Ui;
        ForeColor = Theme.Text;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        KeyPreview = true;

        Controls.Add(MakeLabel("PC MEDIC \u2022 SELF UPDATE", Theme.Kicker, Theme.Alpha(Theme.Accent, 215), new Point(30, 30)));
        Controls.Add(MakeLabel("SOFTWARE UPDATE", new Font(Theme.H2.FontFamily, 15f, FontStyle.Bold), Theme.Text, new Point(30, 48)));

        Controls.Add(MakeLabel("CURRENT BUILD", Theme.Kicker, Theme.TextDim, new Point(30, 88)));
        Controls.Add(MakeLabel("LATEST RELEASE", Theme.Kicker, Theme.TextDim, new Point(300, 88)));

        Panel currentChip = Well(30, 104, 240, 30, 6f);
        Panel latestChip = Well(300, 104, 230, 30, 6f);
        Panel notesWell = Well(24, 146, ClientSize.Width - 48, 106, 8f);

        _currentValue.Text = UpdateService.CurrentVersion;
        _currentValue.Font = Theme.Mono;
        _currentValue.ForeColor = Theme.Text;
        _currentValue.BackColor = Color.Transparent;
        _currentValue.AutoSize = false;
        _currentValue.SetBounds(10, 5, 220, 20);

        _latestValue.Text = "\u2014";
        _latestValue.Font = Theme.Mono;
        _latestValue.ForeColor = Theme.Text;
        _latestValue.BackColor = Color.Transparent;
        _latestValue.AutoSize = false;
        _latestValue.SetBounds(12, 5, 200, 20);

        _notes.Text = string.Empty;
        _notes.Font = Theme.UiSmall;
        _notes.ForeColor = Theme.TextDim;
        _notes.BackColor = Color.Transparent;
        _notes.AutoSize = false;
        _notes.SetBounds(14, 10, notesWell.Width - 28, notesWell.Height - 20);

        _status.Text = "Ready.";
        _status.Font = Theme.MonoSmall;
        _status.ForeColor = Theme.TextDim;
        _status.AutoSize = false;
        _status.SetBounds(30, 258, 500, 34);

        currentChip.Controls.Add(_currentValue);
        latestChip.Controls.Add(_latestValue);
        notesWell.Controls.Add(_notes);

        Controls.Add(currentChip);
        Controls.Add(latestChip);
        Controls.Add(notesWell);
        Controls.Add(_status);

        foreach (NeonButton button in new[] { _check, _install, _close })
        {
            Controls.Add(button);
        }

        // Covers the version readouts and notes while work runs; the status line and buttons stay visible.
        Controls.Add(_loading);
        _loading.SetBounds(12, 88, ClientSize.Width - 24, 164);
        _loading.BringToFront();

        _check.SetBounds(30, 316, _check.Width, _check.Height);
        _close.SetBounds(ClientSize.Width - 30 - _close.Width, 316, _close.Width, _close.Height);
        _install.SetBounds(_close.Left - _install.Width - 10, 316, _install.Width, _install.Height);

        _install.Enabled = false;

        _check.Click += async (_, _) => await CheckAsync();
        _install.Click += async (_, _) => await InstallAsync();
        _close.Click += (_, _) => Close();

        _notes.Text = "Release notes appear here once a build is published.";
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _ = CheckAsync();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode == Keys.Escape && !_busy) Close();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg == WmNcHitTest && (int)m.Result == HtClient) m.Result = HtCaption;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // The window edge gets the same heavy accent outline as the wells inside it, so the dialog reads
        // as a form on the desktop instead of a floating block of text.
        var frame = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
        Theme.PaintSurface(g, frame, 12f, Theme.Surface, Theme.Alpha(Theme.Accent, 215), Theme.Accent);

        using GraphicsPath outline = RoundPath(frame, 12f);
        using var edge = new Pen(Theme.Alpha(Theme.Accent, 215), 2f);
        g.DrawPath(edge, outline);
    }

    /// <summary>
    /// A filled, outlined well for one block of the dialog. Without the edge these blocks sit on the same
    /// near-black as the form and their boundaries disappear.
    /// </summary>
    private static Panel Well(int x, int y, int width, int height, float radius)
    {
        var well = new Panel { BackColor = Theme.Bg };
        well.SetBounds(x, y, width, height);
        well.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath path = RoundPath(new RectangleF(1f, 1f, well.Width - 2f, well.Height - 2f), radius);
            using (var fill = new SolidBrush(Theme.Surface2)) e.Graphics.FillPath(fill, path);
            using var edge = new Pen(Theme.Alpha(Theme.Accent, 215), 2f);
            e.Graphics.DrawPath(edge, path);
        };
        return well;
    }

    /// <summary>Rounded rectangle, so the well's fill and its heavy border follow the same outline.</summary>
    private static GraphicsPath RoundPath(RectangleF rect, float radius)
    {
        float diameter = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Label MakeLabel(string text, Font font, Color color, Point location) => new()
    {
        Text = text,
        Font = font,
        ForeColor = color,
        BackColor = Color.Transparent,
        AutoSize = true,
        Location = location,
    };

    private async Task CheckAsync()
    {
        if (_busy) return;

        _busy = true;
        _check.Enabled = false;
        _install.Enabled = false;
        SetStatus($"Checking {UpdateService.FeedHost}\u2026", Theme.TextDim);
        using IDisposable busy = Busy.Enter(this);
        using IDisposable plate = _loading.Busy();

        SelfUpdateInfo info = await UpdateService.CheckAsync();
        if (IsDisposed) return;

        _info = info;
        _busy = false;
        _check.Enabled = true;

        _currentValue.Text = info.CurrentVersion;
        _latestValue.Text = info.LatestVersion.Length == 0 ? "\u2014" : info.LatestVersion;
        _install.Enabled = info.Available;

        _notes.Text = string.IsNullOrWhiteSpace(info.Notes)
            ? "No release notes for this build."
            : Theme.Truncate(info.Notes.Replace("\r", " ").Replace("\n", "  "), 260);

        SetStatus(info.Message, info.Available ? Theme.Warn : info.Checked ? Theme.Good : Theme.Bad);
        ActivityLog.Write(info.Available ? LogLevel.Info : LogLevel.Success, $"Self-update: {info.Message}");
    }

    private async Task InstallAsync()
    {
        if (_busy || _info is not { Available: true } info) return;

        _busy = true;
        _check.Enabled = false;
        _install.Enabled = false;
        SetStatus("Downloading the package\u2026", Theme.TextDim);
        using IDisposable busy = Busy.Enter(this);
        using IDisposable plate = _loading.Busy();

        var progress = new Progress<string>(text => SetStatus($"Downloading {text}", Theme.TextDim));
        (bool ok, string path, string message) = await UpdateService.DownloadAsync(info, progress);
        if (IsDisposed) return;

        if (!ok)
        {
            _busy = false;
            _check.Enabled = true;
            SetStatus($"Download failed: {message}", Theme.Bad);
            ActivityLog.Error($"Self-update download failed: {message}");
            return;
        }

        ActivityLog.Success($"Downloaded PC Medic {info.LatestVersion} ({Theme.Truncate(Path.GetFileName(path), 40)}).");
        SetStatus("Installing \u2014 the console will restart.", Theme.Good);

        (bool installed, string installMessage) = UpdateService.Install(path);
        ActivityLog.Write(installed ? LogLevel.Success : LogLevel.Error, installMessage);

        if (!installed)
        {
            _busy = false;
            _check.Enabled = true;
            SetStatus($"Install failed: {installMessage}", Theme.Bad);
            return;
        }

        await Task.Delay(700);
        Application.Exit();
    }

    private void SetStatus(string text, Color tone)
    {
        _status.Text = text;
        _status.ForeColor = tone;
    }
}
