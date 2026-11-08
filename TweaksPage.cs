namespace PC_Medic;

internal sealed class TweaksPage : PageBase
{
    private static readonly string[] AppearanceKeys = { "visual", "transparency", "background" };
    private static readonly string[] SystemKeys = { "gamedvr", "telemetry", "hibernate" };

    private readonly List<(Tweak Tweak, ToggleRow Row)> _bindings = new();
    private readonly Label _status = new();
    private readonly NeonButton _rescan = Button("RESCAN", width: 108, height: 30);
    private readonly NeonButton _revert = Button("REVERT ALL", primary: true, width: 132, height: 30);
    private readonly NeonButton _history = Button("HISTORY", width: 118, height: 30);
    private bool _loading;

    public TweaksPage()
        : base("TWEAKS", "Reversible performance switches \u2014 each one is a registry value or a powercfg flag.")
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
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

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

        Card appearance = Panel("VISUAL & UI", "APPEARANCE");
        appearance.Margin = new Padding(0, 0, 12, 12);
        Label appearanceNote = Note("Per-user values apply at once; Explorer picks up appearance changes for new windows.");
        ToggleRow[] appearanceRows = CreateRows(appearance, AppearanceKeys, appearanceNote);

        Card system = Panel("SYSTEM & PRIVACY", "SERVICES & DATA");
        system.Margin = new Padding(0, 0, 0, 12);
        Label systemNote = Note("Elevated switches write a policy or run powercfg \u2014 switching back removes them again.");
        ToggleRow[] systemRows = CreateRows(system, SystemKeys, systemNote);

        LayoutCard(appearance, appearanceRows, appearanceNote);
        appearance.Resize += (_, _) => LayoutCard(appearance, appearanceRows, appearanceNote);

        LayoutCard(system, systemRows, systemNote);
        system.Resize += (_, _) => LayoutCard(system, systemRows, systemNote);

        split.Controls.Add(appearance, 0, 0);
        split.Controls.Add(system, 1, 0);
        root.Controls.Add(split, 0, 0);

        var footer = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };

        _status.Font = Theme.MonoSmall;
        _status.ForeColor = Theme.TextDim;
        _status.BackColor = Color.Transparent;
        _status.AutoSize = true;

        footer.Controls.Add(_status);
        footer.Controls.Add(_revert);
        footer.Controls.Add(_history);
        footer.Controls.Add(_rescan);
        footer.Resize += (_, _) =>
        {
            _status.Location = new Point(2, 9);
            _rescan.Location = new Point(Math.Max(0, footer.ClientSize.Width - _rescan.Width), 2);
            _revert.Location = new Point(Math.Max(0, _rescan.Left - _revert.Width - 8), 2);
            _history.Location = new Point(Math.Max(0, _revert.Left - _history.Width - 8), 2);
        };

        _rescan.Click += (_, _) => LoadStates();
        _history.Click += (_, _) => ShowHistory();
        _revert.Click += RevertAll;
        root.Controls.Add(footer, 0, 1);

        Controls.Add(root);
    }

    public override void OnNavigated() => LoadStates();

    public override void RefreshData() => LoadStates();

    private ToggleRow[] CreateRows(Card card, IEnumerable<string> keys, Label note)
    {
        var rows = new List<ToggleRow>();

        foreach (string key in keys)
        {
            Tweak tweak = TweakService.All.Single(candidate => candidate.Key == key);

            var row = new ToggleRow
            {
                Title = tweak.Title,
                Subtitle = tweak.Subtitle,
                BadgeText = tweak.Badge,
                BadgeTone = tweak.RequiresAdmin ? Theme.Warn : Theme.Accent,
                Tone = tweak.Tone,
            };

            row.Toggled += (sender, on) => OnToggled(sender, on);
            card.Controls.Add(row);
            _bindings.Add((tweak, row));
            rows.Add(row);
        }

        card.Controls.Add(note);
        return rows.ToArray();
    }

    private static Label Note(string text) => new()
    {
        Text = text,
        Font = Theme.UiSmall,
        ForeColor = Theme.TextDim,
        BackColor = Color.Transparent,
        AutoSize = false,
    };

    private static void LayoutCard(Card card, IReadOnlyList<ToggleRow> rows, Label note)
    {
        int width = Math.Max(240, card.ClientSize.Width - 36);
        int y = 58;

        foreach (ToggleRow row in rows)
        {
            row.SetBounds(18, y, width, 62);
            y += 68;
        }

        note.SetBounds(18, y + 2, width, 42);
    }

    private async void OnToggled(ToggleRow row, bool on)
    {
        if (_loading) return;

        Tweak? tweak = null;
        foreach ((Tweak Candidate, ToggleRow Bound) in _bindings)
        {
            if (Bound == row)
            {
                tweak = Candidate;
                break;
            }
        }

        if (tweak is null) return;

        if (tweak.RequiresAdmin && !Native.IsElevated)
        {
            row.Checked = !on;
            row.Invalidate();
            SetStatus($"{tweak.Title}: administrator rights required \u2014 use RESTART AS ADMIN.", Theme.Warn);
            ActivityLog.Warn($"{tweak.Title}: needs an elevated run.");
            return;
        }

        if (on && !tweak.IsApplied() && !TweakBackup.Capture(tweak))
        {
            row.Checked = false;
            row.Invalidate();
            SetStatus($"{tweak.Title}: original value could not be recorded \u2014 change cancelled.", Theme.Bad);
            ActivityLog.Error($"{tweak.Title}: no snapshot could be written.");
            return;
        }

        TweakChange change = TweakBackup.Prepare(tweak, on);

        row.Enabled = false;
        SetStatus($"{tweak.Title.ToLowerInvariant()} \u2014 working\u2026", Theme.TextDim);

        TweakOutcome outcome = await Task.Run(() => tweak.Apply(on));
        if (IsDisposed) return;

        if (outcome.Ok) TweakBackup.Record(change, outcome);

        row.Enabled = true;
        ActivityLog.Write(outcome.Ok ? LogLevel.Success : LogLevel.Error, $"{tweak.Title}: {outcome.Message}");
        SetStatus(outcome.Ok ? outcome.Message : $"{tweak.Title}: {outcome.Message}", outcome.Ok ? Theme.Good : Theme.Bad);

        LoadStates();
    }

    private async void RevertAll(object? sender, EventArgs e)
    {
        _revert.Enabled = false;
        SetStatus("Restoring captured values\u2026", Theme.TextDim);

        var result = await Task.Run(() => TweakBackup.RevertAll(TweakService.All));
        if (IsDisposed) return;

        _revert.Enabled = true;

        LogLevel level = result.Restored > 0 ? LogLevel.Success : LogLevel.Warn;
        ActivityLog.Write(level, $"Tweak revert: {result.Message}");

        foreach (string failure in result.Failures)
        {
            ActivityLog.Warn($"Tweak revert skipped \u2014 {failure}");
        }

        SetStatus(result.Message, result.Restored > 0 && result.Failures.Count == 0 ? Theme.Good : Theme.Warn);
        LoadStates();
    }

    private void ShowHistory()
    {
        using var dialog = new TweakHistoryForm();
        dialog.ShowDialog(this);
        LoadStates();
    }

    private void SetStatus(string text, Color tone)
    {
        _status.Text = text;
        _status.ForeColor = tone;
    }

    private async void LoadStates()
    {
        if (_loading) return;
        _loading = true;

        try
        {
            var snapshot = await Task.Run(() =>
            {
                var states = new List<(ToggleRow Row, bool Applied)>();

                foreach ((Tweak tweak, ToggleRow row) in _bindings)
                {
                    states.Add((row, tweak.IsApplied()));
                }

                return (States: states, Snapshots: TweakBackup.Count, Changes: TweakBackup.HistoryCount);
            });

            if (IsDisposed) return;

            int active = 0;
            foreach ((ToggleRow row, bool applied) in snapshot.States)
            {
                row.Checked = applied;
                row.Invalidate();
                if (applied) active++;
            }

            _revert.Enabled = snapshot.Snapshots > 0;

            SetStatus(
                $"{active} OF {snapshot.States.Count} TWEAKS ACTIVE  \u2022  {snapshot.Snapshots} SNAPSHOT" +
                (snapshot.Snapshots == 1 ? string.Empty : "S") +
                $"  \u2022  {snapshot.Changes} CHANGE" + (snapshot.Changes == 1 ? string.Empty : "S") + "  \u2022  " +
                (Native.IsElevated ? "ELEVATED" : "STANDARD USER"),
                active > 0 ? Theme.Good : Theme.TextDim);
        }
        catch (Exception ex)
        {
            ActivityLog.Error($"Tweak state read failed: {ex.Message}");
            SetStatus("Tweak state could not be read.", Theme.Bad);
        }
        finally
        {
            _loading = false;
        }
    }
}
