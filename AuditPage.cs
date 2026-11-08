namespace PC_Medic;

/// <summary>
/// The two audits on one page: provably orphaned registry keys on the left, dead shortcut targets on
/// the right. Each half keeps its own scan, selection, backup and plate; nothing is shared but the page.
/// </summary>
internal sealed class AuditPage : PageBase
{
    private readonly BufferedPanel _registryList = new();
    private readonly BufferedPanel _shortcutList = new();
    private readonly ScanOverlay _registryLoading = new() { Visible = false };
    private readonly ScanOverlay _shortcutLoading = new() { Visible = false };
    private readonly Label _registryStatus = new();
    private readonly Label _shortcutStatus = new();
    private readonly Label _footer = new();
    private readonly ToolTip _tips = new();

    private readonly NeonButton _registryScan = Button("RESCAN", width: 78, height: 28);
    private readonly NeonButton _registryAll = Button("ALL", width: 56, height: 28);
    private readonly NeonButton _registryRemove = Button("REMOVE (0)", primary: true, width: 116, height: 28);
    private readonly NeonButton _registryRestore = Button("RESTORE", width: 92, height: 28);

    private readonly NeonButton _shortcutScan = Button("RESCAN", width: 78, height: 28);
    private readonly NeonButton _shortcutAll = Button("ALL", width: 56, height: 28);
    private readonly NeonButton _shortcutKeep = Button("NOT DEAD", width: 104, height: 28);
    private readonly NeonButton _shortcutRecycle = Button("RECYCLE (0)", primary: true, width: 118, height: 28);

    private readonly List<(ToggleRow Row, RegistryIssue Issue)> _registryRows = new();
    private readonly List<(ToggleRow Row, ShortcutEntry Entry)> _shortcutRows = new();

    private List<RegistryIssue> _issues = new();
    private ShortcutScan _scan = new(Array.Empty<ShortcutEntry>(), 0, 0, 0);
    private bool _busyRegistry;
    private bool _busyShortcuts;
    private bool _shortcutsScanned;

    public AuditPage()
        : base(
            "AUDIT",
            "Orphaned registry keys and dead shortcuts, removed per item with a backup first.")
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        Card registryCard = BuildColumn(
            "REGISTRY",
            "ORPHANED ENTRIES",
            _registryList,
            _registryStatus,
            _registryLoading,
            _registryAll,
            _registryRemove,
            _registryRestore,
            _registryScan);
        registryCard.Margin = new Padding(0, 0, 6, 8);

        Card shortcutCard = BuildColumn(
            "SHORTCUTS",
            "DEAD TARGETS",
            _shortcutList,
            _shortcutStatus,
            _shortcutLoading,
            _shortcutAll,
            _shortcutKeep,
            _shortcutRecycle,
            _shortcutScan);
        shortcutCard.Margin = new Padding(6, 0, 0, 8);

        root.Controls.Add(registryCard, 0, 0);
        root.Controls.Add(shortcutCard, 1, 0);

        _footer.AutoSize = false;
        _footer.AutoEllipsis = true;
        _footer.Dock = DockStyle.Fill;
        _footer.Margin = new Padding(2, 0, 0, 0);
        _footer.TextAlign = ContentAlignment.MiddleLeft;
        _footer.Font = Theme.MonoSmall;
        _footer.ForeColor = Theme.TextDim;
        _footer.BackColor = Color.Transparent;
        _footer.Text =
            $"BACKUPS  {Theme.Truncate(RegistryService.BackupRoot, 76)}" +
            "  \u2022  RECYCLED SHORTCUTS RESTORE FROM THE BIN";
        _tips.SetToolTip(
            _footer,
            $"{RegistryService.BackupRoot}\r\nRecycled shortcuts restore from the Recycle Bin; kept shortcuts are remembered.");
        root.Controls.Add(_footer, 0, 1);
        root.SetColumnSpan(_footer, 2);

        Controls.Add(root);

        _registryStatus.Text = "NOT SCANNED";
        _shortcutStatus.Text = "NOT SCANNED";

        _registryScan.Click += (_, _) => ReloadRegistry(announce: true);
        _registryAll.Click += (_, _) => ToggleAllRegistry();
        _registryRemove.Click += (_, _) => RemoveRegistrySelection();
        _registryRestore.Click += (_, _) => RestoreRegistryBackup();

        _shortcutScan.Click += (_, _) => ReloadShortcuts(announce: true);
        _shortcutAll.Click += (_, _) => ToggleAllShortcuts();
        _shortcutKeep.Click += (_, _) => KeepShortcuts();
        _shortcutRecycle.Click += (_, _) => RecycleShortcuts();

        _registryList.Resize += (_, _) => ResizeRows(_registryList);
        _shortcutList.Resize += (_, _) => ResizeRows(_shortcutList);

        SyncRegistryButtons();
        SyncShortcutButtons();
    }

    /// <summary>The registry half rescans on every visit; the shortcut walk is reused once it has run.</summary>
    public override void OnNavigated()
    {
        ReloadRegistry(announce: false);

        if (_shortcutsScanned) return;
        _shortcutsScanned = true;
        ReloadShortcuts(announce: false);
    }

    public override void RefreshData() => OnNavigated();

    /// <summary>A card with a 34 px toolbar (status plus buttons) over the scrolling finding list.</summary>
    private Card BuildColumn(
        string header,
        string kicker,
        BufferedPanel list,
        Label status,
        ScanOverlay loading,
        params NeonButton[] buttons)
    {
        Card card = Panel(header, kicker);
        card.Margin = new Padding(0);

        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
        };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // Two toolbar rows: the status gets the full width, the buttons sit underneath. Four pills and a
        // status line do not share one 34 px strip at half-page width.
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Margin = new Padding(0) };

        status.AutoSize = false;
        status.AutoEllipsis = true;
        status.Font = Theme.MonoSmall;
        status.ForeColor = Theme.TextDim;
        status.BackColor = Color.Transparent;
        status.TextAlign = ContentAlignment.MiddleLeft;

        toolbar.Controls.Add(status);
        foreach (NeonButton button in buttons) toolbar.Controls.Add(button);

        void LayoutToolbar()
        {
            int right = toolbar.ClientSize.Width;

            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                NeonButton button = buttons[i];
                right -= button.Width;
                button.SetBounds(right, 26, button.Width, 28);
                right -= 8;
            }

            // Bounded, not auto-sized: a long note must ellipsise rather than run off the card.
            status.SetBounds(2, 4, Math.Max(90, toolbar.ClientSize.Width - 6), 18);
        }

        toolbar.Resize += (_, _) => LayoutToolbar();
        LayoutToolbar();

        list.Dock = DockStyle.Fill;
        list.AutoScroll = true;
        list.BackColor = Theme.Surface;
        list.Margin = new Padding(0);

        inner.Controls.Add(toolbar, 0, 0);
        inner.Controls.Add(list, 0, 1);

        // A sibling of the table, not a child: the layout engine would place and size it as a cell.
        card.Controls.Add(loading);

        void LayoutLoading()
        {
            if (card.IsDisposed || list.IsDisposed || !list.IsHandleCreated) return;
            loading.Bounds = new Rectangle(card.PointToClient(list.PointToScreen(Point.Empty)), list.Size);
        }

        list.SizeChanged += (_, _) => LayoutLoading();
        card.Layout += (_, _) => LayoutLoading();
        LayoutLoading();
        loading.BringToFront();

        card.Controls.Add(inner);

        return card;
    }

    private static void ResizeRows(Panel list)
    {
        foreach (Control control in list.Controls)
        {
            if (control is ToggleRow row) row.Width = Math.Max(200, list.ClientSize.Width - 24);
        }
    }

    private static int RowWidth(Panel list) => Math.Max(200, list.ClientSize.Width - 24);

    // ---------------------------------------------------------------- registry half

    private async void ReloadRegistry(bool announce, string? note = null)
    {
        if (_busyRegistry || IsDisposed) return;

        _busyRegistry = true;
        SetRegistryButtons(false);
        _registryStatus.Text = "SCANNING HKLM + HKCU\u2026";
        _registryStatus.ForeColor = Theme.TextDim;
        using IDisposable busy = Working();

        var progress = new Progress<int>(percent =>
        {
            if (!IsDisposed) _registryLoading.Percent = percent;
        });

        _registryLoading.Percent = 0;
        _registryLoading.Visible = true;
        _registryLoading.BringToFront();

        try
        {
            List<RegistryIssue> issues = await Task.Run(() => RegistryService.Scan(progress));
            _issues = issues;
            RebuildRegistry();

            if (announce)
            {
                ActivityLog.Info(
                    $"Registry audit: {issues.Count} findings, {issues.Count(issue => !issue.ReviewOnly)} orphaned.");
            }
        }
        catch (Exception ex)
        {
            ActivityLog.Error($"Registry audit failed: {ex.Message}");
            note = ex.Message;
        }
        finally
        {
            _registryLoading.Visible = false;
            _busyRegistry = false;
            SetRegistryButtons(true);
            UpdateRegistryStatus(note);
        }
    }

    private void RebuildRegistry()
    {
        _registryList.SuspendLayout();
        foreach (Control control in _registryList.Controls.Cast<Control>().ToArray()) control.Dispose();
        _registryList.Controls.Clear();
        _registryRows.Clear();

        int y = 6;

        if (_issues.Count == 0)
        {
            y = AddEmpty(_registryList, "No orphaned keys found \u2014 nothing here needs touching.", y);
        }

        foreach (IGrouping<RegistryCategory, RegistryIssue> group in _issues.GroupBy(issue => issue.Category))
        {
            AddGroupHeader(_registryList, $"{group.First().CategoryLabel}  \u2022  {group.Count()}", ref y);

            foreach (RegistryIssue issue in group)
            {
                ToggleRow row = BuildRegistryRow(issue);
                row.SetBounds(4, y, RowWidth(_registryList), 62);
                _registryList.Controls.Add(row);
                _registryRows.Add((row, issue));
                y += 68;
            }

            y += 6;
        }

        _registryList.AutoScrollMinSize = new Size(0, y + 6);
        _registryList.ResumeLayout();
        SyncRegistryButtons();
    }

    private ToggleRow BuildRegistryRow(RegistryIssue issue)
    {
        string badge = issue.Category switch
        {
            RegistryCategory.Uninstall => "KEY",
            RegistryCategory.AppPath => "PATH",
            RegistryCategory.SharedDll => "DLL",
            _ => "RUN",
        };

        var row = new ToggleRow
        {
            Title = Theme.Truncate(issue.Display, 48),
            Subtitle = $"MISSING  {Theme.Truncate(issue.MissingPath, 80)}",
            BadgeText = issue.ReviewOnly ? "REVIEW" : badge,
            BadgeTone = issue.ReviewOnly ? Theme.TextFaint : issue.CategoryTone,
            Tone = issue.CategoryTone,
            Checked = false,
            Enabled = !issue.ReviewOnly,
            Tag = issue,
        };

        row.Toggled += (_, _) => SyncRegistryButtons();

        _tips.SetToolTip(
            row,
            $"{issue.Display}\r\n{issue.RegistryPath}\r\nMissing: {issue.MissingPath}" +
            (issue.ReviewOnly ? "\r\nManage this on the Startup Apps page." : string.Empty));

        return row;
    }

    private void ToggleAllRegistry()
    {
        List<ToggleRow> rows = _registryRows
            .Where(pair => !pair.Issue.ReviewOnly)
            .Select(pair => pair.Row)
            .ToList();

        if (rows.Count == 0) return;

        bool select = rows.Any(row => !row.Checked);
        foreach (ToggleRow row in rows) row.Checked = select;
        SyncRegistryButtons();
    }

    private void RemoveRegistrySelection()
    {
        List<RegistryIssue> selected = _registryRows
            .Where(pair => pair.Row.Checked)
            .Select(pair => pair.Issue)
            .ToList();

        if (selected.Count == 0)
        {
            UpdateRegistryStatus("tick the entries to remove first", Theme.Warn);
            return;
        }

        (bool ok, string message) = RegistryService.Remove(selected);

        if (!ok)
        {
            ActivityLog.Error($"Registry clean: {message}");
            UpdateRegistryStatus(message, Theme.Bad);
            return;
        }

        ActivityLog.Success($"Registry clean: {message}");
        ReloadRegistry(announce: false, note: message);
    }

    private void RestoreRegistryBackup()
    {
        (bool ok, string message) = RegistryService.RestoreLatest();

        if (ok) ActivityLog.Success($"Registry restore: {message}");
        else ActivityLog.Warn($"Registry restore: {message}");

        ReloadRegistry(announce: false, note: message);

        if (!ok) UpdateRegistryStatus(message, Theme.Warn);
    }

    private void SetRegistryButtons(bool enabled)
    {
        _registryScan.Enabled = enabled;
        _registryAll.Enabled = enabled;
        _registryRestore.Enabled = enabled && RegistryService.LatestBackupFolder() is not null;
        SyncRegistryButtons();
    }

    private void SyncRegistryButtons()
    {
        int selected = _registryRows.Count(pair => pair.Row.Checked);
        _registryRemove.Text = selected == 0 ? "REMOVE" : $"REMOVE ({selected})";
        _registryRemove.Enabled = !_busyRegistry && selected > 0;
        _registryRemove.Invalidate();
        _tips.SetToolTip(_registryRemove, "Writes a .reg backup, then removes the ticked keys.");
    }

    private void UpdateRegistryStatus(string? note = null, Color? tone = null)
    {
        int orphaned = _issues.Count(issue => !issue.ReviewOnly);
        int review = _issues.Count - orphaned;

        _registryStatus.Text =
            $"{_issues.Count} FINDINGS  \u2022  {orphaned} ORPHANED  \u2022  {review} REVIEW" +
            (string.IsNullOrEmpty(note) ? string.Empty : $"  \u2022  {Theme.Truncate(note.ToUpperInvariant(), 26)}");

        _registryStatus.ForeColor = tone ?? Theme.TextDim;
        _tips.SetToolTip(
            _registryStatus,
            $"{_issues.Count} findings, {orphaned} provably orphaned, {review} review only\r\n" +
            (Native.IsElevated ? "ELEVATED SESSION" : "STANDARD USER") +
            (string.IsNullOrEmpty(note) ? string.Empty : $"\r\n{note}"));
    }

    // ---------------------------------------------------------------- shortcut half

    private async void ReloadShortcuts(bool announce, string? note = null)
    {
        if (_busyShortcuts || IsDisposed) return;

        _busyShortcuts = true;
        SetShortcutButtons(false);
        _shortcutStatus.Text = "RESOLVING .LNK TARGETS\u2026";
        _shortcutStatus.ForeColor = Theme.TextDim;
        using IDisposable busy = Working();

        _shortcutLoading.Percent = 0;
        _shortcutLoading.Visible = true;
        _shortcutLoading.BringToFront();

        var progress = new Progress<int>(percent =>
        {
            if (!IsDisposed) _shortcutLoading.Percent = percent;
        });

        try
        {
            ShortcutScan scan = await ShortcutService.ScanAsync(progress);
            _scan = scan;
            RebuildShortcuts();

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
            _shortcutLoading.Visible = false;
            _busyShortcuts = false;
            SetShortcutButtons(true);
            UpdateShortcutStatus(note);
        }
    }

    private void RebuildShortcuts()
    {
        _shortcutList.SuspendLayout();
        foreach (Control control in _shortcutList.Controls.Cast<Control>().ToArray()) control.Dispose();
        _shortcutList.Controls.Clear();
        _shortcutRows.Clear();

        int y = 6;

        if (_scan.Entries.Count == 0)
        {
            y = AddEmpty(_shortcutList, "No dead shortcuts found \u2014 every .lnk still resolves.", y);
        }

        foreach (IGrouping<string, ShortcutEntry> group in _scan.Entries.GroupBy(entry => entry.Area))
        {
            AddGroupHeader(_shortcutList, $"{group.Key}  \u2022  {group.Count()}", ref y);

            foreach (ShortcutEntry entry in group)
            {
                ToggleRow row = BuildShortcutRow(entry);
                row.SetBounds(4, y, RowWidth(_shortcutList), 62);
                _shortcutList.Controls.Add(row);
                _shortcutRows.Add((row, entry));
                y += 68;
            }

            y += 6;
        }

        _shortcutList.AutoScrollMinSize = new Size(0, y + 6);
        _shortcutList.ResumeLayout();
        SyncShortcutButtons();
    }

    private ToggleRow BuildShortcutRow(ShortcutEntry entry)
    {
        bool dead = entry.Status == ShortcutStatus.Broken;

        var row = new ToggleRow
        {
            Title = Theme.Truncate(entry.Name, 48),
            Subtitle = dead
                ? $"MISSING  {Theme.Truncate(entry.Target, 80)}"
                : $"REVIEW  {Theme.Truncate(entry.Detail, 80)}",
            BadgeText = dead ? "DEAD" : "REVIEW",
            BadgeTone = dead ? Theme.Bad : Theme.TextDim,
            Tone = dead ? Theme.Bad : Theme.TextDim,
            Checked = false,
            // Review rows stay clickable so their folder opens, but their switch must not move.
            Locked = !dead,
            Clickable = true,
            Tag = entry,
        };

        row.Toggled += (_, _) => SyncShortcutButtons();
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

    private void ToggleAllShortcuts()
    {
        List<ToggleRow> rows = _shortcutRows
            .Where(pair => pair.Entry.Status == ShortcutStatus.Broken)
            .Select(pair => pair.Row)
            .ToList();

        if (rows.Count == 0) return;

        bool select = rows.Any(row => !row.Checked);
        foreach (ToggleRow row in rows) row.Checked = select;
        SyncShortcutButtons();
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
    private void KeepShortcuts()
    {
        List<ShortcutEntry> selected = _shortcutRows.Where(pair => pair.Row.Checked).Select(pair => pair.Entry).ToList();

        if (selected.Count == 0)
        {
            UpdateShortcutStatus("tick the shortcuts that still work first", Theme.Warn);
            return;
        }

        (bool ok, string message) = ShortcutService.Keep(selected);
        ActivityLog.Write(ok ? LogLevel.Success : LogLevel.Warn, message);

        if (!ok)
        {
            UpdateShortcutStatus(message, Theme.Bad);
            return;
        }

        _scan = new ShortcutScan(
            _scan.Entries.Where(entry => !selected.Contains(entry)).ToList(),
            _scan.Scanned,
            _scan.Healthy,
            _scan.Suppressed + selected.Count);

        RebuildShortcuts();
        UpdateShortcutStatus(message, Theme.Good);
    }

    private void RecycleShortcuts()
    {
        List<ShortcutEntry> selected = _shortcutRows.Where(pair => pair.Row.Checked).Select(pair => pair.Entry).ToList();

        if (selected.Count == 0)
        {
            UpdateShortcutStatus("tick the shortcuts to recycle first", Theme.Warn);
            return;
        }

        (bool ok, string message) = ShortcutService.Recycle(selected);

        if (!ok)
        {
            ActivityLog.Error($"Shortcut clean: {message}");
            UpdateShortcutStatus(message, Theme.Bad);
            return;
        }

        ActivityLog.Success($"Shortcut clean: {message}");
        ReloadShortcuts(announce: false, note: message);
    }

    private void SetShortcutButtons(bool enabled)
    {
        _shortcutScan.Enabled = enabled;
        _shortcutAll.Enabled = enabled;
        SyncShortcutButtons();
    }

    private void SyncShortcutButtons()
    {
        int selected = _shortcutRows.Count(pair => pair.Row.Checked);
        _shortcutRecycle.Text = selected == 0 ? "RECYCLE" : $"RECYCLE ({selected})";
        _shortcutKeep.Text = selected == 0 ? "NOT DEAD" : $"NOT DEAD ({selected})";
        _shortcutRecycle.Enabled = !_busyShortcuts && selected > 0;
        _shortcutKeep.Enabled = !_busyShortcuts && selected > 0;
        _shortcutRecycle.Invalidate();
        _shortcutKeep.Invalidate();
        _tips.SetToolTip(_shortcutRecycle, "Sends the ticked shortcuts to the Recycle Bin.");
        _tips.SetToolTip(_shortcutKeep, "Remembers the ticked shortcuts as working and hides them from later scans.");
    }

    private void UpdateShortcutStatus(string? note = null, Color? tone = null)
    {
        int dead = _scan.Entries.Count(entry => entry.Status == ShortcutStatus.Broken);
        int review = _scan.Entries.Count - dead;

        _shortcutStatus.Text =
            $"{dead} DEAD  \u2022  {review} REVIEW  \u2022  {_scan.Suppressed} KEPT" +
            (string.IsNullOrEmpty(note) ? string.Empty : $"  \u2022  {Theme.Truncate(note.ToUpperInvariant(), 22)}");

        _shortcutStatus.ForeColor = tone ?? Theme.TextDim;
        _tips.SetToolTip(
            _shortcutStatus,
            $"{_scan.Scanned} shortcuts resolved, {_scan.Healthy} healthy\r\n{_shortcutStatus.Text}");
    }

    // ---------------------------------------------------------------- shared list furniture

    private static int AddEmpty(Panel list, string text, int y)
    {
        var label = new Label
        {
            Text = text,
            Font = Theme.MonoSmall,
            ForeColor = Theme.TextDim,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(8, y + 8),
        };

        list.Controls.Add(label);
        return y + 40;
    }

    private static void AddGroupHeader(Panel list, string text, ref int y)
    {
        var header = new Label
        {
            Text = text,
            Font = Theme.Kicker,
            ForeColor = Theme.TextDim,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(8, y + 4),
        };

        list.Controls.Add(header);
        y += 26;
    }
}
