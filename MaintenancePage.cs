namespace PC_Medic;

internal sealed class MaintenancePage : PageBase
{
    private sealed record ToolSpec(string Title, string Subtitle, string Glyph, string Status, Color Tone, Action Run);

    private readonly Label _status = new();
    private readonly ToolTip _tips = new();

    public MaintenancePage()
        : base("MAINTENANCE", "The Windows recovery and cleanup tools, one click away.")
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 3,
            RowCount = 5,
            Margin = new Padding(0),
        };
        for (int i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        for (int i = 0; i < 5; i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 20));

        List<ToolSpec> specs = BuildSpecs();
        for (int i = 0; i < specs.Count; i++)
        {
            ToolSpec spec = specs[i];
            var tile = new ToolTile
            {
                Glyph = spec.Glyph,
                Title = spec.Title,
                Subtitle = spec.Subtitle,
                Status = spec.Status,
                Tone = spec.Tone,
                StatusTone = spec.Status == "ADMIN" ? Theme.Warn : spec.Status == "TWEAK" ? Theme.Violet : Theme.TextDim,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 10, 10),
                Tag = spec,
            };

            tile.Click += (sender, _) =>
            {
                if (sender is ToolTile clicked && clicked.Tag is ToolSpec tool) tool.Run();
            };

            _tips.SetToolTip(tile, $"{spec.Title}\r\n{spec.Subtitle}");

            grid.Controls.Add(tile, i % 3, i / 3);
        }

        root.Controls.Add(grid, 0, 0);

        _status.Text = Native.IsElevated
            ? "ELEVATED TOKEN \u2014 ADMIN TOOLS RUN WITHOUT AN EXTRA PROMPT"
            : "STANDARD USER \u2014 ADMIN TOOLS WILL ASK FOR ELEVATION";
        _status.Font = Theme.MonoSmall;
        _status.ForeColor = Theme.TextDim;
        _status.BackColor = Color.Transparent;
        _status.AutoSize = true;
        _status.Location = new Point(2, 6);
        root.Controls.Add(_status, 0, 1);

        Controls.Add(root);
    }

    /// <summary>Opens the companion Disk Defrag tool, falling back to the Windows optimiser if it is missing.</summary>
    private static void LaunchDiskDefrag()
    {
        string? path = Tools.Companion("DiskDefrag.exe");

        if (path is null)
        {
            ActivityLog.Warn("Disk Defrag is not built beside PC Medic; opening the Windows optimiser instead.");
            Tools.LaunchTool("Defragmenter", Path.Combine(Environment.SystemDirectory, "dfrgui.exe"));
            return;
        }

        Tools.LaunchTool("Disk Defrag", path);
    }

    private static List<ToolSpec> BuildSpecs()
    {
        string sys = Environment.SystemDirectory;

        return new List<ToolSpec>
        {
            new("DISK CLEANUP", "cleanmgr \u2014 temp + update leftovers", "\uE74D", "SYSTEM", Theme.Good,
                () => Tools.LaunchTool("Disk Cleanup", Path.Combine(sys, "cleanmgr.exe"))),

            new("DISK DEFRAG", "Analyse, defragment and optimize volumes", "\uE9F9", "TOOL", Theme.Accent,
                LaunchDiskDefrag),

            new("RESTORE POINT", "Create a rollback point first", "\uE777", "SYSTEM", Theme.Accent,
                () => Tools.LaunchTool("System Protection", Path.Combine(sys, "SystemPropertiesProtection.exe"))),

            new("WINDOWS UPDATE", "Install updates and optional drivers", "\uE895", "SYSTEM", Theme.Accent,
                () => Tools.LaunchTool("Windows Update", Path.Combine(sys, "control.exe"), "/name Microsoft.WindowsUpdate")),

            new("UNINSTALL APPS", "Installed apps in Settings", "\uE74D", "SYSTEM", Theme.TextDim,
                () => Tools.LaunchTool("Installed Apps", "ms-settings:appsfeatures-app")),

            new("STARTUP APPS", "Task Manager startup tab", "\uE71D", "SYSTEM", Theme.Accent,
                () => Tools.LaunchTool("Startup Apps", Path.Combine(sys, "Taskmgr.exe"), "/startup")),

            new("ANIMATION SPEED", $"MenuShowDelay \u2014 now {Tools.MenuShowDelay} ms", "\uE945", "TWEAK", Theme.Violet,
                () => Tools.SetMenuShowDelay(Tools.MenuShowDelay > 200 ? 200 : 400)),

            new("SFC SCAN", "Repair protected system files", "\uEA18", "ADMIN", Theme.Warn,
                () => Tools.LaunchTool("SFC", "cmd.exe", "/k sfc /scannow", elevated: true)),

            new("MEMORY DIAG", "MdSched \u2014 test RAM health", "\uE9D2", "SYSTEM", Theme.Accent,
                () => Tools.LaunchTool("Memory Diagnostic", Path.Combine(sys, "MdSched.exe"))),

            new("SYSTEM CONFIG", "msconfig \u2014 boot and services", "\uE713", "SYSTEM", Theme.TextDim,
                () => Tools.LaunchTool("System Configuration", Path.Combine(sys, "msconfig.exe"))),

            new("POWER PLAN", "Choose an active power plan", "\uE7E8", "SYSTEM", Theme.Accent,
                () => Tools.LaunchTool("Power Options", Path.Combine(sys, "powercfg.cpl"))),

            new("ULTIMATE PLAN", "Unlock Ultimate Performance", "\uE945", "ADMIN", Theme.Warn,
                () => Tools.LaunchTool("Ultimate Performance", Path.Combine(sys, "powercfg.exe"), "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61", elevated: true)),

            new("STORAGE SENSE", "Automatic temp + download cleanup", "\uE958", "SYSTEM", Theme.TextDim,
                () => Tools.LaunchTool("Storage Sense", "ms-settings:storagesense")),

            new("TASK MANAGER", "Live processes, performance, startup", "\uE9D9", "SYSTEM", Theme.Accent,
                () => Tools.LaunchTool("Task Manager", Path.Combine(sys, "Taskmgr.exe"))),

            new("EVENT VIEWER", "Memory test results and boot logs", "\uE7BA", "SYSTEM", Theme.TextDim,
                () => Tools.LaunchTool("Event Viewer", Path.Combine(sys, "eventvwr.msc"))),
        };
    }
}
