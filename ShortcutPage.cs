namespace PC_Medic;

/// <summary>
/// Broken-shortcut audit page. Lists only shortcuts whose target is provably gone, lets the operator
/// tick exactly what goes, and sends the ticked files to the Recycle Bin rather than deleting them.
/// </summary>
internal sealed class ShortcutPage : PageBase
{
    private readonly BufferedPanel _list = new();
    private readonly Label _status = new();
    private readonly Label _footer = new();
    private readonly ToolTip _tips = new();
    private readonly NeonButton _rescan = Button("RESCAN", width: 92, height: 30);
    private readonly NeonButton _selectAll = Button("SELECT ALL", width: 104, height: 30);
    private readonly NeonButton _keep = Button("MARK NOT DEAD", width: 166, height: 30);
    private readonly NeonButton _recycle = Button("RECYCLE TO BIN", primary: true, width: 172, height: 30);
    private readonly List<(ToggleRow Row, ShortcutEntry Entry)> _rows = new();
    private ShortcutScan _scan = new(Array.Empty<ShortcutEntry>(), 0, 0, 0);
    private bool _busy;
    private bool _scanned;

    public ShortcutPage()
        : base("SHORTCUT AUDIT", "Dead .lnk targets only \u2014 click a row to inspect it, then recycle it or mark it as not dead.")
    {
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Bg };

        // Bounded, not auto-sized: a long note must ellipsise instead of sliding under the toolbar buttons.
        _status.AutoSize = false;
        _status.AutoEllipsis = true;
        _status.Font = Theme.MonoSmall;
        _status.ForeColor = Theme.TextDim;
        _status.BackColor = Color.Transparent;
        _status.Location = new Point(2, 13);
        toolbar.Controls.Add(_status);

        foreach (NeonButton button in new[] { _rescan, _selectAll, _keep, _recycle }) toolbar.Controls.Add(button);

        void LayoutToolbar()
        {
            int right = toolbar.ClientSize.Width;
            right -= _rescan.Width;
            _rescan.SetBounds(right, 4, _rescan.Width, 30);
            right -= _selectAll.Width + 8;
            _selectAll.SetBounds(right, 4, _selectAll.Width, 30);
            right -= _keep.Width + 8;
            _keep.SetBounds(right, 4, _keep.Width, 30);
            right -= _recycle.Width + 8;
            _recycle.SetBounds(right, 4, _recycle.Width, 30);
            _status.SetBounds(2, 13, Math.Max(90, _recycle.Left - 14), 16);
        }

        toolbar.Resize += (_, _) => LayoutToolbar();
        LayoutToolbar();

        Card card = Panel("FINDINGS", "DEAD SHORTCUTS");
        _list.Dock = DockStyle.Fill;
        _list.AutoScroll = true;
        _list.BackColor = Theme.Surface;
        card.Controls.Add(_list);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 26, BackColor = Theme.Bg };
        _footer.AutoSize = true;
        _footer.Font = Theme.MonoSmall;
        _footer.ForeColor = Theme.TextDim;
        _footer.BackColor = Color.Transparent;
        _footer.Location = new Point(2, 6);
        footer.Controls.Add(_footer);

        Controls.Add(card);
        Controls.Add(footer);
        Controls.Add(toolbar);

        _rescan.Click += (_, _) => Reload(announce: true);
        _selectAll.Click += (_, _) => ToggleAll();
        _keep.Click += (_, _) => KeepSelected();
        _recycle.Click += (_, _) => RecycleSelected();

        _list.Resize += (_, _) =>
        {
            foreach (Control control in _list.Controls)
            {
                if (control is ToggleRow row) row.Width = RowWidth();
            }
        };

        _footer.Text = "CLICK A ROW TO OPEN ITS FOLDER  \u2022  RECYCLED ITEMS RESTORE FROM WINDOWS  \u2022  DESKTOP, START MENU AND STARTUP";
    }

    /// <summary>One pass is enough: the folder tree can hold tens of thousands of links, so navigation reuses the result.</summary>
    public override void OnNavigated()
    {
        if (_scanned) return;
        _scanned = true;
        Reload(announce: false);
    }

    public override void RefreshData()
    {
        if (_scanned) return;
        _scanned = true;
        Reload(announce: false);
    }

    private int RowWidth() => Math.Max(240, _list.ClientSize.Width - 24);

    private async void Reload(bool announce, string? note = null)
    {
        if (_busy) return;

        _busy = true;
        _status.Text = "RESOLVING .LNK TARGETS\u2026";
        using IDisposable busy = Working();
        _status.ForeColor = Theme.TextDim;
        SetButtonsEnabled(false);

        try
        {
            ShortcutScan scan = await ShortcutService.ScanAsync();
            _scan = scan;
            Rebuild();

            if (announce)
            {
                int dead = scan.Entries.Count(entry => entry.Status == ShortcutStatus.Broken);
                ActivityLog.Info($"Shortcut audit: {scan.Scanned} resolved, {dead} dead, {scan.Healthy} healthy.");
            }
        }
        catch (Exception ex)
        {
            ActivityLog.Error($"Shortcut audit failed: {ex.Message}");
            note = ex.Message;
        }
        finally
        {
            _busy = false;
            SetButtonsEnabled(true);
            UpdateStatus(note);
        }
    }

    private void Rebuild()
    {
        _list.SuspendLayout();
        foreach (Control control in _list.Controls.Cast<Control>().ToArray()) control.Dispose();
        _list.Controls.Clear();
        _rows.Clear();

        int y = 6;

        if (_scan.Entries.Count == 0)
        {
            var empty = new Label
            {
                Text = "No dead shortcuts found \u2014 every .lnk still resolves to a real target.",
                Font = Theme.MonoSmall,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(8, 12),
            };

            _list.Controls.Add(empty);
            y += 40;
        }

        foreach (IGrouping<string, ShortcutEntry> group in _scan.Entries.GroupBy(entry => entry.Area))
        {
            var header = new Label
            {
                Text = $"{group.Key}  \u2022  {group.Count()}",
                Font = Theme.Kicker,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(8, y + 4),
            };

            _list.Controls.Add(header);
            y += 26;

            foreach (ShortcutEntry entry in group)
            {
                ToggleRow row = BuildRow(entry);
                row.SetBounds(4, y, RowWidth(), 62);
                _list.Controls.Add(row);
                _rows.Add((row, entry));
                y += 68;
            }

            y += 6;
        }

        _list.AutoScrollMinSize = new Size(0, y + 6);
        _list.ResumeLayout();
        SyncButtons();
    }

    private ToggleRow BuildRow(ShortcutEntry entry)
    {
        bool dead = entry.Status == ShortcutStatus.Broken;

        var row = new ToggleRow
        {
            Title = Theme.Truncate(entry.Name, 70),
            Subtitle = dead
                ? $"MISSING  {Theme.Truncate(entry.Target, 110)}"
                : $"REVIEW  {Theme.Truncate(entry.Detail, 110)}",
            BadgeText = dead ? "DEAD" : "REVIEW",
            BadgeTone = dead ? Theme.Bad : Theme.TextDim,
            Tone = dead ? Theme.Bad : Theme.TextDim,
            Checked = false,
            // Review rows stay clickable so their folder opens, but their switch must not move.
            Locked = !dead,
            Clickable = true,
            Tag = entry,
        };

        row.Toggled += (_, _) => SyncButtons();
        row.RowClicked += _ => OpenFolder(entry);

        _tips.SetToolTip(
            row,
            $"{entry.FilePath}\r\n" +
            (string.IsNullOrEmpty(entry.Target) ? entry.Detail : $"Target: {entry.Target}") +
            (dead
                ? "\r\nClick the row to open its folder; tick it to recycle it or mark it not dead."
                : "\r\nClick the row to open its folder. It cannot be ticked: no target was resolved."));

        return row;
    }

    private void ToggleAll()
    {
        List<ToggleRow> rows = _rows
            .Where(pair => pair.Entry.Status == ShortcutStatus.Broken)
            .Select(pair => pair.Row)
            .ToList();

        if (rows.Count == 0) return;

        bool select = rows.Any(row => !row.Checked);
        foreach (ToggleRow row in rows) row.Checked = select;
        SyncButtons();
    }

    /// <summary>Clicking a row opens its folder, so the entry can be checked by eye before a decision.</summary>
    private static void OpenFolder(ShortcutEntry entry)
    {
        string? folder = Path.GetDirectoryName(entry.FilePath);

        if (folder is null || !Native.OpenShell(folder))
        {
            ActivityLog.Warn($"The folder for {entry.Name} could not be opened.");
            return;
        }

        ActivityLog.Info($"Opened {folder} for {entry.Name}.");
    }

    /// <summary>Marks ticked entries as fine and drops them from the list without rescanning.</summary>
    private void KeepSelected()
    {
        List<ShortcutEntry> selected = _rows.Where(pair => pair.Row.Checked).Select(pair => pair.Entry).ToList();

        if (selected.Count == 0)
        {
            UpdateStatus("tick the shortcuts that still work first", Theme.Warn);
            return;
        }

        (bool ok, string message) = ShortcutService.Keep(selected);
        ActivityLog.Write(ok ? LogLevel.Success : LogLevel.Warn, message);

        if (!ok)
        {
            UpdateStatus(message, Theme.Bad);
            return;
        }

        _scan = new ShortcutScan(
            _scan.Entries.Where(entry => !selected.Contains(entry)).ToList(),
            _scan.Scanned,
            _scan.Healthy,
            _scan.Suppressed + selected.Count);

        Rebuild();
        UpdateStatus(message, Theme.Good);
    }

    private void RecycleSelected()
    {
        List<ShortcutEntry> selected = _rows.Where(pair => pair.Row.Checked).Select(pair => pair.Entry).ToList();

        if (selected.Count == 0)
        {
            UpdateStatus("tick the shortcuts to recycle first", Theme.Warn);
            return;
        }

        (bool ok, string message) = ShortcutService.Recycle(selected);

        if (!ok)
        {
            ActivityLog.Error($"Shortcut clean: {message}");
            UpdateStatus(message, Theme.Bad);
            return;
        }

        ActivityLog.Success($"Shortcut clean: {message}");
        Reload(announce: false, note: message);
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _rescan.Enabled = enabled;
        _selectAll.Enabled = enabled;
        SyncButtons();
    }

    private void SyncButtons()
    {
        int selected = _rows.Count(pair => pair.Row.Checked);
        _recycle.Text = selected == 0 ? "RECYCLE TO BIN" : $"RECYCLE TO BIN ({selected})";
        _keep.Text = selected == 0 ? "MARK NOT DEAD" : $"MARK NOT DEAD ({selected})";
        _recycle.Enabled = !_busy && selected > 0;
        _keep.Enabled = !_busy && selected > 0;
        _recycle.Invalidate();
        _keep.Invalidate();
    }

    private void UpdateStatus(string? note = null, Color? tone = null)
    {
        int dead = _scan.Entries.Count(entry => entry.Status == ShortcutStatus.Broken);
        int review = _scan.Entries.Count - dead;

        _status.Text =
            $"{dead} DEAD  \u2022  {review} REVIEW  \u2022  {_scan.Suppressed} KEPT  \u2022  {_scan.Healthy} HEALTHY" +
            (string.IsNullOrEmpty(note) ? string.Empty : $"  \u2022  {Theme.Truncate(note.ToUpperInvariant(), 44)}");

        _status.ForeColor = tone ?? Theme.TextDim;
        _tips.SetToolTip(_status, $"{_scan.Scanned} shortcuts resolved\r\n{_status.Text}");
    }
}
