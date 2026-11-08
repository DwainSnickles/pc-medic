namespace PC_Medic;

internal sealed class BootPage : PageBase
{
    private readonly InfoList _engine = new() { RowHeight = 28 };
    private readonly InfoList _pipeline = new() { RowHeight = 28 };
    private readonly ToggleRow _fast = new()
    {
        Title = "FAST STARTUP",
        Subtitle = "HiberbootEnabled \u2014 resumes the kernel session from disk",
        BadgeText = "BOOT",
        BadgeTone = Theme.Accent,
    };

    private readonly BufferedPanel _services = new();
    private readonly Label _status = new();
    private readonly NeonButton _delayAll = Button("DELAY THIRD-PARTY", primary: true, width: 190, height: 30);
    private readonly NeonButton _rescan = Button("RESCAN", width: 90, height: 30);
    private readonly Label _note = new();
    private readonly ToolTip _tips = new();
    private List<ServiceEntry> _autoServices = new();
    private int _startupTotal;
    private int _startupEnabled;

    public BootPage()
        : base("BOOT SPEED", "Trim what starts with Windows and measure the result.")
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 244));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Card engineCard = Panel("BOOT ENGINE", "STARTUP PATH");
        engineCard.Margin = new Padding(0, 0, 12, 12);
        engineCard.Controls.Add(_engine);
        engineCard.Controls.Add(_fast);

        void LayoutEngine()
        {
            int width = Math.Max(120, engineCard.ClientSize.Width - 36);
            _engine.SetBounds(18, 58, width, 84);
            _fast.SetBounds(18, 152, width, 62);
        }

        engineCard.Resize += (_, _) => LayoutEngine();
        split.Controls.Add(engineCard, 0, 0);

        Card pipelineCard = Panel("STARTUP PIPELINE", "AUTORUN LOAD");
        pipelineCard.Margin = new Padding(0, 0, 0, 12);
        pipelineCard.Controls.Add(_pipeline);

        _note.Text = "Delayed services start after the boot-critical phase.";
        _note.Font = Theme.UiSmall;
        _note.ForeColor = Theme.TextDim;
        _note.BackColor = Color.Transparent;
        _note.AutoSize = false;
        _note.AutoEllipsis = true;
        _note.Height = 20;
        pipelineCard.Controls.Add(_note);

        void LayoutPipeline()
        {
            int width = Math.Max(120, pipelineCard.ClientSize.Width - 36);
            _pipeline.SetBounds(18, 58, width, 112);
            _note.SetBounds(18, 194, width, 20);
        }

        pipelineCard.Resize += (_, _) => LayoutPipeline();
        split.Controls.Add(pipelineCard, 1, 0);

        root.Controls.Add(split, 0, 0);

        var toolbar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Margin = new Padding(0) };
        toolbar.Controls.Add(_delayAll);
        toolbar.Controls.Add(_rescan);
        toolbar.Controls.Add(_status);

        _status.AutoSize = true;
        _status.Font = Theme.MonoSmall;
        _status.ForeColor = Theme.TextDim;
        _status.BackColor = Color.Transparent;
        _status.Location = new Point(2, 13);

        void LayoutToolbar()
        {
            _rescan.SetBounds(toolbar.ClientSize.Width - _rescan.Width, 4, _rescan.Width, 30);
            _delayAll.SetBounds(toolbar.ClientSize.Width - _rescan.Width - _delayAll.Width - 10, 4, _delayAll.Width, 30);
        }

        toolbar.Resize += (_, _) => LayoutToolbar();
        LayoutToolbar();
        root.Controls.Add(toolbar, 0, 1);

        Card servicesCard = Panel("AUTO-START SERVICES", "SERVICE LOAD");
        _services.Dock = DockStyle.Fill;
        _services.AutoScroll = true;
        _services.BackColor = Theme.Surface;
        servicesCard.Controls.Add(_services);
        root.Controls.Add(servicesCard, 0, 2);

        Controls.Add(root);

        _services.Resize += (_, _) =>
        {
            foreach (Control control in _services.Controls) control.Width = Math.Max(220, _services.ClientSize.Width - 24);
        };

        _fast.Toggled += OnFastStartupToggled;
        _delayAll.Click += (_, _) => DelayThirdParty();
        _rescan.Click += (_, _) => Reload();
    }

    public override void OnNavigated() => Reload(announce: false);

    public override void RefreshData() => Reload(announce: false);

    private void Reload(bool announce = true)
    {
        _autoServices = WindowsServices.AutoStartServices();
        List<StartupEntry> startup = StartupService.Load();
        _startupTotal = startup.Count;
        _startupEnabled = startup.Count(entry => entry.Enabled);

        _fast.Checked = PowerService.FastStartupEnabled;
        _fast.Invalidate();

        RebuildServices();
        UpdateInfo();

        if (announce)
        {
            ActivityLog.Info($"Boot scan: {_autoServices.Count} auto services, fast startup {(_fast.Checked ? "on" : "off")}.");
        }
    }

    private void UpdateInfo()
    {
        _engine.Clear();
        _engine.Add("LAST BOOT", MachineInfo.LastBoot.ToString("ddd dd MMM  HH:mm"));
        _engine.Add("UPTIME", Theme.Duration(Native.Uptime));
        _engine.Add("HIBERNATION", MachineInfo.HibernateAvailable ? "available" : "unavailable",
            MachineInfo.HibernateAvailable ? Theme.Good : Theme.Warn);

        _pipeline.Clear();
        _pipeline.Add("STARTUP APPS", $"{_startupEnabled} / {_startupTotal} enabled",
            _startupEnabled == 0 ? Theme.Good : Theme.Text);
        _pipeline.Add("AUTO SERVICES", _autoServices.Count.ToString());
        _pipeline.Add("THIRD-PARTY AUTO", _autoServices.Count(s => s.ThirdParty).ToString(),
            _autoServices.Count(s => s.ThirdParty) > 6 ? Theme.Warn : Theme.Text);
        _pipeline.Add("DELAYED SERVICES", _autoServices.Count(s => s.Delayed).ToString(), Theme.Good);

        _status.Text = $"{_autoServices.Count} AUTO SERVICES  \u2022  {_autoServices.Count(s => s.Delayed)} DELAYED  \u2022  " +
                       (Native.IsElevated ? "ELEVATED" : "STANDARD USER");
        _status.ForeColor = Theme.TextDim;
    }

    private void RebuildServices()
    {
        _services.SuspendLayout();
        foreach (Control control in _services.Controls.Cast<Control>().ToArray()) control.Dispose();
        _services.Controls.Clear();

        List<ServiceEntry> visible = _autoServices.Take(150).ToList();
        int y = 6;
        foreach (ServiceEntry service in visible)
        {
            var row = new ToggleRow
            {
                Title = service.DisplayName,
                Subtitle = $"{service.Name}  \u2022  {(service.ThirdParty ? "third-party" : "windows")}  \u2022  {(service.Delayed ? "delayed start" : "immediate start")}",
                BadgeText = service.Delayed ? "DELAYED" : "BOOT",
                BadgeTone = service.Delayed ? Theme.Good : Theme.Warn,
                Checked = service.Delayed,
                Enabled = Native.IsElevated && service.ThirdParty,
                Tag = service,
            };

            row.Toggled += OnServiceToggled;
            _tips.SetToolTip(
                row,
                $"{service.DisplayName}\r\n{service.Name}  \u2022  " +
                $"{(service.ThirdParty ? "third-party" : "windows")}  \u2022  " +
                $"{(service.Delayed ? "delayed start" : "immediate start")}\r\n{service.ImagePath}");

            row.SetBounds(4, y, Math.Max(220, _services.ClientSize.Width - 24), 62);
            _services.Controls.Add(row);
            y += 68;
        }

        _services.AutoScrollMinSize = new Size(0, y + 6);
        _services.ResumeLayout();
    }

    private void OnServiceToggled(ToggleRow row, bool delayed)
    {
        if (row.Tag is not ServiceEntry service) return;

        (bool ok, string message) = WindowsServices.SetDelayed(service, delayed);
        if (!ok)
        {
            row.Checked = !delayed;
            ActivityLog.Error($"{service.DisplayName}: {message}");
            _status.Text = message.ToUpperInvariant();
            _status.ForeColor = Theme.Bad;
            return;
        }

        row.BadgeText = delayed ? "DELAYED" : "BOOT";
        row.BadgeTone = delayed ? Theme.Good : Theme.Warn;
        row.Subtitle = $"{service.Name}  \u2022  {(service.ThirdParty ? "third-party" : "windows")}  \u2022  {(delayed ? "delayed start" : "immediate start")}";
        ActivityLog.Success($"{service.DisplayName}: {message.ToLowerInvariant()} start.");
        UpdateInfo();
    }

    private void OnFastStartupToggled(ToggleRow row, bool enabled)
    {
        if (!Native.IsElevated)
        {
            row.Checked = PowerService.FastStartupEnabled;
            _status.Text = "FAST STARTUP NEEDS AN ELEVATED RUN \u2014 USE THE SIDEBAR";
            _status.ForeColor = Theme.Warn;
            return;
        }

        if (enabled && !MachineInfo.HibernateAvailable)
        {
            row.Checked = false;
            _status.Text = "ENABLE HIBERNATION FIRST (powercfg /hibernate on)";
            _status.ForeColor = Theme.Warn;
            ActivityLog.Warn("Fast Startup needs hibernation, which is currently disabled.");
            return;
        }

        (bool ok, string message) = PowerService.SetFastStartup(enabled);
        if (!ok)
        {
            row.Checked = PowerService.FastStartupEnabled;
            _status.Text = message.ToUpperInvariant();
            _status.ForeColor = Theme.Bad;
            return;
        }

        ActivityLog.Success(message + ".");
        _status.Text = message.ToUpperInvariant();
        _status.ForeColor = Theme.Good;
        UpdateInfo();
    }

    private void DelayThirdParty()
    {
        if (!Native.IsElevated)
        {
            _status.Text = "DELAYING SERVICES NEEDS AN ELEVATED RUN";
            _status.ForeColor = Theme.Warn;
            return;
        }

        List<ServiceEntry> targets = _autoServices.Where(s => s.ThirdParty && !s.Delayed).ToList();
        if (targets.Count == 0)
        {
            _status.Text = "NOTHING TO DELAY";
            _status.ForeColor = Theme.TextDim;
            return;
        }

        int succeeded = 0;
        foreach (ServiceEntry service in targets)
        {
            (bool ok, string _) = WindowsServices.SetDelayed(service, true);
            if (ok) succeeded++;
        }

        ActivityLog.Success($"Delayed {succeeded} of {targets.Count} third-party auto services.");
        Reload(announce: false);
        _status.Text = $"DELAYED {succeeded} / {targets.Count} SERVICES";
        _status.ForeColor = succeeded == targets.Count ? Theme.Good : Theme.Warn;
    }
}

