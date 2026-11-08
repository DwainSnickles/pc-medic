namespace PC_Medic;

/// <summary>
/// Add or Remove Programs for PC Medic: every registered application with a working uninstaller,
/// and the dead entries whose uninstaller is gone. Dead entries are cleared through
/// RegistryService, so a .reg backup is written before the key disappears.
/// </summary>
internal sealed class ProgramsPage : PageBase
{
    private readonly BufferedPanel _list = new();
    private readonly Label _status = new();
    private readonly Label _footer = new();
    private readonly ToolTip _tips = new();
    private readonly NeonButton _rescan = Button("RESCAN", width: 92, height: 30);
    private readonly NeonButton _uninstall = Button("UNINSTALL", width: 216, height: 30);
    private readonly NeonButton _clean = Button("REMOVE DEAD", width: 152, height: 30);
    private readonly List<(ToggleRow Row, ProgramEntry Entry)> _rows = new();
    private List<ProgramEntry> _entries = new();
    private bool _busy;

    public ProgramsPage()
        : base("PROGRAMS", "Installed applications, one-click uninstall, and dead entries that can be cleared out.")
    {
        _uninstall.Tone = Theme.Warn;
        _clean.Tone = Theme.Bad;

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Bg };

        _status.AutoSize = true;
        _status.Font = Theme.MonoSmall;
        _status.ForeColor = Theme.TextDim;
        _status.BackColor = Color.Transparent;
        _status.Location = new Point(2, 13);
        toolbar.Controls.Add(_status);

        toolbar.Controls.Add(_rescan);
        toolbar.Controls.Add(_uninstall);
        toolbar.Controls.Add(_clean);

        void LayoutToolbar()
        {
            int right = toolbar.ClientSize.Width;
            right -= _rescan.Width;
            _rescan.SetBounds(right, 4, _rescan.Width, 30);
            right -= _uninstall.Width + 8;
            _uninstall.SetBounds(right, 4, _uninstall.Width, 30);
            right -= _clean.Width + 8;
            _clean.SetBounds(right, 4, _clean.Width, 30);
        }

        toolbar.Resize += (_, _) => LayoutToolbar();
        LayoutToolbar();

        Card card = Panel("INSTALLED APPLICATIONS", "ADD OR REMOVE PROGRAMS");
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
        _uninstall.Click += (_, _) => UninstallSelected();
        _clean.Click += (_, _) => CleanSelected();

        _list.Resize += (_, _) =>
        {
            foreach (Control control in _list.Controls)
            {
                if (control is ToggleRow row) row.Width = RowWidth();
            }
        };

        _footer.Text = $"BACKUPS  {Theme.Truncate(RegistryService.BackupRoot, 120)}";
        _status.Text = "NOT LOADED";
    }

    public override void OnNavigated() => Reload(announce: false);

    public override void RefreshData() => Reload(announce: false);

    private int RowWidth() => Math.Max(260, _list.ClientSize.Width - 24);

    private async void Reload(bool announce, string? note = null)
    {
        if (_busy) return;

        _busy = true;
        _status.Text = "READING THE UNINSTALL REGISTRY\u2026";
        using IDisposable busy = Working();
        _status.ForeColor = Theme.TextDim;
        SetButtonsEnabled(false);

        try
        {
            List<ProgramEntry> entries = await Task.Run(ProgramsService.Load);
            _entries = entries;
            Rebuild();

            if (announce)
            {
                ActivityLog.Info(
                    $"Program list: {entries.Count} installed, {entries.Count(entry => entry.Dead)} dead entries.");
            }
        }
        catch (Exception ex)
        {
            ActivityLog.Error($"Program list failed: {ex.Message}");
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

        if (_entries.Count == 0)
        {
            var empty = new Label
            {
                Text = "No installed programs were found in the uninstall registry.",
                Font = Theme.MonoSmall,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(8, 12),
            };

            _list.Controls.Add(empty);
            y += 40;
        }

        foreach (ProgramEntry entry in _entries)
        {
            ToggleRow row = BuildRow(entry);
            row.SetBounds(4, y, RowWidth(), 62);
            _list.Controls.Add(row);
            _rows.Add((row, entry));
            y += 68;
        }

        _list.AutoScrollMinSize = new Size(0, y + 6);
        _list.ResumeLayout();
        SyncButtons();
    }

    private ToggleRow BuildRow(ProgramEntry entry)
    {
        var row = new ToggleRow
        {
            Title = Theme.Truncate(entry.DisplayName, 68),
            Subtitle = entry.DetailLine,
            BadgeText = entry.Dead ? "DEAD" : entry.IsMsi ? "MSI" : "APP",
            BadgeTone = entry.Dead ? Theme.Bad : Theme.TextDim,
            Tone = entry.Dead ? Theme.Bad : Theme.Accent,
            Checked = false,
            Tag = entry,
        };

        row.Toggled += (_, on) =>
        {
            if (on && !entry.Dead) SelectOnly(row);
            SyncButtons();
        };

        _tips.SetToolTip(
            row,
            $"{entry.DisplayName}\r\n{entry.RegistryPath}" +
            (entry.HasUninstaller ? $"\r\n{entry.UninstallCommand}" : string.Empty) +
            (entry.Dead ? $"\r\n{entry.DeadReason}" : string.Empty));

        return row;
    }

    /// <summary>Uninstalling is one program at a time, so a working entry behaves like a radio button.</summary>
    private void SelectOnly(ToggleRow target)
    {
        foreach ((ToggleRow Row, ProgramEntry Entry) pair in _rows)
        {
            if (pair.Row == target || pair.Entry.Dead) continue;
            pair.Row.Checked = false;
        }
    }

    private ProgramEntry? SelectedApp() => _rows
        .Where(pair => !pair.Entry.Dead && pair.Row.Checked)
        .Select(pair => pair.Entry)
        .FirstOrDefault();

    private void UninstallSelected()
    {
        ProgramEntry? entry = SelectedApp();

        if (entry is null)
        {
            UpdateStatus("tick a program to uninstall first", Theme.Warn);
            return;
        }

        (bool ok, string message) = ProgramsService.Uninstall(entry);

        if (ok)
        {
            ActivityLog.Success(message);
            UpdateStatus(message, Theme.Good);
            return;
        }

        ActivityLog.Error($"Uninstall: {message}");
        UpdateStatus(message, Theme.Bad);
    }

    private void CleanSelected()
    {
        List<ProgramEntry> dead = _rows
            .Where(pair => pair.Entry.Dead && pair.Row.Checked)
            .Select(pair => pair.Entry)
            .ToList();

        if (dead.Count == 0)
        {
            UpdateStatus("tick the dead entries to clear first", Theme.Warn);
            return;
        }

        (bool ok, string message) = RegistryService.Remove(ProgramsService.ToIssues(dead));

        if (!ok)
        {
            ActivityLog.Error($"Dead entry clean: {message}");
            UpdateStatus(message, Theme.Bad);
            return;
        }

        ActivityLog.Success($"Dead entry clean: {message}");
        Reload(announce: false, note: message);

        if (message.Contains("restart as admin", StringComparison.OrdinalIgnoreCase))
        {
            UpdateStatus(message, Theme.Warn);
        }
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _rescan.Enabled = enabled;
        SyncButtons();
    }

    private void SyncButtons()
    {
        ProgramEntry? app = SelectedApp();
        int dead = _rows.Count(pair => pair.Entry.Dead && pair.Row.Checked);

        _uninstall.Text = app is null
            ? "UNINSTALL"
            : $"UNINSTALL {Theme.Truncate(app.DisplayName.ToUpperInvariant(), 17)}";
        _uninstall.Enabled = !_busy && app is not null && app.HasUninstaller;
        _uninstall.Invalidate();

        _clean.Text = dead == 0 ? "REMOVE DEAD" : $"REMOVE DEAD ({dead})";
        _clean.Enabled = !_busy && dead > 0;
        _clean.Invalidate();
    }

    private void UpdateStatus(string? note = null, Color? tone = null)
    {
        int dead = _entries.Count(entry => entry.Dead);

        _status.Text =
            $"{_entries.Count} PROGRAMS  \u2022  {dead} DEAD  \u2022  " +
            (Native.IsElevated ? "ELEVATED" : "STANDARD USER") +
            (string.IsNullOrEmpty(note) ? string.Empty : $"  \u2022  {Theme.Truncate(note.ToUpperInvariant(), 80)}");

        _status.ForeColor = tone ?? Theme.TextDim;
    }
}
