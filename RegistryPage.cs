namespace PC_Medic;

/// <summary>
/// Registry audit page. Shows only entries whose target file is provably gone, lets the
/// operator tick exactly what goes, and exports a .reg backup before anything is deleted.
/// </summary>
internal sealed class RegistryPage : PageBase
{
    private readonly BufferedPanel _list = new();
    private readonly Label _status = new();
    private readonly Label _footer = new();
    private readonly ToolTip _tips = new();
    private readonly NeonButton _rescan = Button("RESCAN", width: 92, height: 30);
    private readonly NeonButton _selectAll = Button("SELECT ALL", width: 104, height: 30);
    private readonly NeonButton _remove = Button("BACK UP + REMOVE", primary: true, width: 208, height: 30);
    private readonly NeonButton _restore = Button("RESTORE BACKUP", width: 156, height: 30);
    private readonly List<(ToggleRow Row, RegistryIssue Issue)> _rows = new();
    private List<RegistryIssue> _issues = new();
    private bool _busy;

    public RegistryPage()
        : base("REGISTRY AUDIT", "Provably orphaned keys only \u2014 a .reg backup is written before anything is removed.")
    {
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Bg };

        _status.AutoSize = true;
        _status.Font = Theme.MonoSmall;
        _status.ForeColor = Theme.TextDim;
        _status.BackColor = Color.Transparent;
        _status.Location = new Point(2, 13);
        toolbar.Controls.Add(_status);

        NeonButton[] buttons = { _rescan, _selectAll, _remove, _restore };
        foreach (NeonButton button in buttons) toolbar.Controls.Add(button);

        void LayoutToolbar()
        {
            int right = toolbar.ClientSize.Width;
            right -= _rescan.Width;
            _rescan.SetBounds(right, 4, _rescan.Width, 30);
            right -= _selectAll.Width + 8;
            _selectAll.SetBounds(right, 4, _selectAll.Width, 30);
            right -= _remove.Width + 8;
            _remove.SetBounds(right, 4, _remove.Width, 30);
            right -= _restore.Width + 8;
            _restore.SetBounds(right, 4, _restore.Width, 30);
        }

        toolbar.Resize += (_, _) => LayoutToolbar();
        LayoutToolbar();

        Card card = Panel("FINDINGS", "ORPHANED ENTRIES");
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
        _remove.Click += (_, _) => RemoveSelected();
        _restore.Click += (_, _) => RestoreLatest();

        _list.Resize += (_, _) =>
        {
            foreach (Control control in _list.Controls)
            {
                if (control is ToggleRow row) row.Width = RowWidth();
            }
        };

        _footer.Text = $"BACKUPS  {Theme.Truncate(RegistryService.BackupRoot, 120)}";
    }

    public override void OnNavigated() => Reload(announce: false);

    public override void RefreshData() => Reload(announce: false);

    private int RowWidth() => Math.Max(240, _list.ClientSize.Width - 24);

    private async void Reload(bool announce, string? note = null)
    {
        if (_busy) return;

        _busy = true;
        _status.Text = "SCANNING HKLM + HKCU\u2026";
        using IDisposable busy = Working();
        _status.ForeColor = Theme.TextDim;
        SetButtonsEnabled(false);

        try
        {
            List<RegistryIssue> issues = await Task.Run(RegistryService.Scan);
            _issues = issues;
            Rebuild();

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

        if (_issues.Count == 0)
        {
            var empty = new Label
            {
                Text = "No orphaned keys found \u2014 nothing here needs touching.",
                Font = Theme.MonoSmall,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(8, 12),
            };
            _list.Controls.Add(empty);
            y += 40;
        }

        foreach (IGrouping<RegistryCategory, RegistryIssue> group in _issues.GroupBy(issue => issue.Category))
        {
            var header = new Label
            {
                Text = $"{group.First().CategoryLabel}  \u2022  {group.Count()}",
                Font = Theme.Kicker,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(8, y + 4),
            };

            _list.Controls.Add(header);
            y += 26;

            foreach (RegistryIssue issue in group)
            {
                ToggleRow row = BuildRow(issue);
                row.SetBounds(4, y, RowWidth(), 62);
                _list.Controls.Add(row);
                _rows.Add((row, issue));
                y += 68;
            }

            y += 6;
        }

        _list.AutoScrollMinSize = new Size(0, y + 6);
        _list.ResumeLayout();
        SyncRemoveButton();
    }

    private ToggleRow BuildRow(RegistryIssue issue)
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
            Title = Theme.Truncate(issue.Display, 70),
            Subtitle = $"MISSING  {Theme.Truncate(issue.MissingPath, 110)}",
            BadgeText = issue.ReviewOnly ? "REVIEW" : badge,
            BadgeTone = issue.ReviewOnly ? Theme.TextFaint : issue.CategoryTone,
            Tone = issue.CategoryTone,
            Checked = false,
            Enabled = !issue.ReviewOnly,
            Tag = issue,
        };

        row.Toggled += (_, _) => SyncRemoveButton();

        _tips.SetToolTip(
            row,
            $"{issue.Display}\r\n{issue.RegistryPath}\r\nMissing: {issue.MissingPath}" +
            (issue.ReviewOnly ? "\r\nManage this on the Startup Apps page." : string.Empty));

        return row;
    }

    private void ToggleAll()
    {
        List<ToggleRow> rows = _rows
            .Where(pair => !pair.Issue.ReviewOnly)
            .Select(pair => pair.Row)
            .ToList();

        if (rows.Count == 0) return;

        bool select = rows.Any(row => !row.Checked);
        foreach (ToggleRow row in rows) row.Checked = select;
        SyncRemoveButton();
    }

    private void RemoveSelected()
    {
        List<RegistryIssue> selected = _rows
            .Where(pair => pair.Row.Checked)
            .Select(pair => pair.Issue)
            .ToList();

        if (selected.Count == 0)
        {
            UpdateStatus("tick the entries to remove first", Theme.Warn);
            return;
        }

        (bool ok, string message) = RegistryService.Remove(selected);

        if (!ok)
        {
            ActivityLog.Error($"Registry clean: {message}");
            UpdateStatus(message, Theme.Bad);
            return;
        }

        ActivityLog.Success($"Registry clean: {message}");
        Reload(announce: false, note: message);
    }

    private void RestoreLatest()
    {
        (bool ok, string message) = RegistryService.RestoreLatest();

        if (ok) ActivityLog.Success($"Registry restore: {message}");
        else ActivityLog.Warn($"Registry restore: {message}");

        Reload(announce: false, note: message);

        if (!ok) UpdateStatus(message, Theme.Warn);
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _rescan.Enabled = enabled;
        _selectAll.Enabled = enabled;
        _restore.Enabled = enabled && RegistryService.LatestBackupFolder() is not null;
        SyncRemoveButton();
    }

    private void SyncRemoveButton()
    {
        int selected = _rows.Count(pair => pair.Row.Checked);
        _remove.Text = selected == 0 ? "BACK UP + REMOVE" : $"BACK UP + REMOVE ({selected})";
        _remove.Enabled = !_busy && selected > 0;
        _remove.Invalidate();
    }

    private void UpdateStatus(string? note = null, Color? tone = null)
    {
        int orphaned = _issues.Count(issue => !issue.ReviewOnly);
        int review = _issues.Count - orphaned;

        _status.Text =
            $"{_issues.Count} FINDINGS  \u2022  {orphaned} ORPHANED  \u2022  {review} REVIEW  \u2022  " +
            (Native.IsElevated ? "ELEVATED" : "STANDARD USER") +
            (string.IsNullOrEmpty(note) ? string.Empty : $"  \u2022  {Theme.Truncate(note.ToUpperInvariant(), 80)}");

        _status.ForeColor = tone ?? Theme.TextDim;
    }
}
