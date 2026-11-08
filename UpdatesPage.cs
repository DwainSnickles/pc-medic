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
    private readonly Label _appStatus = new();
    private readonly Label _driverStatus = new();
    private readonly Label _note = new();
    private readonly ToolTip _tips = new();
    private readonly NeonButton _scanApps = Button("SCAN", width: 76, height: 28);
    private readonly NeonButton _upgrade = Button("UPGRADE (0)", primary: true, width: 132, height: 28);
    private readonly NeonButton _scanDrivers = Button("SCAN", width: 76, height: 28);
    private readonly NeonButton _install = Button("INSTALL (0)", primary: true, width: 112, height: 28);
    private readonly List<(ToggleRow Row, AppUpgrade App)> _appRows = new();
    private readonly List<(ToggleRow Row, DriverUpdate Driver)> _driverRows = new();
    private readonly string? _winget = UpdatesService.WingetPath;
    private List<AppUpgrade> _appItems = new();
    private List<DriverUpdate> _driverItems = new();
    private bool _busyApps;
    private bool _busyDrivers;
    private bool _firstVisit = true;
    private ToggleRow? _confirmRow;

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

        Card appsCard = BuildColumn("APPLICATIONS", "WINGET", _apps, _appStatus, _scanApps, _upgrade, _appsLoading);
        appsCard.Margin = new Padding(0, 0, 6, 8);

        Card driverCard = BuildColumn("DRIVERS", "WINDOWS UPDATE", _drivers, _driverStatus, _scanDrivers, _install, _driversLoading);
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

        _upgrade.Click += (_, _) =>
        {
            if (_winget is null)
            {
                Tools.OpenUrl(UpdatesService.WingetDownloadPage);
                return;
            }

            UpgradeSelected();
        };

        _install.Click += (_, _) => InstallSelected();

        _apps.Resize += (_, _) => ResizeRows(_apps);
        _drivers.Resize += (_, _) => ResizeRows(_drivers);
        _confirmTimer.Tick += (_, _) => ResetConfirm();

        SyncButtons();
    }

    public override void OnNavigated()
    {
        if (!_firstVisit) return;
        _firstVisit = false;
        ReloadApps(announce: false);
    }

    private Card BuildColumn(
        string header,
        string kicker,
        BufferedPanel list,
        Label status,
        NeonButton scan,
        NeonButton action,
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
        toolbar.Controls.Add(scan);

        void LayoutToolbar()
        {
            int right = toolbar.ClientSize.Width;
            right -= action.Width;
            action.SetBounds(right, 3, action.Width, 28);
            right -= scan.Width + 8;
            scan.SetBounds(right, 3, scan.Width, 28);
            status.SetBounds(0, 8, Math.Max(80, right - 12), 18);
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
            RebuildDrivers();
            UpdatesState.Publish(UpdatesState.Apps, drivers.Count);

            if (error is not null)
            {
                SetDriverStatus(Theme.Truncate(error.ToUpperInvariant(), 46), Theme.Warn, error.ToUpperInvariant());
            }
            else
            {
                SetDriverStatus(
                    drivers.Count == 0 ? "NO DRIVER UPDATES" : $"{drivers.Count} DRIVER UPDATES",
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
            row.ActionText = "CONFIRM?";
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

        if (_confirmRow is { IsDisposed: false } row) row.ActionText = "UNINSTALL";
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
                ActionText = "UNINSTALL",
                ActionTone = Theme.Bad,
                Checked = true,
                Tag = app,
            };

            row.Toggled += (_, _) => SyncButtons();
            row.ActionClicked += clicked => RequestUninstall(clicked, app);
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
            y = AddEmpty(_drivers, _busyDrivers ? "Scanning\u2026" : "No driver updates offered.", y);
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
                Checked = false,
                Tag = driver,
            };

            row.Toggled += (_, _) => SyncButtons();
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

    private async void UpgradeSelected()
    {
        List<AppUpgrade> selected = _appRows
            .Where(pair => pair.Row.Checked)
            .Select(pair => pair.App)
            .ToList();

        if (selected.Count == 0) return;

        _busyApps = true;
        _scanApps.Enabled = false;
        SetRowsEnabled(_appRows.Select(pair => pair.Row), false);
        using IDisposable busy = Working();
        SyncButtons();

        int applied = 0;

        try
        {
            foreach (AppUpgrade app in selected)
            {
                SetAppStatus($"UPGRADING {Theme.Truncate(app.Name.ToUpperInvariant(), 28)}\u2026", Theme.Text, app.Name);

                (bool ok, string message) = await Task.Run(() => UpdatesService.Upgrade(app));

                if (ok)
                {
                    applied++;
                    ActivityLog.Success(message);
                }
                else
                {
                    ActivityLog.Error(message);
                }
            }
        }
        finally
        {
            _busyApps = false;
            _scanApps.Enabled = true;
            SetRowsEnabled(_appRows.Select(pair => pair.Row), true);
            ActivityLog.Info($"Upgrade pass finished: {applied} of {selected.Count} applied.");
            ReloadApps(announce: false);
        }
    }

    private async void InstallSelected()
    {
        List<DriverUpdate> selected = _driverRows
            .Where(pair => pair.Row.Checked)
            .Select(pair => pair.Driver)
            .ToList();

        if (selected.Count == 0) return;

        if (!Native.IsElevated)
        {
            SetDriverStatus("NEEDS ADMIN \u2014 RESTART AS ADMIN", Theme.Warn);
            ActivityLog.Warn("Driver install needs an elevated run.");
            return;
        }

        _busyDrivers = true;
        _scanDrivers.Enabled = false;
        SetRowsEnabled(_driverRows.Select(pair => pair.Row), false);
        SyncButtons();
        SetDriverStatus("DOWNLOADING + INSTALLING\u2026", Theme.Text);
        using IDisposable busy = Working();

        try
        {
            (bool ok, string message) = await Task.Run(() => UpdatesService.Install(selected));

            if (ok) ActivityLog.Success(message);
            else ActivityLog.Error(message);

            SetDriverStatus(Theme.Truncate(message.ToUpperInvariant(), 46), ok ? Theme.Good : Theme.Bad, message);
        }
        catch (Exception ex)
        {
            SetDriverStatus(Theme.Truncate(ex.Message.ToUpperInvariant(), 46), Theme.Bad, ex.Message.ToUpperInvariant());
            ActivityLog.Error($"Driver install: {ex.Message}");
        }
        finally
        {
            _busyDrivers = false;
            _scanDrivers.Enabled = true;
            SetRowsEnabled(_driverRows.Select(pair => pair.Row), true);
            SyncButtons();
            ReloadDrivers(announce: false);
        }
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
        int apps = _appRows.Count(pair => pair.Row.Checked);
        int drivers = _driverRows.Count(pair => pair.Row.Checked);

        _upgrade.Text = missingWinget ? "GET WINGET" : $"UPGRADE ({apps})";
        _upgrade.Enabled = !_busyApps && (missingWinget || apps > 0);
        _upgrade.Invalidate();

        _install.Text = $"INSTALL ({drivers})";
        _install.Enabled = !_busyDrivers && drivers > 0;
        _install.Invalidate();
    }
}
