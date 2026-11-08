namespace PC_Medic;

/// <summary>
/// The shell: fixed sidebar, fixed header, docked page host. No flow layouts anywhere.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly Font HeaderFont = new(Theme.H2.FontFamily, 15f, FontStyle.Bold);

    private readonly Panel _sidebar = new();
    private readonly Panel _header = new();
    private readonly Panel _content = new();
    private readonly Label _pageTitle = new();
    private readonly Label _pageSub = new();
    private readonly Label _clock = new();
    private readonly Badge _adminBadge = new();
    private readonly Badge _planBadge = new();
    private readonly List<NavItem> _nav = new();
    private NavItem? _updatesNav;
    private readonly Dictionary<string, PageBase> _pages = new();
    private readonly System.Windows.Forms.Timer _clockTimer = new() { Interval = 1000 };
    private readonly NotifyIcon _tray = new();
    private readonly SystemMonitor _trayMonitor = new();
    private bool _trayHintShown;

    public MainForm()
    {
        Text = "PC MEDIC \u2014 System Optimizer";
        ClientSize = new Size(1250, 815);
        MinimumSize = new Size(1120, 760);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        DoubleBuffered = true;

        try
        {
            Icon = Brand.CreateIcon(64);
        }
        catch
        {
            // Icon rendering is cosmetic; never let it block startup.
        }

        BuildSidebar();
        BuildHeader();
        BuildContent();

        Controls.Add(_content);
        Controls.Add(_header);
        Controls.Add(_sidebar);

        BuildNav();
        ShowPage("overview");

        _clockTimer.Tick += (_, _) =>
        {
            _clock.Text = DateTime.Now.ToString("HH:mm:ss");
            _planBadge.SetText(PowerService.ActivePlanName.ToUpperInvariant());
        };
        _clockTimer.Start();
        _clock.Text = DateTime.Now.ToString("HH:mm:ss");

        BuildTray();

        UpdatesState.Changed += OnUpdatesChanged;
        ShowUpdatesBadge();
        UpdatesState.BeginScan();
    }

    /// <summary>Tray presence, so the console can be parked and reopened without relaunching.</summary>
    private void BuildTray()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Theme.Surface2,
            ForeColor = Theme.Text,
            Font = Theme.Ui,
            ShowImageMargin = false,
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()),
        };

        menu.Items.Add("Open PC MEDIC", null, (_, _) => ShowFromTray());
        menu.Items.Add("Check for updates", null, (_, _) => ShowSelfUpdate());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit PC MEDIC", null, (_, _) => Close());

        _tray.Text = "PC MEDIC \u2014 System Optimizer";
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowFromTray();

        try
        {
            _tray.Icon = Brand.CreateIcon(32);
        }
        catch
        {
            // Without a rendered icon the tray entry stays hidden; the shell still works.
        }

        _tray.Visible = _tray.Icon is not null;

        // The tooltip is the only surface visible while the console is parked, so it carries live telemetry.
        _trayMonitor.Sampled += OnTraySample;
        _trayMonitor.Start();
    }

    /// <summary>Mirrors the dashboard sampler into the tray tooltip: CPU load, memory load and used / total.</summary>
    private void OnTraySample(SystemSample sample)
    {
        if (IsDisposed || !_tray.Visible) return;
        _tray.Text = TrayText(sample);
    }

    /// <summary>One-line telemetry for the tray tooltip. Kept well under the NotifyIcon 63-character cap.</summary>
    internal static string TrayText(SystemSample sample) =>
        $"PC MEDIC \u2014 CPU {sample.CpuPercent:0}% \u00b7 RAM {sample.MemoryPercent:0}%" +
        $" \u00b7 {Theme.Bytes(sample.MemoryUsed)} / {Theme.Bytes(sample.MemoryTotal)}";

    /// <summary>Brings the console back from the tray or the taskbar.</summary>
    internal void ShowFromTray()
    {
        if (InvokeRequired)
        {
            BeginInvoke(ShowFromTray);
            return;
        }

        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (WindowState != FormWindowState.Minimized || !_tray.Visible) return;

        Hide();
        ShowInTaskbar = false;

        if (_trayHintShown) return;
        _trayHintShown = true;
        _tray.ShowBalloonTip(
            2200,
            "PC MEDIC is still running",
            "Double-click the tray icon to reopen the console.",
            ToolTipIcon.None);
    }

    private static Label MakeLabel(string text, Font font, Color color) => new()
    {
        Text = text,
        Font = font,
        ForeColor = color,
        BackColor = Color.Transparent,
        AutoSize = true,
    };

    private void BuildSidebar()
    {
        _sidebar.Dock = DockStyle.Left;
        _sidebar.Width = 246;
        _sidebar.BackColor = Theme.Surface;
        _sidebar.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, _sidebar.Width - 1, 0, _sidebar.Width - 1, _sidebar.Height);
        };

        var mark = new BrandMark
        {
            Location = new Point(20, 16),
            Size = new Size(42, 42),
        };

        Label brand = MakeLabel("PC MEDIC", Theme.H1, Theme.Text);
        brand.Location = new Point(70, 18);

        Label tagline = MakeLabel("SYSTEM OPTIMIZER", Theme.Kicker, Theme.Alpha(Theme.Accent, 215));
        tagline.Location = new Point(72, 48);

        Label build = MakeLabel("PORTABLE 1.0", Theme.Kicker, Theme.TextDim);
        build.Location = new Point(22, 70);

        var divider = new Panel
        {
            BackColor = Theme.Border,
            Location = new Point(22, 90),
            Width = 202,
            Height = 1,
        };

        _sidebar.Controls.Add(mark);
        _sidebar.Controls.Add(brand);
        _sidebar.Controls.Add(tagline);
        _sidebar.Controls.Add(build);
        _sidebar.Controls.Add(divider);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 168, BackColor = Theme.Surface };
        bottom.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 22, 0, bottom.Width - 22, 0);
        };

        Label elevation = MakeLabel("ELEVATION", Theme.Kicker, Theme.TextDim);
        elevation.Location = new Point(24, 16);

        Label state = MakeLabel(
            Native.IsElevated ? "ADMINISTRATOR" : "STANDARD USER",
            Theme.MonoSmall,
            Native.IsElevated ? Theme.Good : Theme.Warn);
        state.Location = new Point(24, 32);

        var elevate = new NeonButton
        {
            Text = Native.IsElevated ? "RUNNING ELEVATED" : "RESTART AS ADMIN",
            Size = new Size(198, 32),
            Location = new Point(22, 58),
            Enabled = !Native.IsElevated,
        };

        elevate.Click += (_, _) =>
        {
            if (Native.RestartElevated())
            {
                Close();
            }
            else
            {
                ActivityLog.Warn("Elevation was cancelled.");
            }
        };

        // The update check lives here as a button rather than riding along in the kicker line.
        var checkUpdates = new NeonButton
        {
            Text = "CHECK UPDATES",
            Size = new Size(198, 32),
            Location = new Point(22, 96),
        };

        checkUpdates.Click += (_, _) => ShowSelfUpdate();

        Label note = MakeLabel("USB-friendly \u2022 nothing installed", Theme.Kicker, Theme.TextDim);
        note.Location = new Point(24, 138);

        bottom.Controls.Add(elevation);
        bottom.Controls.Add(state);
        bottom.Controls.Add(elevate);
        bottom.Controls.Add(checkUpdates);
        bottom.Controls.Add(note);
        _sidebar.Controls.Add(bottom);

        _adminBadge.SetText(Native.IsElevated ? "ELEVATED" : "STANDARD");
        _adminBadge.Tone = Native.IsElevated ? Theme.Good : Theme.Warn;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        LayoutHeader();
    }

    private void BuildHeader()
    {
        _header.Dock = DockStyle.Top;
        _header.Height = 78;
        _header.BackColor = Theme.Bg;
        _header.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.BorderSoft);
            e.Graphics.DrawLine(pen, 28, _header.Height - 1, _header.Width - 26, _header.Height - 1);
        };

        _pageTitle.Font = HeaderFont;
        _pageTitle.ForeColor = Theme.Text;
        _pageTitle.BackColor = Color.Transparent;
        _pageTitle.AutoSize = true;
        _pageTitle.Location = new Point(28, 14);

        _pageSub.ForeColor = Theme.TextDim;
        _pageSub.BackColor = Color.Transparent;
        _pageSub.AutoSize = true;
        _pageSub.Font = Theme.UiSmall;
        _pageSub.Location = new Point(30, 45);

        _clock.Font = Theme.Mono;
        _clock.ForeColor = Theme.TextDim;
        _clock.BackColor = Color.Transparent;
        _clock.AutoSize = true;

        _adminBadge.Tone = Native.IsElevated ? Theme.Good : Theme.Warn;
        _planBadge.Tone = Theme.Accent;
        _planBadge.SetText(PowerService.ActivePlanName.ToUpperInvariant());

        _header.Controls.Add(_pageTitle);
        _header.Controls.Add(_pageSub);
        _header.Controls.Add(_clock);
        _header.Controls.Add(_adminBadge);
        _header.Controls.Add(_planBadge);

        _header.Resize += (_, _) => LayoutHeader();
    }

    private void LayoutHeader()
    {
        int right = _header.ClientSize.Width - 26;
        _clock.Location = new Point(right - _clock.Width, 27);
        right -= _clock.Width + 16;
        _adminBadge.Location = new Point(right - _adminBadge.Width, 31);
        right -= _adminBadge.Width + 10;
        _planBadge.Location = new Point(right - _planBadge.Width, 31);
    }

    private void BuildContent()
    {
        _content.Dock = DockStyle.Fill;
        _content.BackColor = Theme.Bg;
        _content.Padding = new Padding(28, 8, 26, 22);
    }

    private void BuildNav()
    {
        (string Key, string Glyph, string Caption)[] entries =
        {
            ("overview", "\uE80F", "Overview"),
            ("startup", "\uE71D", "Startup Apps"),
            ("boot", "\uE945", "Boot Speed"),
            ("sleep", "\uE708", "Optimize Sleep"),
            ("maintenance", "\uE90F", "Maintenance"),
            ("tweaks", "\uE713", "Tweaks"),
            ("programs", "\uE7B8", "Programs"),
            ("updates", "\uE896", "Updates"),
            ("audit", "\uE721", "Audit"),
            ("benchmarks", "\uE9D9", "Benchmarks"),
            ("console", "\uE756", "Command Shell"),
        };

        int y = 104;
        foreach ((string key, string glyph, string caption) in entries)
        {
            var item = new NavItem(key, glyph, caption)
            {
                Location = new Point(14, y),
                Width = 218,
            };

            item.Click += (_, _) => ShowPage(item.Key);
            _sidebar.Controls.Add(item);
            _nav.Add(item);

            if (key == "updates") _updatesNav = item;
            y += 48;
        }
    }

    private void ShowSelfUpdate()
    {
        if (!Visible) ShowFromTray();

        using var dialog = new SelfUpdateForm();
        dialog.ShowDialog(this);
    }

    private void OnUpdatesChanged()
    {
        if (IsDisposed) return;

        if (InvokeRequired)
        {
            BeginInvoke(OnUpdatesChanged);
            return;
        }

        ShowUpdatesBadge();
    }

    private void ShowUpdatesBadge()
    {
        if (_updatesNav is null) return;

        _updatesNav.BadgeText = UpdatesState.Total > 0 ? UpdatesState.Total.ToString() : string.Empty;
        _updatesNav.BadgeTone = UpdatesState.Drivers > 0 ? Theme.Violet : Theme.Accent;
    }

    private void ShowPage(string key)
    {
        if (!_pages.TryGetValue(key, out PageBase? page))
        {
            page = CreatePage(key);
            page.NavigateTo = ShowPage;
            _content.Controls.Add(page);
            _pages[key] = page;
        }

        foreach ((string pageKey, PageBase candidate) in _pages)
        {
            candidate.Visible = pageKey == key;
        }

        foreach (NavItem item in _nav)
        {
            item.Active = item.Key == key;
        }

        _pageTitle.Text = page.TitleText;
        _pageSub.Text = page.SubtitleText;
        _planBadge.SetText(PowerService.ActivePlanName.ToUpperInvariant());

        page.BringToFront();
        page.OnNavigated();
    }

    private static PageBase CreatePage(string key) => key switch
    {
        "startup" => new StartupPage(),
        "boot" => new BootPage(),
        "sleep" => new SleepPage(),
        "maintenance" => new MaintenancePage(),
        "tweaks" => new TweaksPage(),
        "programs" => new ProgramsPage(),
        "updates" => new UpdatesPage(),
        "audit" => new AuditPage(),
        "benchmarks" => new BenchPage(),
        "console" => new ConsolePage(),
        _ => new OverviewPage(),
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _clockTimer.Stop();
            _clockTimer.Dispose();
            UpdatesState.Changed -= OnUpdatesChanged;

            _trayMonitor.Sampled -= OnTraySample;
            _trayMonitor.Dispose();

            _tray.Visible = false;
            _tray.ContextMenuStrip?.Dispose();
            _tray.Dispose();
        }

        base.Dispose(disposing);
    }
}
