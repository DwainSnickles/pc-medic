namespace PC_Medic;

internal sealed class StartupPage : PageBase
{
    private readonly Panel _toolbar = new();
    private readonly BufferedPanel _list = new();
    private readonly ToolTip _tips = new();
    private readonly Label _status = new();
    private readonly NeonButton _all = Button("ALL", width: 68);
    private readonly NeonButton _enabled = Button("ENABLED", width: 88);
    private readonly NeonButton _disabled = Button("DISABLED", width: 90);
    private readonly NeonButton _rescan = Button("RESCAN", width: 90);
    private List<StartupEntry> _entries = new();
    private int _filter;

    public StartupPage()
        : base("STARTUP APPS", "Disable boot bloat without losing the ability to switch it back on.")
    {
        _toolbar.Dock = DockStyle.Top;
        _toolbar.Height = 40;
        _toolbar.BackColor = Theme.Bg;

        _status.AutoSize = true;
        _status.Font = Theme.MonoSmall;
        _status.ForeColor = Theme.TextDim;
        _status.BackColor = Color.Transparent;
        _status.Location = new Point(2, 13);
        _toolbar.Controls.Add(_status);

        NeonButton[] buttons = { _all, _enabled, _disabled, _rescan };
        foreach (NeonButton button in buttons) _toolbar.Controls.Add(button);

        void LayoutToolbar()
        {
            int right = _toolbar.ClientSize.Width;
            right -= _rescan.Width;
            _rescan.SetBounds(right, 4, _rescan.Width, 30);
            right -= _disabled.Width + 8;
            _disabled.SetBounds(right, 4, _disabled.Width, 30);
            right -= _enabled.Width + 8;
            _enabled.SetBounds(right, 4, _enabled.Width, 30);
            right -= _all.Width + 8;
            _all.SetBounds(right, 4, _all.Width, 30);
        }

        _toolbar.Resize += (_, _) => LayoutToolbar();
        LayoutToolbar();

        Card card = Panel("DETECTED ENTRIES", "AUTORUN SURFACE");
        _list.Dock = DockStyle.Fill;
        _list.AutoScroll = true;
        _list.BackColor = Theme.Surface;
        card.Controls.Add(_list);

        Controls.Add(card);
        Controls.Add(_toolbar);

        _all.Click += (_, _) => SetFilter(0);
        _enabled.Click += (_, _) => SetFilter(1);
        _disabled.Click += (_, _) => SetFilter(2);
        _rescan.Click += (_, _) => Reload();

        _list.Resize += (_, _) =>
        {
            foreach (Control control in _list.Controls)
            {
                control.Width = RowWidth();
            }
        };
    }

    public override void OnNavigated() => Reload(announce: false);

    public override void RefreshData() => Reload(announce: false);

    private int RowWidth() => Math.Max(220, _list.ClientSize.Width - 24);

    private void SetFilter(int filter)
    {
        _filter = filter;
        SyncFilterButtons();
        Rebuild();
    }

    private void SyncFilterButtons()
    {
        _all.Primary = _filter == 0;
        _enabled.Primary = _filter == 1;
        _disabled.Primary = _filter == 2;
        _all.Invalidate();
        _enabled.Invalidate();
        _disabled.Invalidate();
    }

    private void Reload(bool announce = true)
    {
        _entries = StartupService.Load();
        Rebuild();
        SyncFilterButtons();
        UpdateStatus();

        if (announce)
        {
            ActivityLog.Info($"Startup scan: {_entries.Count} entries, {_entries.Count(e => e.Enabled)} enabled.");
        }
    }

    private void Rebuild()
    {
        _list.SuspendLayout();
        foreach (Control control in _list.Controls.Cast<Control>().ToArray()) control.Dispose();
        _list.Controls.Clear();

        List<StartupEntry> visible = _entries
            .Where(entry => _filter switch
            {
                1 => entry.Enabled,
                2 => !entry.Enabled,
                _ => true,
            })
            .ToList();

        int y = 6;
        if (visible.Count == 0)
        {
            var empty = new Label
            {
                Text = "Nothing matches this filter.",
                Font = Theme.MonoSmall,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(8, 12),
            };
            _list.Controls.Add(empty);
        }

        foreach (StartupEntry entry in visible)
        {
            ToggleRow row = BuildRow(entry);
            row.SetBounds(4, y, RowWidth(), 62);
            _list.Controls.Add(row);
            y += 68;
        }

        _list.AutoScrollMinSize = new Size(0, y + 6);
        _list.ResumeLayout();
    }

    private ToggleRow BuildRow(StartupEntry entry)
    {
        bool locked = entry.MachineWide && !Native.IsElevated;
        string subtitle = $"{entry.Source}  \u2022  {entry.Command}";
        if (locked) subtitle += "  \u2022  needs admin";

        var row = new ToggleRow
        {
            Title = entry.Name,
            Subtitle = subtitle,
            BadgeText = entry.ImpactLabel,
            BadgeTone = entry.ImpactTone,
            Tone = entry.Enabled ? Theme.Accent : Theme.TextDim,
            Checked = entry.Enabled,
            Enabled = !locked,
            Tag = entry,
        };

        row.Toggled += OnRowToggled;

        _tips.SetToolTip(
            row,
            locked
                ? $"{entry.Name}\r\n{entry.Source}\r\n{entry.Command}\r\nNeeds an elevated run to change."
                : $"{entry.Name}\r\n{entry.Source}\r\n{entry.Command}");

        return row;
    }

    private void OnRowToggled(ToggleRow row, bool enabled)
    {
        if (row.Tag is not StartupEntry entry) return;

        (bool ok, string message) = StartupService.SetEnabled(entry, enabled);
        if (!ok)
        {
            row.Checked = entry.Enabled;
            ActivityLog.Error($"{entry.Name}: {message}");
            UpdateStatus(message, Theme.Bad);
            return;
        }

        entry.Enabled = enabled;
        row.Tone = enabled ? Theme.Accent : Theme.TextDim;
        row.Invalidate();
        ActivityLog.Success($"{entry.Name} {message.ToLowerInvariant()}.");
        UpdateStatus($"{entry.Name} {message.ToLowerInvariant()}", Theme.Good);
    }

    private void UpdateStatus(string? message = null, Color? tone = null)
    {
        int enabledCount = _entries.Count(e => e.Enabled);
        string scope = Native.IsElevated ? "elevated" : "standard user";
        _status.Text =
            $"{_entries.Count} ENTRIES  \u2022  {enabledCount} ENABLED  \u2022  {_entries.Count - enabledCount} DISABLED  \u2022  {scope.ToUpperInvariant()}" +
            (message is null ? string.Empty : $"  \u2022  {message.ToUpperInvariant()}");
        _status.ForeColor = tone ?? Theme.TextDim;
    }
}

