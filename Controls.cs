using System.Drawing.Drawing2D;

namespace PC_Medic;

/// <summary>Base for every hand-painted control: no stock chrome, no layout panels.</summary>
internal abstract class SkinnedControl : Control
{
    protected SkinnedControl()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Font = Theme.Ui;
        ForeColor = Theme.Text;
    }

    protected const TextFormatFlags Centered =
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;

    protected const TextFormatFlags Labeled =
        TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
}

internal sealed class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        DoubleBuffered = true;
    }
}

/// <summary>Angular instrument-panel button used for every action in the app.</summary>
internal sealed class NeonButton : SkinnedControl
{
    private bool _hover;
    private bool _down;

    public NeonButton()
    {
        Cursor = Cursors.Hand;
        Size = new Size(150, 34);
        Font = Theme.UiBold;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Tone { get; set; } = Theme.Accent;

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _down = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _down = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _down = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 6 || Height < 6) return;

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using GraphicsPath path = Theme.Round(rect, 7f);

        if (!Enabled)
        {
            using var fill = new SolidBrush(Theme.Surface);
            using var border = new Pen(Theme.BorderSoft);
            g.FillPath(fill, path);
            g.DrawPath(border, path);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Theme.TextFaint, Centered);
            return;
        }

        if (Primary)
        {
            Color top = _down ? ControlPaint.Dark(Tone, 0.12f) : _hover ? ControlPaint.Light(Tone, 0.16f) : Tone;
            Color bottom = ControlPaint.Dark(top, 0.22f);
            using (var fill = new LinearGradientBrush(new RectangleF(0f, 0f, Width, Height), top, bottom, 90f))
            {
                g.FillPath(fill, path);
            }

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Color.FromArgb(4, 18, 24), Centered);
            return;
        }

        using var body = new SolidBrush(_hover ? Theme.Surface2 : Color.FromArgb(19, 24, 33));
        using var edge = new Pen(_hover ? Theme.Alpha(Tone, 175) : Theme.Border, 1f);
        g.FillPath(body, path);
        g.DrawPath(edge, path);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, _hover ? Tone : Theme.Text, Centered);
    }
}

/// <summary>Small status pill (READY / MISSING / HIGH impact).</summary>
internal sealed class Badge : SkinnedControl
{
    public Badge()
    {
        Height = 18;
        Width = 60;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Tone { get; set; } = Theme.Accent;

    public void SetText(string text)
    {
        Text = text;
        Width = TextRenderer.MeasureText(text, Theme.Kicker).Width + 16;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 4 || Height < 4) return;

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using GraphicsPath path = Theme.Round(rect, Height / 2f);
        using (var fill = new SolidBrush(Theme.Alpha(Tone, 26)))
        {
            g.FillPath(fill, path);
        }

        using (var pen = new Pen(Theme.Alpha(Tone, 110), 1f))
        {
            g.DrawPath(pen, path);
        }

        TextRenderer.DrawText(g, Text, Theme.Kicker, ClientRectangle, Tone, Centered);
    }
}

/// <summary>Sidebar navigation row with an icon glyph and an active accent rail.</summary>
internal sealed class NavItem : SkinnedControl
{
    private bool _active;
    private bool _hover;
    private string _badgeText = string.Empty;

    public NavItem(string key, string glyph, string caption)
    {
        Key = key;
        Glyph = glyph;
        Caption = caption;
        Height = 44;
        Cursor = Cursors.Hand;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Key { get; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Glyph { get; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Caption { get; }

    /// <summary>Short count shown on the right, e.g. pending updates; empty hides the pill.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string BadgeText
    {
        get => _badgeText;
        set
        {
            if (_badgeText == value) return;
            _badgeText = value;
            Invalidate();
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color BadgeTone { get; set; } = Theme.Accent;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 20 || Height < 10) return;

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);

        if (_active)
        {
            using GraphicsPath path = Theme.Round(rect, 8f);
            using var fill = new SolidBrush(Theme.Surface2);
            using var border = new Pen(Theme.Alpha(Theme.Accent, 70));
            g.FillPath(fill, path);
            g.DrawPath(border, path);
            using var rail = new SolidBrush(Theme.Accent);
            g.FillRectangle(rail, 0, (Height - 20) / 2f + 0.5f, 3f, 20f);
        }
        else if (_hover)
        {
            using GraphicsPath path = Theme.Round(rect, 8f);
            using var fill = new SolidBrush(Color.FromArgb(14, 20, 28));
            g.FillPath(fill, path);
        }

        Color iconTone = _active ? Theme.Accent : _hover ? Theme.Text : Theme.TextDim;
        Color textTone = _active ? Theme.Text : _hover ? Theme.Text : Theme.TextDim;

        TextRenderer.DrawText(
            g,
            Glyph,
            Theme.Icon,
            new Rectangle(16, 0, 26, Height),
            iconTone,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        int captionRight = Width - 56;

        if (!string.IsNullOrEmpty(_badgeText))
        {
            int badgeWidth = Math.Max(24, TextRenderer.MeasureText(_badgeText, Theme.Kicker).Width + 14);
            var badgeRect = new Rectangle(Width - badgeWidth - 14, (Height - 17) / 2, badgeWidth, 17);
            using GraphicsPath pill = Theme.Round(badgeRect, 5f);
            using var fill = new SolidBrush(Theme.Alpha(BadgeTone, 34));
            using var pen = new Pen(Theme.Alpha(BadgeTone, 120));
            g.FillPath(fill, pill);
            g.DrawPath(pen, pill);
            TextRenderer.DrawText(
                g,
                _badgeText,
                Theme.Kicker,
                badgeRect,
                BadgeTone,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            captionRight = Width - badgeWidth - 26;
        }

        TextRenderer.DrawText(
            g,
            Caption,
            _active ? Theme.UiBold : Theme.Ui,
            new Rectangle(46, 0, Math.Max(40, captionRight - 46), Height),
            textTone,
            Labeled);
    }
}

/// <summary>Rounded surface with a kicker + header, used for every panel.</summary>
internal class Card : Panel
{
    public Card(string? header = null, string? kicker = null, bool accentTick = true)
    {
        DoubleBuffered = true;
        BackColor = Theme.Bg;
        SetStyle(ControlStyles.ResizeRedraw, true);
        Header = header;
        Kicker = kicker;
        AccentTick = accentTick;
        Padding = new Padding(18, header is null ? 16 : 54, 18, 14);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? Header { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? Kicker { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AccentTick { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 8 || Height < 8) return;

        Graphics g = e.Graphics;
        Theme.PaintSurface(
            g,
            new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f),
            10f,
            Theme.Surface,
            Theme.Border,
            AccentTick ? Theme.Accent : null);

        if (!string.IsNullOrEmpty(Kicker))
        {
            TextRenderer.DrawText(g, Kicker.ToUpperInvariant(), Theme.Kicker, new Point(18, 15), Theme.TextDim, TextFormatFlags.NoPrefix);
        }

        if (!string.IsNullOrEmpty(Header))
        {
            TextRenderer.DrawText(g, Header, Theme.H2, new Point(18, 27), Theme.Text, TextFormatFlags.NoPrefix);
        }

        base.OnPaint(e);
    }
}

/// <summary>Telemetry tile: kicker, large mono reading, context line, segmented load bar.</summary>
internal sealed class StatTile : SkinnedControl
{
    private string _value = "\u2014";
    private string _sub = string.Empty;
    private double _progress;
    private double _target;
    private NeonButton? _action;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Caption { get; set; } = "METRIC";

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Tone { get; set; } = Theme.Accent;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            Invalidate();
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Sub
    {
        get => _sub;
        set
        {
            if (_sub == value) return;
            _sub = value;
            Invalidate();
        }
    }

    /// <summary>Optional remediation button docked in the tile's bottom band.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public NeonButton? Action
    {
        get => _action;
        set
        {
            if (_action is not null) Controls.Remove(_action);
            _action = value;

            if (_action is not null)
            {
                Controls.Add(_action);
                LayoutAction();
            }

            Invalidate();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutAction();
    }

    private void LayoutAction()
    {
        if (_action is null || Width < 40 || Height < 90) return;
        _action.SetBounds(14, Height - 46, Math.Max(20, Width - 28), 30);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Progress
    {
        get => _target;
        set
        {
            _target = Math.Clamp(value, 0, 1);
            if (Math.Abs(_progress - _target) < 0.004) _progress = _target;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 40 || Height < 40) return;

        Graphics g = e.Graphics;
        _progress += (_target - _progress) * 0.35;

        Theme.PaintSurface(
            g,
            new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f),
            10f,
            Theme.Surface,
            Theme.Border,
            Tone);

        TextRenderer.DrawText(g, Caption.ToUpperInvariant(), Theme.Kicker, new Point(18, 18), Theme.TextDim, TextFormatFlags.NoPrefix);

        using (var accent = new SolidBrush(Theme.Alpha(Tone, 18)))
        {
            g.FillEllipse(accent, Width - 34, 16, 16, 16);
        }

        using (var dot = new SolidBrush(Tone))
        {
            g.FillEllipse(dot, Width - 30, 20, 8, 8);
        }

        TextRenderer.DrawText(
            g,
            Value,
            Theme.Numeric,
            new Rectangle(16, 36, Width - 30, 34),
            Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        if (!string.IsNullOrEmpty(Sub))
        {
            TextRenderer.DrawText(
                g,
                Sub,
                Theme.UiSmall,
                new Rectangle(17, 70, Width - 32, 18),
                Theme.TextDim,
                Labeled);
        }

        // The action band is reserved on every tile so the load bars stay on one line across the row.
        int barY = Height >= 130 ? Height - 58 : Height - 22;
        var bar = new Rectangle(18, barY, Width - 36, 5);
        if (bar.Width > 24)
        {
            DrawSegments(g, bar, _progress, Tone);
        }
    }

    private static void DrawSegments(Graphics g, Rectangle bounds, double progress, Color tone)
    {
        const int Segments = 34;
        int gap = 2;
        int width = Math.Max(1, (bounds.Width - (gap * (Segments - 1))) / Segments);
        int lit = (int)Math.Round(Math.Clamp(progress, 0, 1) * Segments);

        using var off = new SolidBrush(Theme.BorderSoft);
        using var on = new SolidBrush(tone);

        for (int i = 0; i < Segments; i++)
        {
            int x = bounds.X + (i * (width + gap));
            g.FillRectangle(i < lit ? on : off, x, bounds.Y, width, bounds.Height);
        }
    }
}

/// <summary>Key/value readout rows with hairline separators.</summary>
internal sealed class InfoList : SkinnedControl
{
    private readonly List<(string Key, string Value, Color Tone)> _rows = new();
    private readonly ToolTip _tip = new();
    private int _rowHeight = 27;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int RowHeight
    {
        get => _rowHeight;
        set
        {
            _rowHeight = Math.Max(18, value);
            Height = _rows.Count * _rowHeight;
            Invalidate();
        }
    }

    public void Clear()
    {
        _rows.Clear();
        Height = 0;
        Invalidate();
    }

    public void Add(string key, string value, Color? tone = null)
    {
        _rows.Add((key, value, tone ?? Theme.Text));
        Height = _rows.Count * _rowHeight;
        Invalidate();
    }

    /// <summary>Rows ellipsise long values, so hovering one shows the whole line.</summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        int index = e.Y / Math.Max(1, _rowHeight);
        string text = index >= 0 && index < _rows.Count ? $"{_rows[index].Key}: {_rows[index].Value}" : string.Empty;

        if (_tip.GetToolTip(this) != text) _tip.SetToolTip(this, text);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        int index = 0;

        foreach ((string key, string value, Color tone) in _rows)
        {
            int y = index * _rowHeight;
            int keyWidth = Math.Max(60, (int)(Width * 0.36));

            TextRenderer.DrawText(
                g,
                key.ToUpperInvariant(),
                Theme.Kicker,
                new Rectangle(0, y, keyWidth, _rowHeight),
                Theme.TextDim,
                Labeled);

            TextRenderer.DrawText(
                g,
                value,
                value.Length > 32 ? Theme.MonoSmall : Theme.Mono,
                new Rectangle(keyWidth + 6, y, Math.Max(10, Width - keyWidth - 6), _rowHeight),
                tone,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            using var separator = new Pen(Theme.BorderSoft);
            g.DrawLine(separator, 0, y + _rowHeight - 1, Width, y + _rowHeight - 1);
            index++;
        }
    }
}

/// <summary>List row with title, subtitle, impact badge and a painted toggle.</summary>
internal sealed class ToggleRow : SkinnedControl
{
    private bool _checked;
    private bool _hover;
    private bool _hoverSwitch;
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private string _badge = string.Empty;
    private string _actionText = string.Empty;
    private Color _actionTone = Theme.Bad;
    private bool _actionHover;
    private bool _actionPressed;
    private string _actionGlyph = string.Empty;
    private bool _working;
    private int _progress;
    private float _phase;

    public ToggleRow()
    {
        Height = 62;
    }

    public event Action<ToggleRow, bool>? Toggled;

    /// <summary>Raised when the row body, rather than the switch, is clicked.</summary>
    public event Action<ToggleRow>? RowClicked;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => _title;
        set { _title = value; Invalidate(); }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Subtitle
    {
        get => _subtitle;
        set { _subtitle = value; Invalidate(); }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string BadgeText
    {
        get => _badge;
        set { _badge = value; Invalidate(); }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color BadgeTone { get; set; } = Theme.TextDim;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Tone { get; set; } = Theme.Accent;

    /// <summary>True while a download or install owns this row: a bar runs along its bottom edge.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Working
    {
        get => _working;
        set
        {
            if (_working == value) return;
            _working = value;
            Invalidate();
        }
    }

    /// <summary>0–100 from the downloader; zero keeps the bar sweeping instead of filling.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Progress
    {
        get => _progress;
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            if (_progress == clamped) return;
            _progress = clamped;
            Invalidate();
        }
    }

    /// <summary>Advances the indeterminate sweep; driven by the owning page's timer.</summary>
    public void Sweep()
    {
        if (!_working) return;

        _phase += 0.024f;
        if (_phase > 1f) _phase -= 1f;
        Invalidate();
    }

    /// <summary>Optional per-row action pill before the switch; empty draws nothing.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string ActionText
    {
        get => _actionText;
        set
        {
            string text = value ?? string.Empty;
            if (_actionText == text) return;
            _actionText = text;
            Invalidate();
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color ActionTone
    {
        get => _actionTone;
        set
        {
            _actionTone = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Icon-only action (a Segoe glyph) drawn as a square pad rather than a text pill, for a row whose
    /// single action is obvious enough that a label would only crowd the title.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string ActionGlyph
    {
        get => _actionGlyph;
        set
        {
            string glyph = value ?? string.Empty;
            if (_actionGlyph == glyph) return;
            _actionGlyph = glyph;
            Invalidate();
        }
    }

    /// <summary>Raised as the pointer enters and leaves the action, so a page can swap its tooltip.</summary>
    public event Action<ToggleRow, bool>? ActionHoverChanged;

    /// <summary>Raised when the pill is pressed; the switch is left untouched.</summary>
    public event Action<ToggleRow>? ActionClicked;

    private bool HasAction => _actionText.Length > 0 || _actionGlyph.Length > 0;

    private Rectangle ActionBounds
    {
        get
        {
            if (!HasAction) return Rectangle.Empty;

            if (_actionText.Length == 0)
            {
                return new Rectangle(Width - 58 - 10 - 30, (Height - 26) / 2, 30, 26);
            }

            int width = TextRenderer.MeasureText(_actionText, Theme.Kicker).Width + 20;
            return new Rectangle(Width - 58 - 10 - width, (Height - 18) / 2, width, 18);
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
        }
    }

    /// <summary>Lets the row body react to clicks even when the switch must not move.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Clickable { get; set; }

    /// <summary>The switch is decorative: the row can be inspected but not ticked.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Locked { get; set; }

    private Rectangle SwitchBounds => new(Width - 58, (Height - 20) / 2, 40, 20);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool overSwitch = SwitchBounds.Contains(e.Location);
        bool overAction = HasAction && ActionBounds.Contains(e.Location);
        bool actionChanged = overAction != _actionHover;
        bool changed = false;
        if (overSwitch != _hoverSwitch || actionChanged)
        {
            _hoverSwitch = overSwitch;
            _actionHover = overAction;
            Cursor = Enabled && (overSwitch || overAction || Clickable) ? Cursors.Hand : Cursors.Default;
            changed = true;
        }

        if (actionChanged) ActionHoverChanged?.Invoke(this, overAction);

        if (!_hover)
        {
            _hover = true;
            changed = true;
        }

        if (changed) Invalidate();
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _hoverSwitch = false;
        bool wasAction = _actionHover;
        _actionHover = false;
        Cursor = Cursors.Default;
        if (wasAction) ActionHoverChanged?.Invoke(this, false);
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (HasAction && Enabled && ActionBounds.Contains(e.Location))
        {
            _actionPressed = true;
            ActionClicked?.Invoke(this);
            base.OnMouseDown(e);
            return;
        }

        if (Enabled && !Locked && SwitchBounds.Contains(e.Location))
        {
            Checked = !Checked;
            Toggled?.Invoke(this, Checked);
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_actionPressed)
        {
            _actionPressed = false;
            base.OnMouseUp(e);
            return;
        }

        if (Enabled && Clickable && !SwitchBounds.Contains(e.Location)) RowClicked?.Invoke(this);
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 60 || Height < 24) return;

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        bool interactive = Enabled && !Locked;

        Theme.PaintSurface(
            g,
            new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f),
            8f,
            _hover && interactive ? Color.FromArgb(21, 29, 40) : Theme.Surface2,
            _hover && interactive ? Theme.Alpha(Tone, 60) : Theme.BorderSoft,
            null);

        using (var dot = new SolidBrush(Checked && interactive ? Tone : Theme.TextFaint))
        {
            g.FillRectangle(dot, 16, (Height / 2f) - 4f, 8, 8);
        }

        int reserved = HasAction ? ActionBounds.Width + 12 : 0;
        int textWidth = Math.Max(40, Width - 90 - reserved);
        bool badge = !string.IsNullOrEmpty(BadgeText);

        // The badge sits after the title, so the title box gives up that room: a long name ellipsises
        // instead of running underneath the pill.
        int titleWidth = Math.Max(40, textWidth - (badge ? 58 : 0));
        TextRenderer.DrawText(
            g,
            Title,
            Theme.UiBold,
            new Rectangle(34, 10, titleWidth, 20),
            interactive ? Theme.Text : Theme.TextDim,
            TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (badge)
        {
            int drawn = Math.Min(TextRenderer.MeasureText(Title, Theme.UiBold).Width, titleWidth);
            var badgeRect = new Rectangle(34 + drawn + 10, 13, 44, 15);
            using GraphicsPath pill = Theme.Round(badgeRect, 4f);
            using var fill = new SolidBrush(Theme.Alpha(BadgeTone, 24));
            using var pen = new Pen(Theme.Alpha(BadgeTone, 90));
            g.FillPath(fill, pill);
            g.DrawPath(pen, pill);
            TextRenderer.DrawText(g, BadgeText, Theme.Kicker, badgeRect, BadgeTone, Centered);
        }

        if (!string.IsNullOrEmpty(Subtitle))
        {
            TextRenderer.DrawText(
                g,
                Subtitle,
                Theme.MonoSmall,
                new Rectangle(34, 31, Math.Max(40, Width - 100 - reserved), 20),
                interactive ? Theme.TextDim : Theme.TextFaint,
                Labeled);
        }

        if (HasAction)
        {
            Rectangle pill = ActionBounds;
            Color tone = Enabled ? ActionTone : Theme.TextFaint;
            bool glyphOnly = _actionText.Length == 0;

            using GraphicsPath pillPath = Theme.Round(pill, glyphOnly ? 6f : 5f);
            using (var fill = new SolidBrush(Theme.Alpha(tone, _actionHover && Enabled ? 60 : 26)))
            {
                g.FillPath(fill, pillPath);
            }

            using (var pen = new Pen(Theme.Alpha(tone, _actionHover && Enabled ? 200 : 120), 1f))
            {
                g.DrawPath(pen, pillPath);
            }

            if (glyphOnly)
            {
                TextRenderer.DrawText(g, _actionGlyph, Theme.Icon, pill, tone, Centered);
            }
            else
            {
                TextRenderer.DrawText(g, _actionText, Theme.Kicker, pill, tone, Centered);
            }
        }

        Rectangle track = SwitchBounds;
        using GraphicsPath switchPath = Theme.Round(track, track.Height / 2f);
        using (var trackFill = new SolidBrush(Checked ? Theme.Alpha(Tone, interactive ? 55 : 26) : Color.FromArgb(10, 15, 21)))
        {
            g.FillPath(trackFill, switchPath);
        }

        using (var trackEdge = new Pen(Checked ? Theme.Alpha(Tone, interactive ? 210 : 80) : Theme.Border, 1f))
        {
            g.DrawPath(trackEdge, switchPath);
        }

        int knobX = Checked ? track.Right - 17 : track.X + 3;
        using var knob = new SolidBrush(Checked ? (interactive ? Tone : Theme.Alpha(Tone, 110)) : Theme.TextFaint);
        g.FillEllipse(knob, knobX, track.Y + 3, 14, 14);

        if (!_working) return;

        var meter = new RectangleF(14, Height - 9, Math.Max(40, Width - 28), 4);
        using (GraphicsPath meterTrack = Theme.Round(meter, 2f))
        {
            using var off = new SolidBrush(Theme.BorderSoft);
            g.FillPath(off, meterTrack);
        }

        RectangleF lit;

        if (_progress > 0)
        {
            lit = new RectangleF(meter.X, meter.Y, Math.Max(3f, meter.Width * (_progress / 100f)), meter.Height);
        }
        else
        {
            float segment = Math.Max(40f, meter.Width * 0.3f);
            float x = -segment + (_phase * (meter.Width + segment));
            float left = Math.Max(meter.X, x);
            float right = Math.Min(meter.Right, x + segment);
            if (right <= left) return;

            lit = new RectangleF(left, meter.Y, right - left, meter.Height);
        }

        using GraphicsPath litPath = Theme.Round(lit, 2f);
        using var brush = new SolidBrush(_progress > 0 ? Theme.Good : Theme.Accent);
        g.FillPath(brush, litPath);
    }
}

/// <summary>Minute stepper row with painted minus/plus pads.</summary>
internal sealed class StepperRow : SkinnedControl
{
    private int _value;
    private bool _hover;
    private int _hot;

    public StepperRow()
    {
        Height = 58;
        Tone = Theme.Accent;
    }

    public event Action<StepperRow, int>? ValueChanged;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; } = string.Empty;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Subtitle { get; set; } = string.Empty;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Min { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Max { get; set; } = 240;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Step { get; set; } = 5;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Tone { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Unit { get; set; } = "MIN";

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Clamp(value, Min, Max);
            if (_value == clamped) return;
            _value = clamped;
            Invalidate();
        }
    }

    private Rectangle MinusBounds => new(Width - 126, (Height - 26) / 2, 26, 26);

    private Rectangle PlusBounds => new(Width - 34, (Height - 26) / 2, 26, 26);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int hot = MinusBounds.Contains(e.Location) ? -1 : PlusBounds.Contains(e.Location) ? 1 : 0;
        bool over = e.X > Width - 140;
        if (hot != _hot || over != _hover)
        {
            _hot = hot;
            _hover = over;
            Cursor = hot != 0 && Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hot = 0;
        _hover = false;
        Cursor = Cursors.Default;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (!Enabled) return;

        int delta = MinusBounds.Contains(e.Location) ? -Step : PlusBounds.Contains(e.Location) ? Step : 0;
        if (delta == 0) return;

        int next = Math.Clamp(_value + delta, Min, Max);
        if (next == _value) return;
        _value = next;
        Invalidate();
        ValueChanged?.Invoke(this, _value);
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 80 || Height < 24) return;

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Theme.PaintSurface(
            g,
            new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f),
            8f,
            _hover ? Color.FromArgb(21, 29, 40) : Theme.Surface2,
            _hover ? Theme.Alpha(Tone, 50) : Theme.BorderSoft,
            null);

        TextRenderer.DrawText(g, Title, Theme.UiBold, new Rectangle(16, 8, Width - 200, 20), Theme.Text, Labeled);

        if (!string.IsNullOrEmpty(Subtitle))
        {
            TextRenderer.DrawText(g, Subtitle, Theme.UiSmall, new Rectangle(16, 28, Width - 160, 18), Theme.TextDim, Labeled);
        }

        DrawPad(g, MinusBounds, "\u2212", _hot == -1);
        DrawPad(g, PlusBounds, "+", _hot == 1);

        var valueRect = new Rectangle(MinusBounds.Right, 0, PlusBounds.Left - MinusBounds.Right, Height);
        string text = _value == 0 ? "NEVER" : $"{_value} {Unit}";
        TextRenderer.DrawText(g, text, Theme.Mono, valueRect, Enabled ? Theme.Text : Theme.TextDim, Centered);
    }

    private void DrawPad(Graphics g, Rectangle bounds, string glyph, bool hot)
    {
        using GraphicsPath path = Theme.Round(bounds, 6f);
        using (var fill = new SolidBrush(hot ? Theme.Alpha(Tone, 55) : Color.FromArgb(11, 16, 23)))
        {
            g.FillPath(fill, path);
        }

        using (var pen = new Pen(hot ? Theme.Alpha(Tone, 200) : Theme.Border, 1f))
        {
            g.DrawPath(pen, path);
        }

        TextRenderer.DrawText(g, glyph, Theme.UiBold, bounds, hot ? Tone : Theme.Text, Centered);
    }
}

/// <summary>Scrolling activity feed painted as monospace console lines.</summary>
internal sealed class ActivityConsole : SkinnedControl
{
    private const int Capacity = 160;
    private readonly List<LogEntry> _lines = new();

    public ActivityConsole()
    {
        foreach (LogEntry entry in ActivityLog.Snapshot().TakeLast(Capacity))
        {
            _lines.Add(entry);
        }

        ActivityLog.Written += OnWritten;
    }

    private void OnWritten(LogEntry entry)
    {
        if (IsDisposed) return;

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => Append(entry)));
            }
            catch (InvalidOperationException)
            {
                // Handle went away mid-flight; nothing to paint.
            }

            return;
        }

        Append(entry);
    }

    private void Append(LogEntry entry)
    {
        _lines.Add(entry);
        if (_lines.Count > Capacity) _lines.RemoveRange(0, _lines.Count - Capacity);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ActivityLog.Written -= OnWritten;
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        const int LineHeight = 21;
        int y = Height - 8 - LineHeight;

        for (int i = _lines.Count - 1; i >= 0 && y > -LineHeight; i--)
        {
            LogEntry entry = _lines[i];
            TextRenderer.DrawText(
                g,
                entry.Stamp,
                Theme.MonoSmall,
                new Rectangle(0, y, 62, LineHeight),
                Theme.TextDim,
                Labeled);
            g.FillRectangle(new SolidBrush(entry.Tone), 70, y + (LineHeight / 2) - 2, 4, 4);
            TextRenderer.DrawText(
                g,
                entry.Message,
                Theme.MonoSmall,
                new Rectangle(84, y, Math.Max(10, Width - 90), LineHeight),
                entry.Tone == Theme.Accent ? Theme.Text : entry.Tone,
                Labeled);
            y -= LineHeight;
        }
    }
}

/// <summary>Clickable tool tile for the maintenance and benchmark grids.</summary>
internal sealed class ToolTile : SkinnedControl
{
    private bool _hover;

    public ToolTile()
    {
        Height = 86;
        Cursor = Cursors.Hand;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Glyph { get; set; } = string.Empty;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; } = string.Empty;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Subtitle { get; set; } = string.Empty;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Status { get; set; } = string.Empty;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Tone { get; set; } = Theme.Accent;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color StatusTone { get; set; } = Theme.TextDim;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string ActionText { get; set; } = string.Empty;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color ActionTone { get; set; } = Theme.Accent;

    /// <summary>Hand-drawn plate shown instead of the glyph, when the tile has one.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public RigMark? Mark { get; set; }

    /// <summary>Dims the plate and stamps a download badge, for a rig that is not present.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Missing { get; set; }

    /// <summary>Where to get the tool, shown in the lower right; empty hides the line.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string LinkText { get; set; } = string.Empty;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color LinkTone { get; set; } = Theme.Violet;

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 60 || Height < 40) return;

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Theme.PaintSurface(
            g,
            new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f),
            9f,
            _hover ? Color.FromArgb(21, 29, 40) : Theme.Surface,
            _hover ? Theme.Alpha(Tone, 120) : Theme.Border,
            null);

        if (Mark is { } watermark && Height >= 140)
        {
            float plate = Math.Min(Height * 0.40f, Width * 0.26f);
            var field = new RectangleF((Width - plate) / 2f, (Height - plate) / 2f, plate, plate);
            RigArt.Paint(g, field, watermark, Theme.Alpha(Tone, Missing ? 20 : 28));
        }

        var iconBox = new Rectangle(14, 16, 34, 34);
        using (GraphicsPath path = Theme.Round(iconBox, 8f))
        {
            using var fill = new SolidBrush(Theme.Alpha(Tone, _hover ? 34 : 20));
            using var pen = new Pen(Theme.Alpha(Tone, _hover ? 160 : 90));
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        TextRenderer.DrawText(g, Glyph, Theme.Icon, iconBox, Missing ? Theme.Alpha(Tone, 95) : Tone, Centered);

        if (Mark is { } mark)
        {
            RigArt.Paint(g, iconBox, mark, Missing ? Theme.Alpha(Tone, 110) : Tone);
            if (Missing) PaintMissingBadge(g, iconBox);
        }
        else
        {
            TextRenderer.DrawText(g, Glyph, Theme.Icon, iconBox, Missing ? Theme.Alpha(Tone, 95) : Tone, Centered);
        }

        int textLeft = iconBox.Right + 12;
        TextRenderer.DrawText(
            g,
            Title,
            Theme.UiBold,
            new Rectangle(textLeft, 16, Math.Max(20, Width - textLeft - 16), 20),
            Theme.Text,
            TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(
            g,
            Subtitle,
            Theme.UiSmall,
            new Rectangle(textLeft, 38, Math.Max(20, Width - textLeft - 16), 18),
            Theme.TextDim,
            Labeled);

        if (!string.IsNullOrEmpty(LinkText))
        {
            TextRenderer.DrawText(
                g,
                LinkText,
                Theme.MonoSmall,
                new Rectangle(Width / 2, Height - 46, Math.Max(20, (Width / 2) - 16), 16),
                LinkTone,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        if (!string.IsNullOrEmpty(Status))
        {
            var pill = new Rectangle(textLeft, Height - 24, 54, 14);
            using GraphicsPath path = Theme.Round(pill, 4f);
            using var fill = new SolidBrush(Theme.Alpha(StatusTone, 22));
            using var pen = new Pen(Theme.Alpha(StatusTone, 90));
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
            TextRenderer.DrawText(g, Status, Theme.Kicker, pill, StatusTone, Centered);
        }

        if (!string.IsNullOrEmpty(ActionText))
        {
            int actionWidth = TextRenderer.MeasureText(ActionText, Theme.Kicker).Width;
            var actionRect = new Rectangle(Width - 16 - actionWidth, Height - 24, actionWidth, 14);
            TextRenderer.DrawText(
                g,
                ActionText,
                Theme.Kicker,
                actionRect,
                ActionTone,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Small download stamp on the plate, so a missing rig is obvious at a glance.</summary>
    private static void PaintMissingBadge(Graphics g, Rectangle iconBox)
    {
        var badge = new Rectangle(iconBox.Right - 12, iconBox.Bottom - 12, 14, 14);

        using (var fill = new SolidBrush(Theme.Bg))
        {
            g.FillEllipse(fill, badge);
        }

        using (var edge = new Pen(Theme.Warn, 1.3f))
        {
            g.DrawEllipse(edge, badge);
        }

        float cx = badge.X + (badge.Width / 2f);
        float bottom = badge.Bottom - 3.5f;

        using var arrow = new Pen(Theme.Warn, 1.5f);
        g.DrawLine(arrow, cx, badge.Y + 3.5f, cx, bottom);
        g.DrawLine(arrow, cx - 2.6f, bottom - 2.8f, cx, bottom);
        g.DrawLine(arrow, cx + 2.6f, bottom - 2.8f, cx, bottom);
    }
}

/// <summary>
/// Indeterminate working strip for work that cannot report a percentage. The shell shows it for the
/// life of a BusyScope, so every wait cursor in the suite now comes with a moving bar rather than a
/// frozen window.
/// </summary>
internal sealed class BusyBar : SkinnedControl
{
    private readonly System.Windows.Forms.Timer _sweep = new() { Interval = 40 };
    private float _phase;

    public BusyBar()
    {
        Height = 36;
        Visible = false;
        _sweep.Tick += (_, _) =>
        {
            _phase += 0.024f;
            if (_phase > 1f) _phase -= 1f;
            Invalidate();
        };
    }

    /// <summary>
    /// Shows the strip when the busy form is this control's own form. A modal dialog runs its own
    /// plate, so the shell's bar stays out of its way.
    /// </summary>
    public void Begin(Form? owner)
    {
        if (IsDisposed) return;

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => Begin(owner)));
            }
            catch (InvalidOperationException)
            {
                // Handle went away mid-flight; nothing to show.
            }

            return;
        }

        if (owner is not null && owner != FindForm()) return;

        _phase = 0f;
        Visible = true;
        BringToFront();
        _sweep.Start();
        Invalidate();
    }

    public void End()
    {
        if (IsDisposed) return;

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(End));
            }
            catch (InvalidOperationException)
            {
                // Handle went away mid-flight; nothing to hide.
            }

            return;
        }

        _sweep.Stop();
        Visible = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _sweep.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 80 || Height < 24) return;

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.PaintSurface(
            g,
            new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f),
            8f,
            Theme.Surface2,
            Theme.Alpha(Theme.Accent, 60),
            null);

        TextRenderer.DrawText(
            g,
            "WORKING",
            Theme.Kicker,
            new Rectangle(16, 6, 140, 14),
            Theme.Accent,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(
            g,
            "PLEASE WAIT",
            Theme.Kicker,
            new Rectangle(Width - 156, 6, 140, 14),
            Theme.TextDim,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        var track = new RectangleF(16, Height - 14, Width - 32, 5);
        using (GraphicsPath path = Theme.Round(track, 2.5f))
        {
            using var off = new SolidBrush(Theme.BorderSoft);
            g.FillPath(off, path);
        }

        float segment = Math.Max(60f, track.Width * 0.28f);
        float x = -segment + (_phase * (track.Width + segment));
        float left = Math.Max(track.X, x);
        float right = Math.Min(track.Right, x + segment);
        if (right <= left) return;

        var lit = new RectangleF(left, track.Y, right - left, track.Height);
        using GraphicsPath litPath = Theme.Round(lit, 2.5f);
        using var brush = new LinearGradientBrush(
            new RectangleF(track.X, track.Y, Math.Max(2f, track.Width), track.Height),
            Theme.Alpha(Theme.Accent, 70),
            Theme.Accent,
            LinearGradientMode.Horizontal);
        g.FillPath(brush, litPath);
    }
}

/// <summary>
/// Fixed window pad: a raised 3D square with a single glyph, used for the shell's minimize control
/// so the action carries its own tooltip instead of hiding in stock window chrome.
/// </summary>
internal sealed class ChromeButton : SkinnedControl
{
    private bool _hover;
    private bool _down;

    public ChromeButton()
    {
        Size = new Size(34, 30);
        Cursor = Cursors.Hand;
        Font = Theme.Icon;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Glyph { get; set; } = "\uE921";

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Tone { get; set; } = Theme.TextDim;

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _down = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _down = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _down = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 12 || Height < 12) return;

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using GraphicsPath path = Theme.Round(rect, 7f);

        // Pressed inverts the gradient, so the pad reads as being pushed rather than raised.
        Color top = _down ? Theme.Bg : _hover ? Theme.Surface2 : Color.FromArgb(16, 22, 31);
        Color bottom = _down ? Theme.Surface2 : Color.FromArgb(10, 14, 20);

        using (var fill = new LinearGradientBrush(rect, top, bottom, 90f))
        {
            g.FillPath(fill, path);
        }

        using (var edge = new Pen(
                   new LinearGradientBrush(rect, Theme.Alpha(Theme.Text, _hover ? 90 : 46), Theme.Bg, 90f),
                   1f))
        {
            g.DrawPath(edge, path);
        }

        var glyph = new Rectangle(0, _down ? 1 : 0, Width, Height);
        TextRenderer.DrawText(g, Glyph, Theme.Icon, glyph, _hover ? Theme.Accent : Tone, Centered);
    }
}

