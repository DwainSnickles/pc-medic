namespace PC_Medic;

internal sealed class SleepPage : PageBase
{
    private readonly Card _hero;
    private readonly Label _heroBody = new();
    private readonly Label _heroStatus = new();
    private readonly NeonButton _apply = Button("OPTIMIZE SLEEP", primary: true, width: 200, height: 34);
    private readonly NeonButton _restore = Button("RESTORE PREVIOUS", width: 180, height: 34);
    private readonly StepperRow _screen = new() { Title = "TURN OFF DISPLAY", Min = 0, Max = 120, Step = 5, Unit = "MIN" };
    private readonly StepperRow _sleep = new() { Title = "SLEEP AFTER", Min = 0, Max = 240, Step = 5, Unit = "MIN" };
    private readonly StepperRow _hibernate = new() { Title = "HIBERNATE AFTER", Min = 0, Max = 480, Step = 15, Unit = "MIN" };
    private readonly StepperRow _disk = new() { Title = "TURN OFF DISK", Min = 0, Max = 240, Step = 10, Unit = "MIN" };
    private readonly ToggleRow _wake = new() { Title = "ALLOW WAKE TIMERS", Subtitle = "Scheduled tasks that can wake the machine", BadgeText = "SLEEP", BadgeTone = Theme.Warn };
    private readonly ToggleRow _usb = new() { Title = "USB SELECTIVE SUSPEND", Subtitle = "Power down idle USB devices", BadgeText = "USB", BadgeTone = Theme.Accent };
    private readonly ToggleRow _hybrid = new() { Title = "HYBRID SLEEP", Subtitle = "Keep a disk copy of the session while sleeping", BadgeText = "DISK", BadgeTone = Theme.Violet };
    private readonly Label _settingsNote = new();
    private bool _loading;

    public SleepPage()
        : base("OPTIMIZE SLEEP", "One coherent idle ladder instead of half-sleep and surprise wake-ups.")
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 184));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _hero = Panel("OPTIMIZE SLEEP", "POWER LADDER");
        _hero.Margin = new Padding(0, 0, 0, 12);

        _heroBody.Text =
            "Sets a measured idle ladder \u2014 display 10 min, sleep 30 min, hibernate 2 h \u2014 disables wake timers, " +
            "stops USB devices dropping on AC and keeps a disk copy of the session. Every value is backed up first.";
        _heroBody.Font = Theme.UiSmall;
        _heroBody.ForeColor = Theme.TextDim;
        _heroBody.BackColor = Color.Transparent;
        _heroBody.AutoSize = false;

        _heroStatus.Text = "Reading current power policy\u2026";
        _heroStatus.Font = Theme.MonoSmall;
        _heroStatus.ForeColor = Theme.TextDim;
        _heroStatus.BackColor = Color.Transparent;
        _heroStatus.AutoSize = true;

        _hero.Controls.Add(_heroBody);
        _hero.Controls.Add(_heroStatus);
        _hero.Controls.Add(_apply);
        _hero.Controls.Add(_restore);

        void LayoutHero()
        {
            int width = Math.Max(240, _hero.ClientSize.Width - 36);
            _heroBody.SetBounds(18, 56, width, 44);
            _heroStatus.Location = new Point(19, 105);
            _apply.SetBounds(18, 130, _apply.Width, _apply.Height);
            _restore.SetBounds(_apply.Right + 10, 130, _restore.Width, _restore.Height);
        }

        _hero.Resize += (_, _) => LayoutHero();
        root.Controls.Add(_hero, 0, 0);

        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Card ladder = Panel("IDLE LADDER", "TIMEOUTS");
        ladder.Margin = new Padding(0, 0, 12, 0);
        StepperRow[] steppers = { _screen, _sleep, _hibernate, _disk };
        foreach (StepperRow row in steppers) ladder.Controls.Add(row);

        void LayoutLadder()
        {
            int width = Math.Max(200, ladder.ClientSize.Width - 36);
            int y = 58;
            foreach (StepperRow row in steppers)
            {
                row.SetBounds(18, y, width, 58);
                y += 62;
            }
        }

        ladder.Resize += (_, _) => LayoutLadder();
        LayoutLadder();
        split.Controls.Add(ladder, 0, 0);

        Card behavior = Panel("POWER BEHAVIOR", "GOTCHAS");
        behavior.Margin = new Padding(0);
        ToggleRow[] toggles = { _wake, _usb, _hybrid };
        foreach (ToggleRow row in toggles) behavior.Controls.Add(row);

        _settingsNote.Text = "Values apply to the active plan. Machine-wide writes may need elevation.";
        _settingsNote.Font = Theme.UiSmall;
        _settingsNote.ForeColor = Theme.TextDim;
        _settingsNote.BackColor = Color.Transparent;
        _settingsNote.AutoSize = false;
        _settingsNote.AutoEllipsis = true;

        void LayoutBehavior()
        {
            int width = Math.Max(200, behavior.ClientSize.Width - 36);
            int y = 58;
            foreach (ToggleRow row in toggles)
            {
                row.SetBounds(18, y, width, 62);
                y += 68;
            }

            _settingsNote.SetBounds(18, y + 4, width, 20);
        }

        behavior.Resize += (_, _) => LayoutBehavior();
        behavior.Controls.Add(_settingsNote);
        LayoutBehavior();
        split.Controls.Add(behavior, 1, 0);

        root.Controls.Add(split, 0, 1);
        Controls.Add(root);

        _apply.Click += async (_, _) =>
        {
            _apply.Enabled = false;
            SetHero("Applying optimized sleep ladder\u2026", Theme.TextDim);
            List<PowerStep> steps = await PowerService.OptimizeSleepAsync();
            int ok = steps.Count(step => step.Ok);
            bool all = ok == steps.Count;
            SetHero($"{ok}/{steps.Count} steps applied. " + (all ? "Sleep profile active." : "Remaining steps need elevation."),
                all ? Theme.Good : Theme.Warn);
            _apply.Enabled = true;
            LoadValues();
        };

        _restore.Click += async (_, _) =>
        {
            _restore.Enabled = false;
            (bool ok, string message) = await Task.Run(PowerService.RestoreBackup);
            ActivityLog.Write(ok ? LogLevel.Success : LogLevel.Warn, message);
            SetHero(message, ok ? Theme.Good : Theme.Warn);
            _restore.Enabled = true;
            LoadValues();
        };

        _screen.ValueChanged += (_, minutes) => WriteSetting(PowerService.ScreenOff, minutes);
        _sleep.ValueChanged += (_, minutes) => WriteSetting(PowerService.SleepAfter, minutes);
        _hibernate.ValueChanged += (_, minutes) => WriteSetting(PowerService.HibernateAfter, minutes);
        _disk.ValueChanged += (_, minutes) => WriteSetting(PowerService.DiskIdle, minutes);

        _wake.Toggled += (row, on) => WriteFlag(PowerService.WakeTimers, row, on, "Wake timers");
        _usb.Toggled += (row, on) => WriteFlag(PowerService.UsbSuspend, row, on, "USB selective suspend");
        _hybrid.Toggled += (row, on) => WriteFlag(PowerService.HybridSleep, row, on, "Hybrid sleep");
    }

    public override void OnNavigated() => LoadValues();

    public override void RefreshData() => LoadValues();

    private void SetHero(string text, Color tone)
    {
        _heroStatus.Text = text;
        _heroStatus.ForeColor = tone;
    }

    private async void LoadValues()
    {
        if (_loading) return;
        _loading = true;

        try
        {
            var snapshot = await Task.Run(() => new
            {
                Screen = PowerService.Read(PowerService.ScreenOff),
                Sleep = PowerService.Read(PowerService.SleepAfter),
                Hibernate = PowerService.Read(PowerService.HibernateAfter),
                Disk = PowerService.Read(PowerService.DiskIdle),
                Wake = PowerService.Read(PowerService.WakeTimers),
                Usb = PowerService.Read(PowerService.UsbSuspend),
                Hybrid = PowerService.Read(PowerService.HybridSleep),
            });

            if (IsDisposed) return;

            _screen.Value = (snapshot.Screen.Ac ?? 0) / 60;
            _sleep.Value = (snapshot.Sleep.Ac ?? 0) / 60;
            _hibernate.Value = (snapshot.Hibernate.Ac ?? 0) / 60;
            _disk.Value = (snapshot.Disk.Ac ?? 0) / 60;
            _wake.Checked = snapshot.Wake.Ac is > 0;
            _usb.Checked = snapshot.Usb.Ac is > 0;
            _hybrid.Checked = snapshot.Hybrid.Ac is > 0;

            foreach (ToggleRow row in new[] { _wake, _usb, _hybrid }) row.Invalidate();

            _hibernate.Enabled = MachineInfo.HibernateAvailable;
            _hybrid.Enabled = MachineInfo.HibernateAvailable;

            SetHero(
                $"Plan: {PowerService.ActivePlanName}  \u2022  hibernate {(MachineInfo.HibernateAvailable ? "available" : "unavailable")}" +
                (Native.IsElevated ? "  \u2022  elevated" : "  \u2022  standard user"),
                Theme.TextDim);
        }
        catch (Exception ex)
        {
            ActivityLog.Error($"Power query failed: {ex.Message}");
            SetHero("Power policy could not be read.", Theme.Bad);
        }
        finally
        {
            _loading = false;
        }
    }

    private async void WriteSetting(PowerSetting setting, int minutes)
    {
        if (_loading) return;

        int seconds = Math.Max(0, minutes * 60);
        (bool ok, string message) = await Task.Run(() => PowerService.Apply(setting, seconds));

        if (!ok)
        {
            SetHero($"{setting.Label}: {message}", Theme.Bad);
            ActivityLog.Error($"{setting.Label}: {message}");
            LoadValues();
            return;
        }

        ActivityLog.Success($"{setting.Label} set to {(minutes == 0 ? "never" : minutes + " min")}.");
        SetHero($"{setting.Label} \u2192 {(minutes == 0 ? "never" : minutes + " min")}", Theme.Good);
    }

    private async void WriteFlag(PowerSetting setting, ToggleRow row, bool on, string label)
    {
        if (_loading) return;

        int value = on ? 1 : 0;
        (bool ok, string message) = await Task.Run(() => PowerService.Apply(setting, value));

        if (!ok)
        {
            row.Checked = !on;
            row.Invalidate();
            SetHero($"{label}: {message}", Theme.Bad);
            ActivityLog.Error($"{label}: {message}");
            return;
        }

        ActivityLog.Success($"{label} {(on ? "enabled" : "disabled")}.");
        SetHero($"{label} {(on ? "enabled" : "disabled")}.", Theme.Good);
    }
}
