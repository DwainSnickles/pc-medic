namespace PC_Medic;

/// <summary>
/// Updates page: application upgrades through winget on the left, driver updates from the
/// Windows Update agent on the right. Both are opt-in per row; drivers require elevation.
/// </summary>
internal sealed class UpdatesPage : PageBase
{
    private readonly BufferedPanel _apps = new();
    private readonly BufferedPanel _drivers = new();
    private readonly ScanOverlay _appsLoading = new() { Visible = false };
    private readonly ScanOverlay _driversLoading = new() { Visible = false };
    private readonly System.Windows.Forms.Timer _confirmTimer = new() { Interval = 4000 };
    private readonly System.Windows.Forms.Timer _rowSweep = new() { Interval = 40 };
    private readonly Label _appStatus = new();
    private readonly Label _driverStatus = new();
    private readonly Label _note = new();
    private readonly ToolTip _tips = new();
    private readonly NeonButton _scanApps = Button("SCAN", width: 76, height: 28);
    private readonly NeonButton _downloadApps = DownloadAllButton();
    private readonly NeonButton _cancelApps = CancelButton();
    private readonly NeonButton _scanDrivers = Button("SCAN", width: 76, height: 28);
    private readonly NeonButton _downloadDrivers = DownloadAllButton();
    private readonly NeonButton _cancelDrivers = CancelButton();
    private readonly List<(ToggleRow Row, AppUpgrade App)> _appRows = new();
    private readonly List<(ToggleRow Row, DriverUpdate Driver)> _driverRows = new();
    private readonly string? _winget = UpdatesService.WingetPath;
    private List<AppUpgrade> _appItems = new();
    private List<DriverUpdate> _driverItems = new();
    private bool _busyApps;
    private bool _busyDrivers;
    private CancellationTokenSource? _appCancel;
    private CancellationTokenSource? _driverCancel;
    private bool _firstVisit = true;
    private ToggleRow? _confirmRow;

    /// <summary>The single action each column carries: every listed update, downloaded in one pass.</summary>
    private static NeonButton DownloadAllButton()
    {
        var button = Button("DOWNLOAD ALL", primary: true, width: 158, height: 28);
        button.Glyph = "\uE896";
        return button;
    }

    private static NeonButton CancelButton()
    {
        var button = Button("CANCEL", width: 88, height: 28);
        button.Tone = Theme.Bad;
        button.Visible = false;
        return button;
    }

    public UpdatesPage()
        : base("UPDATES", "Application upgrades through winget, driver updates through Windows Update.")
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

        Card appsCard = BuildColumn("APPLICATIONS", "WINGET", _apps, _appStatus, _scanApps, _downloadApps, _cancelApps, _appsLoading);
        appsCard.Margin = new Padding(0, 0, 6, 8);

        Card driverCard = BuildColumn("DRIVERS", "WINDOWS UPDATE", _drivers, _driverStatus, _scanDrivers, _downloadDrivers, _cancelDrivers, _driversLoading);
        driverCard.Margin = new Padding(6, 0, 0, 8);

        root.Controls.Add(appsCard, 0, 0);
        root.Controls.Add(driverCard, 1, 0);

        _note.Text = _winget is null
            ? "WINGET NOT FOUND \u2014 THE UPGRADE BUTTON OPENS THE APP INSTALLER PAGE"
            : $"DATABASE  {Theme.Truncate(_winget, 70)}";
        _note.Font = Theme.MonoSmall;
        _note.ForeColor = Theme.TextDim;
        _note.BackColor = Color.Transparent;
        _note.AutoSize = false;
        _note.AutoEllipsis = true;
        _note.TextAlign = ContentAlignment.MiddleLeft;
        _note.Dock = DockStyle.Fill;
        _note.Margin = new Padding(2, 0, 0, 0);
        _tips.SetToolTip(_note, _winget ?? UpdatesService.WingetDownloadPage);
        root.Controls.Add(_note, 0, 1);
        root.SetColumnSpan(_note, 2);

        Controls.Add(root);

        _appStatus.Text = _winget is null ? "WINGET NOT FOUND" : "NOT SCANNED";
        _driverStatus.Text = "NOT SCANNED";

        _scanApps.Click += (_, _) => ReloadApps(announce: true);
        _scanDrivers.Click += (_, _) => ReloadDrivers(announce: true);

        _downloadApps.Click += (_, _) =>
        {
            if (_winget is null)
            {
                Tools.OpenUrl(UpdatesService.WingetDownloadPage);
                return;
            }

            DownloadAllApps();
        };

        _downloadDrivers.Click += (_, _) => DownloadAllDrivers();

        _cancelApps.Click += (_, _) => CancelApps();
        _cancelDrivers.Click += (_, _) => CancelDrivers();

        _apps.Resize += (_, _) => ResizeRows(_apps);
        _drivers.Resize += (_, _) => ResizeRows(_drivers);
        _confirmTimer.Tick += (_, _) => ResetConfirm();
        _rowSweep.Tick += (_, _) =>
        {
            foreach ((ToggleRow row, AppUpgrade _) in _appRows) row.Sweep();
        };

        SyncButtons();
    }

    public override void OnNavigated()
    {
        if (!_firstVisit) return;
        _firstVisit = false;

        // Both halves refresh on arrival: the driver search is the slower one, so it starts with the
        // winget query rather than waiting for someone to press its own SCAN button.
        ReloadApps(announce: false);
        ReloadDrivers(announce: false);
    }

    /// <summary>
    /// Work started here keeps running when the panel is left, but the wait cursor must not follow the
    /// pointer around the rest of the console: anything still outstanding is dropped on the way out.
    /// </summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (!Visible) Busy.ReleaseWaitCursor();
    }

    private Card BuildColumn(
        string header,
        string kicker,
        BufferedPanel list,
        Label status,
        NeonButton scan,
        NeonButton action,
        NeonButton cancel,
        ScanOverlay loading)
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
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Margin = new Padding(0) };

        status.AutoSize = false;
        status.AutoEllipsis = true;
        status.Font = Theme.MonoSmall;
        status.ForeColor = Theme.TextDim;
        status.BackColor = Color.Transparent;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.SetBounds(0, 8, 210, 18);

        toolbar.Controls.Add(status);
        toolbar.Controls.Add(action);
        toolbar.Controls.Add(cancel);
        toolbar.Controls.Add(scan);

        void LayoutToolbar()
        {
            int right = toolbar.ClientSize.Width;
            right -= action.Width;
            action.SetBounds(right, 3, action.Width, 28);

            // Cancel only takes room while a pass is running, so the rest of the toolbar stays put.
            if (cancel.Visible)
            {
                right -= cancel.Width + 6;
                cancel.SetBounds(right, 3, cancel.Width, 28);
            }

            right -= scan.Width + 8;
            scan.SetBounds(right, 3, scan.Width, 28);
            status.SetBounds(0, 8, Math.Max(80, right - 12), 18);
        }

        toolbar.Resize += (_, _) => LayoutToolbar();
        cancel.VisibleChanged += (_, _) => LayoutToolbar();
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

    private static int RowWidth(Panel list) => Math.Max(180, list.ClientSize.Width - 20);

    private static void ResizeRows(Panel list)
    {
        foreach (Control control in list.Controls)
        {
            if (control is ToggleRow row) row.Width = RowWidth(list);
        }
    }

    private async void ReloadApps(bool announce)
    {
        if (_busyApps) return;

        _busyApps = true;
        _scanApps.Enabled = false;
        SyncButtons();
        SetAppStatus(_winget is null ? "WINGET NOT FOUND" : "QUERYING WINGET\u2026", Theme.TextDim);
        using IDisposable busy = Working();
        _appsLoading.Begin();

        try
        {
            (List<AppUpgrade> apps, string? error) = await Task.Run(UpdatesService.ScanApps);
            _appItems = apps;
            _busyApps = false;
            RebuildApps();
            UpdatesState.Publish(apps.Count, UpdatesState.Drivers);

            if (error is not null)
            {
                SetAppStatus(Theme.Truncate(error.ToUpperInvariant(), 46), Theme.Warn, error.ToUpperInvariant());
            }
            else
            {
                SetAppStatus(
                    apps.Count == 0 ? "EVERYTHING IS CURRENT" : $"{apps.Count} UPDATES AVAILABLE",
                    apps.Count == 0 ? Theme.Good : Theme.Text);
            }

            if (announce)
            {
                ActivityLog.Info($"Update scan: {apps.Count} application upgrades available.");
            }
        }
        catch (Exception ex)
        {
            SetAppStatus(Theme.Truncate(ex.Message.ToUpperInvariant(), 46), Theme.Bad, ex.Message.ToUpperInvariant());
            ActivityLog.Error($"Update scan: {ex.Message}");
        }
        finally
        {
            _appsLoading.End();
            _busyApps = false;
            _scanApps.Enabled = true;
            SyncButtons();
        }
    }

    private async void ReloadDrivers(bool announce)
    {
        if (_busyDrivers) return;

        _busyDrivers = true;
        _scanDrivers.Enabled = false;
        SyncButtons();
        SetDriverStatus("ASKING WINDOWS UPDATE\u2026", Theme.TextDim);
        using IDisposable busy = Working();
        _driversLoading.Begin();

        try
        {
            (List<DriverUpdate> drivers, string? error) = await Task.Run(UpdatesService.ScanDrivers);
            _driverItems = drivers;
            _busyDrivers = false;
            RebuildDrivers();
            UpdatesState.Publish(UpdatesState.Apps, drivers.Count);

            if (error is not null)
            {
                SetDriverStatus(Theme.Truncate(error.ToUpperInvariant(), 46), Theme.Warn, error.ToUpperInvariant());
            }
            else
            {
                SetDriverStatus(
                    drivers.Count == 0 ? "NO DRIVERS NEED UPDATING" : $"{drivers.Count} DRIVER UPDATES",
                    drivers.Count == 0 ? Theme.Good : Theme.Text);
            }

            if (announce)
            {
                ActivityLog.Info($"Driver scan: {drivers.Count} driver updates offered by Windows Update.");
            }
        }
        catch (Exception ex)
        {
            SetDriverStatus(Theme.Truncate(ex.Message.ToUpperInvariant(), 46), Theme.Bad, ex.Message.ToUpperInvariant());
            ActivityLog.Error($"Driver scan: {ex.Message}");
        }
        finally
        {
            _driversLoading.End();
            _busyDrivers = false;
            _scanDrivers.Enabled = true;
            SyncButtons();
        }
    }

    /// <summary>First press arms the row, a second press within a few seconds removes the app.</summary>
    private void RequestUninstall(ToggleRow row, AppUpgrade app)
    {
        if (_busyApps) return;

        if (!ReferenceEquals(_confirmRow, row))
        {
            ResetConfirm();
            _confirmRow = row;
            row.ActionGlyph = "\uE73E";
            row.ActionTone = Theme.Warn;
            _confirmTimer.Start();
            SetAppStatus($"PRESS CONFIRM TO REMOVE {Theme.Truncate(app.Name.ToUpperInvariant(), 22)}", Theme.Warn, app.Name);
            return;
        }

        ResetConfirm();
        _ = UninstallAsync(app);
    }

    private void ResetConfirm()
    {
        _confirmTimer.Stop();

        if (_confirmRow is { IsDisposed: false } row) { row.ActionGlyph = "\uE74D"; row.ActionTone = Theme.Bad; }
        _confirmRow = null;
    }

    /// <summary>winget first; if it refuses the package, the app's own registered uninstaller is started.</summary>
    private async Task UninstallAsync(AppUpgrade app)
    {
        if (_busyApps) return;

        _busyApps = true;
        _scanApps.Enabled = false;
        SetRowsEnabled(_appRows.Select(pair => pair.Row), false);
        using IDisposable busy = Working();
        using IDisposable plate = _appsLoading.Busy();
        SyncButtons();
        SetAppStatus($"REMOVING {Theme.Truncate(app.Name.ToUpperInvariant(), 26)}\u2026", Theme.Bad, app.Name);

        try
        {
            (bool ok, string message) = await Task.Run(() => UpdatesService.Uninstall(app));

            if (!ok)
            {
                // Store packages and apps without a winget uninstaller only remove through their own entry.
                ProgramEntry? entry = await Task.Run(() => FindRegisteredUninstaller(app.Name));

                if (entry is null)
                {
                    ActivityLog.Error(message);
                    return;
                }

                (ok, message) = await Task.Run(() => ProgramsService.Uninstall(entry));
            }

            if (ok) ActivityLog.Success(message);
            else ActivityLog.Error(message);
        }
        catch (Exception ex)
        {
            ActivityLog.Error($"Uninstall failed: {ex.Message}");
        }
        finally
        {
            _busyApps = false;
            _scanApps.Enabled = true;
            SetRowsEnabled(_appRows.Select(pair => pair.Row), true);
            SyncButtons();
            ReloadApps(announce: false);
        }
    }

    private static ProgramEntry? FindRegisteredUninstaller(string name)
    {
        try
        {
            return ProgramsService.Load().FirstOrDefault(entry =>
                !entry.Dead &&
                entry.HasUninstaller &&
                (string.Equals(entry.DisplayName, name, StringComparison.OrdinalIgnoreCase) ||
                 entry.DisplayName.StartsWith(name, StringComparison.OrdinalIgnoreCase) ||
                 name.StartsWith(entry.DisplayName, StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex)
        {
            ActivityLog.Warn($"Uninstaller lookup failed: {ex.Message}");
            return null;
        }
    }

    private void RebuildApps()
    {
        _apps.SuspendLayout();
        foreach (Control control in _apps.Controls.Cast<Control>().ToArray()) control.Dispose();
        _apps.Controls.Clear();
        _appRows.Clear();

        int y = 4;

        if (_appItems.Count == 0)
        {
            y = AddEmpty(_apps, _busyApps ? "Scanning\u2026" : "Nothing to upgrade.", y);
        }

        foreach (AppUpgrade app in _appItems)
        {
            var row = new ToggleRow
            {
                Title = Theme.Truncate(app.Name, 54),
                Subtitle = $"{app.VersionLine}  \u2022  {app.Id}",
                BadgeText = "APP",
                BadgeTone = Theme.Accent,
                Tone = Theme.Accent,
                ActionGlyph = "\uE74D",
                ActionTone = Theme.Bad,
                ShowSwitch = false,
                Tag = app,
            };

            row.ActionClicked += clicked => RequestUninstall(clicked, app);
            string details = $"{app.Name}\r\n{app.VersionLine}  \u2022  {app.Id}";
            row.ActionHoverChanged += (_, over) => _tips.SetToolTip(row, over ? $"UNINSTALL {app.Name}" : details);
            _tips.SetToolTip(row, $"{app.Name}\r\n{app.VersionLine}  \u2022  {app.Id}");
            row.SetBounds(4, y, RowWidth(_apps), 62);
            _apps.Controls.Add(row);
            _appRows.Add((row, app));
            y += 68;
        }

        _apps.AutoScrollMinSize = new Size(0, y + 6);
        _apps.ResumeLayout();
        SyncButtons();
    }

    private void RebuildDrivers()
    {
        _drivers.SuspendLayout();
        foreach (Control control in _drivers.Controls.Cast<Control>().ToArray()) control.Dispose();
        _drivers.Controls.Clear();
        _driverRows.Clear();

        int y = 4;

        if (_driverItems.Count == 0)
        {
            y = AddEmpty(_drivers, _busyDrivers ? "Scanning\u2026" : "No drivers need updating.", y);
        }

        foreach (DriverUpdate driver in _driverItems)
        {
            var row = new ToggleRow
            {
                Title = Theme.Truncate(driver.Title, 54),
                Subtitle = driver.DetailLine,
                BadgeText = "DRV",
                BadgeTone = Theme.Violet,
                Tone = Theme.Violet,
                ShowSwitch = false,
                Tag = driver,
            };

            _tips.SetToolTip(row, $"{driver.Title}\r\n{driver.DetailLine}");
            row.SetBounds(4, y, RowWidth(_drivers), 62);
            _drivers.Controls.Add(row);
            _driverRows.Add((row, driver));
            y += 68;
        }

        _drivers.AutoScrollMinSize = new Size(0, y + 6);
        _drivers.ResumeLayout();
        SyncButtons();
    }

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

    /// <summary>
    /// Downloads and installs every listed app in one pass. The work stays on the message loop instead
    /// of behind a wait cursor, so the panel can be left while it continues; each row keeps its own bar
    /// and CANCEL stops the pass, killing winget's transfer if one is in flight.
    /// </summary>
    private async void DownloadAllApps()
    {
        if (_busyApps) return;

        List<AppUpgrade> queue = _appItems.ToList();
        if (queue.Count == 0) return;

        if (_winget is null)
        {
            Tools.OpenUrl(UpdatesService.WingetDownloadPage);
            return;
        }

        using var cancel = new CancellationTokenSource();
        _appCancel = cancel;

        _busyApps = true;
        _scanApps.Enabled = false;
        SetRowsEnabled(_appRows.Select(pair => pair.Row), false);
        SyncButtons();
        _rowSweep.Start();

        int applied = 0;
        bool stopped = false;

        try
        {
            for (int index = 0; index < queue.Count; index++)
            {
                if (cancel.IsCancellationRequested) { stopped = true; break; }

                AppUpgrade app = queue[index];
                ToggleRow? row = RowFor(app);

                if (row is not null) BeginRowProgress(row);
                SetAppStatus(
                    $"({index + 1}/{queue.Count}) DOWNLOADING {Theme.Truncate(app.Name.ToUpperInvariant(), 18)}",
                    Theme.Text,
                    app.Name);

                var progress = new Progress<int>(percent =>
                {
                    if (!IsDisposed && row is { IsDisposed: false }) ReportRowProgress(row, percent);
                });

                (bool ok, string message) = await UpdatesService.UpgradeAsync(app, progress, cancel.Token);

                if (IsDisposed) return;

                if (cancel.IsCancellationRequested) stopped = true;

                if (row is not null) EndRowProgress(row, ok, stopped);

                if (ok && !stopped)
                {
                    applied++;
                    ActivityLog.Success(message);
                }
                else if (!stopped)
                {
                    ActivityLog.Error(message);
                }
            }

            if (stopped)
            {
                SetAppStatus("CANCELLED", Theme.Warn);
                ActivityLog.Warn($"Download all stopped: {applied} of {queue.Count} applied.");
            }
            else
            {
                SetAppStatus(
                    $"{applied} OF {queue.Count} UPGRADED",
                    applied == queue.Count ? Theme.Good : Theme.Warn);
            }
        }
        catch (Exception ex)
        {
            SetAppStatus(Theme.Truncate(ex.Message.ToUpperInvariant(), 46), Theme.Bad, ex.Message);
            ActivityLog.Error($"Download all: {ex.Message}");
        }
        finally
        {
            _rowSweep.Stop();
            _busyApps = false;
            _appCancel = null;

            if (!IsDisposed)
            {
                _scanApps.Enabled = true;
                SetRowsEnabled(_appRows.Select(pair => pair.Row), true);
                SyncButtons();
                ReloadApps(announce: false);
            }
        }
    }

    private void CancelApps()
    {
        if (_appCancel is null) return;

        SetAppStatus("CANCELLING\u2026", Theme.Warn);
        _appCancel.Cancel();
    }

    /// <summary>The row that currently shows this app, or null once the list has been rebuilt.</summary>
    private ToggleRow? RowFor(AppUpgrade app)
    {
        foreach ((ToggleRow row, AppUpgrade candidate) in _appRows)
        {
            if (ReferenceEquals(candidate, app)) return row;
        }

        return null;
    }

    /// <summary>The row takes over as the progress surface while its package downloads and installs.</summary>
    private static void BeginRowProgress(ToggleRow row)
    {
        row.Working = true;
        row.Progress = 0;
        row.ActionText = string.Empty;
        row.ActionGlyph = string.Empty;
        row.BadgeText = "0%";
        row.BadgeTone = Theme.Accent;
        row.Invalidate();
    }

    private static void ReportRowProgress(ToggleRow row, int percent)
    {
        if (percent <= 0) return;

        row.Progress = percent;
        row.BadgeText = $"{percent}%";
    }

    private static void EndRowProgress(ToggleRow row, bool ok, bool stopped)
    {
        row.Working = false;
        row.Progress = 0;
        row.ActionText = string.Empty;
        row.ActionGlyph = stopped ? string.Empty : "\uE74D";
        row.ActionTone = Theme.Bad;
        row.BadgeText = stopped ? "STOPPED" : ok ? "DONE" : "FAILED";
        row.BadgeTone = stopped ? Theme.Warn : ok ? Theme.Good : Theme.Bad;
        row.Invalidate();
    }

    /// <summary>Every driver update, downloaded and installed one at a time so CANCEL can stop between them.</summary>
    private async void DownloadAllDrivers()
    {
        if (_busyDrivers) return;

        List<DriverUpdate> queue = _driverItems.ToList();
        if (queue.Count == 0) return;

        if (!Native.IsElevated)
        {
            SetDriverStatus("NEEDS ADMIN \u2014 RESTART AS ADMIN", Theme.Warn);
            ActivityLog.Warn("Driver install needs an elevated run.");
            return;
        }

        using var cancel = new CancellationTokenSource();
        _driverCancel = cancel;

        _busyDrivers = true;
        _scanDrivers.Enabled = false;
        SetRowsEnabled(_driverRows.Select(pair => pair.Row), false);
        SyncButtons();
        SetDriverStatus($"DOWNLOADING 1/{queue.Count}\u2026", Theme.Text);

        try
        {
            var progress = new Progress<(int Index, int Total)>(step =>
            {
                if (IsDisposed || step.Total <= 0) return;

                SetDriverStatus($"DOWNLOADING {step.Index + 1}/{step.Total}\u2026", Theme.Text);

                if (step.Index < _driverRows.Count) BeginRowProgress(_driverRows[step.Index].Row);
            });

            (bool ok, string message) = await Task.Run(
                () => UpdatesService.Install(queue, cancel.Token, progress),
                cancel.Token);

            if (IsDisposed) return;

            if (ok) ActivityLog.Success(message);
            else ActivityLog.Error(message);

            SetDriverStatus(Theme.Truncate(message.ToUpperInvariant(), 46), ok ? Theme.Good : Theme.Bad, message);
        }
        catch (OperationCanceledException)
        {
            SetDriverStatus("CANCELLED", Theme.Warn);
            ActivityLog.Warn("Driver download all cancelled.");
        }
        catch (Exception ex)
        {
            SetDriverStatus(Theme.Truncate(ex.Message.ToUpperInvariant(), 46), Theme.Bad, ex.Message.ToUpperInvariant());
            ActivityLog.Error($"Driver install: {ex.Message}");
        }
        finally
        {
            _busyDrivers = false;
            _driverCancel = null;

            if (!IsDisposed)
            {
                _scanDrivers.Enabled = true;
                SetRowsEnabled(_driverRows.Select(pair => pair.Row), true);
                SyncButtons();
                ReloadDrivers(announce: false);
            }
        }
    }

    private void CancelDrivers()
    {
        if (_driverCancel is null) return;

        SetDriverStatus("CANCELLING\u2026", Theme.Warn);
        _driverCancel.Cancel();
    }

    private static void SetRowsEnabled(IEnumerable<ToggleRow> rows, bool enabled)
    {
        foreach (ToggleRow row in rows)
        {
            row.Enabled = enabled;
            row.Invalidate();
        }
    }

    /// <summary>Status line kept short for the toolbar, with the full message in a tooltip.</summary>
    private void SetAppStatus(string text, Color tone, string? full = null)
    {
        _appStatus.Text = text;
        _appStatus.ForeColor = tone;
        _tips.SetToolTip(_appStatus, full ?? text);
    }

    private void SetDriverStatus(string text, Color tone, string? full = null)
    {
        _driverStatus.Text = text;
        _driverStatus.ForeColor = tone;
        _tips.SetToolTip(_driverStatus, full ?? text);
    }

    private void SyncButtons()
    {
        bool missingWinget = _winget is null;

        _downloadApps.Text = missingWinget ? "GET WINGET" : "DOWNLOAD ALL";
        _downloadApps.Glyph = missingWinget ? string.Empty : "\uE896";
        _downloadApps.Enabled = !_busyApps && (missingWinget || _appItems.Count > 0);
        _downloadApps.Invalidate();
        _cancelApps.Visible = _busyApps;

        _downloadDrivers.Enabled = !_busyDrivers && _driverItems.Count > 0;
        _downloadDrivers.Invalidate();
        _cancelDrivers.Visible = _busyDrivers;
    }
}
