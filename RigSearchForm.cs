using System.Drawing.Drawing2D;

namespace PC_Medic;

/// <summary>
/// Searches the machine for rigs that are not in the portable folder. Each rig gets its own line: it
/// sweeps while the volumes are walked, then lands on FOUND with the executable it matched, or on
/// NOT FOUND. Hits are remembered by path, so the grid behind stops reading them as missing.
/// </summary>
internal sealed class RigSearchForm : Form
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
    private int _found;
    private int _scanned;
    private string _target = "THIS PC";

    /// <summary>True when at least one rig was located, so the page knows to re-detect everything.</summary>
    public bool FoundAnything => _found > 0;

    public RigSearchForm(IReadOnlyList<PortableApp> missing)
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
        Controls.Add(MakeLabel("SEARCH THIS PC", new Font(Theme.H2.FontFamily, 15f, FontStyle.Bold), Theme.Text, new Point(28, 44)));
        Controls.Add(MakeLabel(
            "Walks every drive, plus the registry and winget, for the rig executables \u2014 nothing is installed.",
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

        _status.Text = "READY TO SEARCH";
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
            SetStatus("CANCELLING \u2014 STOPPING THE WALK\u2026", Theme.Warn);
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

    private void SetStatus(string text, Color tone)
    {
        _status.Text = text.ToUpperInvariant();
        _status.ForeColor = tone;
    }

    private async Task RunAsync()
    {
        _running = true;
        _sweep.Start();

        foreach ((RigProgressRow row, PortableApp _) in _rows) row.Search("Looking on every drive\u2026");
        SetStatus("READING REGISTRY AND WINGET\u2026", Theme.Accent);

        var stage = new Progress<string>(target =>
        {
            if (IsDisposed) return;
            _target = target;
            ShowProgress();
        });

        var folders = new Progress<int>(count =>
        {
            if (IsDisposed) return;
            _scanned = count;
            ShowProgress();
        });

        var landed = new Progress<RigSearchHit>(result =>
        {
            if (IsDisposed) return;

            foreach ((RigProgressRow row, PortableApp app) in _rows)
            {
                if (!ReferenceEquals(app, result.App)) continue;
                row.Finish(true, "FOUND", Theme.Truncate(result.Path, 64));
                break;
            }

            ActivityLog.Success($"{result.App.Title}: found at {result.Path}");
        });

        IReadOnlyList<RigSearchHit> hits;

        try
        {
            hits = await RigSearch.RunAsync(
                _rows.Select(row => row.App).ToList(),
                stage,
                folders,
                landed,
                _cancel.Token);
        }
        catch (Exception ex)
        {
            ActivityLog.Error($"Rig search failed: {ex.Message}");
            hits = Array.Empty<RigSearchHit>();
        }

        if (IsDisposed) return;

        foreach ((RigProgressRow row, PortableApp app) in _rows)
        {
            if (hits.Any(result => ReferenceEquals(result.App, app))) continue;
            row.Finish(false, "NOT FOUND", $"No {app.ExeName} on the drives that were searched.");
        }

        _found = hits.Count;

        _sweep.Stop();
        _running = false;

        SetStatus(
            _cancel.IsCancellationRequested
                ? $"CANCELLED \u2014 {_found} OF {_rows.Count} RIGS FOUND"
                : _found == 0
                    ? "NOTHING FOUND \u2014 USE AN INSTALL LINK OR SET THE LOCATION"
                    : $"FINISHED \u2014 {_found} OF {_rows.Count} RIGS FOUND",
            _found == 0 ? Theme.Warn : Theme.Good);

        _action.Text = "CLOSE";
        _action.Enabled = true;
    }

    private void ShowProgress()
    {
        SetStatus($"{_target}  \u2022  {_scanned:N0} FOLDERS", Theme.Accent);
    }
}
