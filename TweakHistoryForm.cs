using System.Drawing.Drawing2D;

namespace PC_Medic;

/// <summary>The tweak journal, newest first: every switch flipped, what it replaced, and a LIFO undo.</summary>
internal sealed class TweakHistoryForm : Form
{
    private const int WmNcHitTest = 0x0084;
    private const int HtCaption = 2;
    private const int HtClient = 1;
    private const int RowHeight = 70;

    private readonly Panel _list = new();
    private readonly Label _status = new();
    private readonly Label _empty = new();
    private readonly NeonButton _undo = new() { Text = "UNDO LAST", Size = new Size(140, 32), Primary = true };
    private readonly NeonButton _close = new() { Text = "CLOSE", Size = new Size(96, 32) };
    private readonly ToolTip _tips = new();

    public TweakHistoryForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(680, 520);
        BackColor = Theme.Bg;
        Font = Theme.Ui;
        ForeColor = Theme.Text;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        KeyPreview = true;

        Controls.Add(MakeLabel("TWEAK JOURNAL", Theme.Kicker, Theme.Alpha(Theme.Accent, 215), new Point(30, 28)));
        Controls.Add(MakeLabel("UNDO HISTORY", new Font(Theme.H2.FontFamily, 15f, FontStyle.Bold), Theme.Text, new Point(30, 46)));
        Controls.Add(MakeLabel(
            "Newest first \u2014 every entry keeps the exact values the switch replaced.",
            Theme.UiSmall,
            Theme.TextDim,
            new Point(30, 78)));

        _list.SetBounds(24, 108, ClientSize.Width - 48, ClientSize.Height - 108 - 78);
        _list.BackColor = Theme.Surface;
        _list.AutoScroll = true;
        _list.Margin = new Padding(0);
        _list.ClientSizeChanged += (_, _) => ResizeRows();
        Controls.Add(_list);

        _empty.Text = "No tweak changes recorded yet. Flip a switch and it lands here.";
        _empty.Font = Theme.MonoSmall;
        _empty.ForeColor = Theme.TextDim;
        _empty.BackColor = Color.Transparent;
        _empty.AutoSize = true;
        _empty.Location = new Point(12, 12);
        _empty.Visible = false;

        _status.Font = Theme.MonoSmall;
        _status.ForeColor = Theme.TextDim;
        _status.BackColor = Color.Transparent;
        _status.AutoSize = false;
        _status.AutoEllipsis = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.SetBounds(30, ClientSize.Height - 64, ClientSize.Width - 30 - 30 - 96 - 150, 32);

        _undo.SetBounds(ClientSize.Width - 30 - 96 - 10 - 140, ClientSize.Height - 64, 140, 32);
        _close.SetBounds(ClientSize.Width - 30 - 96, ClientSize.Height - 64, 96, 32);

        Controls.Add(_status);
        Controls.Add(_undo);
        Controls.Add(_close);

        _tips.SetToolTip(_undo, "Restores the values recorded in the newest entry. Older entries are read-only history.");

        _undo.Click += (_, _) => Undo();
        _close.Click += (_, _) => Close();

        Reload();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Close();
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

    private static Label RowLabel(Color tone, int height) => new()
    {
        Dock = DockStyle.Top,
        Height = height,
        AutoSize = false,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = Theme.MonoSmall,
        ForeColor = tone,
        BackColor = Color.Transparent,
        Margin = new Padding(0),
    };

    private void Reload()
    {
        IReadOnlyList<TweakChange> history = TweakBackup.History;

        foreach (Control control in _list.Controls.Cast<Control>().ToArray()) control.Dispose();
        _list.Controls.Clear();

        int y = 6;

        for (int index = history.Count - 1; index >= 0; index--)
        {
            AddRow(history[index], newest: index == history.Count - 1, y);
            y += RowHeight;
        }

        _empty.Visible = history.Count == 0;
        if (_empty.Visible) _list.Controls.Add(_empty);

        _list.AutoScrollMinSize = new Size(0, y + 6);
        _undo.Enabled = history.Count > 0;

        _status.Text = history.Count == 0
            ? "Nothing to undo."
            : $"{history.Count} CHANGE{(history.Count == 1 ? string.Empty : "S")} RECORDED \u00b7 UNDO RESTORES THE NEWEST";
        _status.ForeColor = Theme.TextDim;
        _tips.SetToolTip(_status, _status.Text);
    }

    private void AddRow(TweakChange change, bool newest, int y)
    {
        var row = new Panel
        {
            BackColor = newest ? Theme.Surface2 : Theme.Surface,
            Location = new Point(4, y),
            Height = RowHeight - 6,
            Width = Math.Max(240, _list.ClientSize.Width - 20),
            Padding = new Padding(12, 6, 10, 0),
            Margin = new Padding(0),
        };

        row.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.BorderSoft);
            e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
        };

        string state = change.Enabled ? "APPLIED" : change.FromRevertAll ? "REVERTED (BULK)" : "REVERTED";
        string headline = $"{change.At:MMM d HH:mm:ss}  \u00b7  {change.Title}  \u00b7  {state}";
        string summary = PreviousSummary(change);

        Label head = RowLabel(change.Enabled ? Theme.Good : Theme.TextDim, 18);
        head.Text = headline;

        Label values = RowLabel(Theme.TextDim, 16);
        values.Text = summary;

        Label message = RowLabel(Theme.TextDim, 16);
        message.Text = string.IsNullOrWhiteSpace(change.Message) ? "\u2014" : change.Message;

        _tips.SetToolTip(head, $"{headline}\r\n{PreviousDetail(change)}");
        _tips.SetToolTip(values, PreviousDetail(change));
        _tips.SetToolTip(message, change.Message);

        row.Controls.Add(message);
        row.Controls.Add(values);
        row.Controls.Add(head);
        _list.Controls.Add(row);
    }

    private void ResizeRows()
    {
        int width = Math.Max(240, _list.ClientSize.Width - 20);
        foreach (Control control in _list.Controls)
        {
            if (control is Panel row) row.Width = width;
        }
    }

    /// <summary>Compact line: just the value names, since the full key paths live in the tooltip.</summary>
    private static string PreviousSummary(TweakChange change)
    {
        var parts = new List<string>();

        foreach (SavedValue saved in change.Previous)
        {
            parts.Add($"{saved.Name} = {saved.Value ?? "(not set)"}");
        }

        if (change.HibernatePrevious is { } hibernated)
        {
            parts.Add($"hiberfil.sys = {(hibernated ? "present" : "off")}");
        }

        return parts.Count == 0 ? "No values recorded for this change." : string.Join("  \u00b7  ", parts);
    }

    private static string PreviousDetail(TweakChange change)
    {
        if (change.Previous.Count == 0 && change.HibernatePrevious is null) return "No values recorded.";

        var lines = new List<string>();
        lines.Add($"Before {change.At:yyyy-MM-dd HH:mm:ss}");

        foreach (SavedValue saved in change.Previous)
        {
            lines.Add($"{saved.Hive}\\{saved.Path}\\{saved.Name} = {saved.Value ?? "(not set)"}");
        }

        if (change.HibernatePrevious is { } hibernated)
        {
            lines.Add($"hiberfil.sys = {(hibernated ? "present" : "off")}");
        }

        return string.Join("\r\n", lines);
    }

    private void Undo()
    {
        _undo.Enabled = false;

        (bool ok, string message) = TweakBackup.UndoLast();
        ActivityLog.Write(ok ? LogLevel.Success : LogLevel.Warn, $"Tweak undo: {message}");
        if (IsDisposed) return;

        Reload();

        _status.Text = message;
        _status.ForeColor = ok ? Theme.Good : Theme.Warn;
        _tips.SetToolTip(_status, message);
    }
}
