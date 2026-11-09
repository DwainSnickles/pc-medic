using System.Drawing.Drawing2D;

namespace PC_Medic;

/// <summary>
/// Batch setup for the missing benchmark rigs. Every rig gets its own bar: filled from winget's own
/// transfer percentage when winget reports one, sweeping when it does not. When the last package is
/// done the page behind re-detects the suite, so installed rigs stop reading as missing.
/// </summary>
internal sealed class RigSetupForm : Form
{
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtCaption = 2;
    private const int RowHeight = 62;

    private readonly BufferedPanel _list = new();
    private readonly Label _status = new();
    private readonly NeonButton _action = new() { Text = "CANCEL", Size = new Size(116, 32) };
    private readonly List<(RigProgressRow Row, PortableApp App)> _rows = new();
    private readonly System.Windows.Forms.Timer _sweep = new() { Interval = 40 };
    private readonly CancellationTokenSource _cancel = new();
    private bool _running;
    private int _installed;
    private readonly List<PortableApp> _needsSearch = new();

    /// <summary>True when at least one rig landed, so the page knows to re-detect everything.</summary>
    public bool InstalledAnything => _installed > 0;

    /// <summary>
    /// Rigs winget reported as installed but whose executable could not be found. The page offers a
    /// search for these instead of the install link, since downloading again would not help.
    /// </summary>
    public IReadOnlyList<PortableApp> NeedsSearch => _needsSearch;

    public RigSetupForm(IReadOnlyList<PortableApp> missing)
    {
        int listHeight = Math.Max(70, (missing.Count * (RowHeight + 6)) + 4);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(680, 100 + listHeight + 114);
        BackColor = Theme.Bg;
        Font = Theme.Ui;
        ForeColor = Theme.Text;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        KeyPreview = true;

        Controls.Add(MakeLabel("PC MEDIC \u2022 BENCHMARK RIGS", Theme.Kicker, Theme.Alpha(Theme.Accent, 215), new Point(28, 26)));
        Controls.Add(MakeLabel("INSTALL MISSING RIGS", new Font(Theme.H2.FontFamily, 15f, FontStyle.Bold), Theme.Text, new Point(28, 44)));
        Controls.Add(MakeLabel(
            "winget downloads each vendor package and verifies its hash \u2014 one bar per rig.",
            Theme.UiSmall,
            Theme.TextDim,
            new Point(28, 76)));

        _list.BackColor = Theme.Surface;
        _list.AutoScroll = true;
        _list.SetBounds(20, 100, ClientSize.Width - 40, listHeight);
        Controls.Add(_list);

        int y = 4;
        foreach (PortableApp app in missing)
        {
            var row = new RigProgressRow
            {
                Title = app.Title,
                Note = app.Note,
                Tone = Theme.Accent,
            };

            row.SetBounds(4, y, Math.Max(300, _list.ClientSize.Width - 12), RowHeight);
            _list.Controls.Add(row);
            _rows.Add((row, app));
            y += RowHeight + 6;
        }

        _list.AutoScrollMinSize = new Size(0, y + 4);
        _list.Resize += (_, _) =>
        {
            foreach (Control control in _list.Controls)
            {
                if (control is RigProgressRow row) row.Width = Math.Max(300, _list.ClientSize.Width - 12);
            }
        };

        _status.Text = "READY TO INSTALL";
        _status.Font = Theme.MonoSmall;
        _status.ForeColor = Theme.TextDim;
        _status.BackColor = Color.Transparent;
        _status.AutoSize = false;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.SetBounds(28, _list.Bottom + 8, ClientSize.Width - 200, 30);
        Controls.Add(_status);

        Controls.Add(_action);
        _action.SetBounds(
            ClientSize.Width - 28 - _action.Width,
            ClientSize.Height - 28 - _action.Height,
            _action.Width,
            _action.Height);

        _action.Click += (_, _) =>
        {
            if (!_running)
            {
                Close();
                return;
            }

            _action.Enabled = false;
            _cancel.Cancel();
            SetStatus("CANCELLING \u2014 STOPPING WINGET\u2026", Theme.Warn);
        };

        _sweep.Tick += (_, _) =>
        {
            foreach ((RigProgressRow row, PortableApp _) in _rows) row.Advance();
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _ = RunAsync();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode == Keys.Escape && !_running) Close();
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
        Theme.PaintSurface(g, new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 12f, Theme.Surface, Theme.Alpha(Theme.Accent, 170), Theme.Accent);
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

    private void SetStatus(string text, Color tone)
    {
        _status.Text = text.ToUpperInvariant();
        _status.ForeColor = tone;
    }

    private async Task RunAsync()
    {
        _running = true;
        _sweep.Start();
        int index = 0;

        try
        {
            foreach ((RigProgressRow row, PortableApp app) in _rows)
            {
                index++;

                if (_cancel.IsCancellationRequested)
                {
                    row.Finish(false, "CANCELLED", "Not started.");
                    continue;
                }

                row.Begin();
                SetStatus($"({index}/{_rows.Count}) INSTALLING {app.Title}\u2026", Theme.Accent);

                var progress = new Progress<int>(value =>
                {
                    if (!IsDisposed) row.Report(value);
                });

                RigInstallResult result = await RigInstaller.InstallAsync(app, progress, _cancel.Token);

                if (IsDisposed) return;

                row.Finish(result.Ok, result.Ok ? "READY" : "FAILED", result.Message);

                if (result.Ok)
                {
                    _installed++;
                    ActivityLog.Success($"{app.Title}: {result.Message}");
                }
                else
                {
                    if (result.Unlocated) _needsSearch.Add(app);
                    ActivityLog.Error($"{app.Title}: {result.Message}");
                }
            }

            string suffix = _needsSearch.Count == 0
                ? string.Empty
                : $"  \u2022  {_needsSearch.Count} NEED A SEARCH";

            SetStatus(
                _cancel.IsCancellationRequested
                    ? $"CANCELLED \u2014 {_installed} OF {_rows.Count} RIGS INSTALLED{suffix}"
                    : $"FINISHED \u2014 {_installed} OF {_rows.Count} RIGS INSTALLED{suffix}",
                _installed == _rows.Count ? Theme.Good : Theme.Warn);
        }
        finally
        {
            _sweep.Stop();
            _running = false;

            if (!IsDisposed)
            {
                _action.Text = "CLOSE";
                _action.Enabled = true;
            }
        }
    }
}

/// <summary>One rig's line in the setup dialog: name, state, message and its own progress track.</summary>
internal sealed class RigProgressRow : SkinnedControl
{
    private string _state = "PENDING";
    private string _message = "Waiting for its turn.";
    private Color _tone = Theme.TextDim;
    private int _percent;
    private bool _running;
    private float _phase;

    public RigProgressRow()
    {
        Height = 62;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; } = string.Empty;

    /// <summary>The one-line context under the name, before a result replaces it.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Note
    {
        get => _message;
        set => _message = value;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Tone { get; set; } = Theme.Accent;

    public void Begin()
    {
        _running = true;
        _percent = 0;
        _state = "DOWNLOADING";
        _message = "Asking winget for the package\u2026";
        _tone = Tone;
        Invalidate();
    }

    /// <summary>Starts a search line: a sweeping bar with no percentage, until a hit or the walk ends.</summary>
    public void Search(string message)
    {
        _running = true;
        _percent = 0;
        _state = "SEARCHING";
        _message = message;
        _tone = Tone;
        Invalidate();
    }

    /// <summary>A percentage from winget turns the sweep into a filling bar; zero keeps it sweeping.</summary>
    public void Report(int percent)
    {
        if (!_running || percent <= 0) return;
        if (percent == _percent) return;

        _percent = Math.Clamp(percent, 0, 100);
        _message = $"TRANSFER {_percent}%";
        Invalidate();
    }

    public void Advance()
    {
        if (!_running) return;

        _phase += 0.024f;
        if (_phase > 1f) _phase -= 1f;
        Invalidate();
    }

    public void Finish(bool ok, string state, string message)
    {
        _running = false;
        _state = state;
        _message = message;
        _tone = ok ? Theme.Good : Theme.Bad;
        if (ok) _percent = 100;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 140 || Height < 30) return;

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.PaintSurface(
            g,
            new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f),
            8f,
            Theme.Surface2,
            Theme.BorderSoft,
            null);

        TextRenderer.DrawText(
            g,
            Title,
            Theme.UiBold,
            new Rectangle(14, 8, Math.Max(40, Width - 150), 18),
            Theme.Text,
            TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(
            g,
            _state,
            Theme.Kicker,
            new Rectangle(Width - 132, 8, 118, 18),
            _tone,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(
            g,
            _message,
            Theme.MonoSmall,
            new Rectangle(14, 28, Math.Max(40, Width - 28), 16),
            _tone == Theme.Bad ? Theme.Bad : Theme.TextDim,
            Labeled);

        var track = new RectangleF(14, Height - 14, Width - 28, 5);
        using (GraphicsPath path = Theme.Round(track, 2.5f))
        {
            using var off = new SolidBrush(Theme.BorderSoft);
            g.FillPath(off, path);
        }

        if (_percent <= 0 && !_running) return;

        RectangleF lit;

        if (_percent > 0)
        {
            lit = new RectangleF(track.X, track.Y, Math.Max(3f, track.Width * (_percent / 100f)), track.Height);
        }
        else
        {
            float segment = Math.Max(50f, track.Width * 0.3f);
            float x = -segment + (_phase * (track.Width + segment));
            float left = Math.Max(track.X, x);
            float right = Math.Min(track.Right, x + segment);
            if (right <= left) return;

            lit = new RectangleF(left, track.Y, right - left, track.Height);
        }

        using GraphicsPath litPath = Theme.Round(lit, 2.5f);
        using var brush = new SolidBrush(_running && _percent == 0 ? Theme.Accent : _percent > 0 ? Theme.Good : _tone);
        g.FillPath(brush, litPath);
    }
}
