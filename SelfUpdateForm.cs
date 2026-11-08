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

        Controls.Add(MakeLabel("CURRENT BUILD", Theme.Kicker, Theme.TextDim, new Point(30, 96)));
        Controls.Add(MakeLabel("LATEST RELEASE", Theme.Kicker, Theme.TextDim, new Point(300, 96)));

        _currentValue.Text = UpdateService.CurrentVersion;
        _currentValue.Font = Theme.Mono;
        _currentValue.ForeColor = Theme.Text;
        _currentValue.AutoSize = false;
        _currentValue.SetBounds(30, 112, 240, 22);

        _latestValue.Text = "\u2014";
        _latestValue.Font = Theme.Mono;
        _latestValue.ForeColor = Theme.Text;
        _latestValue.AutoSize = false;
        _latestValue.SetBounds(300, 112, 230, 22);

        _notes.Text = string.Empty;
        _notes.Font = Theme.UiSmall;
        _notes.ForeColor = Theme.TextDim;
        _notes.AutoSize = false;
        _notes.SetBounds(30, 152, 500, 96);

        _status.Text = "Ready.";
        _status.Font = Theme.MonoSmall;
        _status.ForeColor = Theme.TextDim;
        _status.AutoSize = false;
        _status.SetBounds(30, 258, 500, 34);

        Controls.Add(_currentValue);
        Controls.Add(_latestValue);
        Controls.Add(_notes);
        Controls.Add(_status);

        foreach (NeonButton button in new[] { _check, _install, _close })
        {
            Controls.Add(button);
        }

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
        Theme.PaintSurface(g, new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 12f, Theme.Surface, Theme.Border, Theme.Accent);
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
