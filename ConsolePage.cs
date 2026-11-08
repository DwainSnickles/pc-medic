namespace PC_Medic;

internal sealed class ConsolePage : PageBase
{
    private static readonly (string Label, string Command, int Width)[] CommandQuick =
    {
        ("FLUSH DNS", "ipconfig /flushdns", 118),
        ("SFC", "sfc /scannow", 76),
        ("SCAN DISK", "chkdsk C: /scan", 122),
        ("DISM REPAIR", "DISM /Online /Cleanup-Image /RestoreHealth", 148),
        ("NETSTAT", "netstat -ano", 100),
        ("POWER REPORT", "powercfg /a", 138),
    };

    private static readonly (string Label, string Command, int Width)[] PowerShellQuick =
    {
        ("PS VERSION", "$PSVersionTable.PSVersion.ToString()", 118),
        ("TOP PROCESSES", "Get-Process | Sort-Object CPU -Descending | Select-Object -First 12 Name, CPU, WS", 140),
        ("STOPPED SERVICES", "Get-Service | Where-Object Status -eq 'Stopped' | Select-Object -First 20 Name, DisplayName", 156),
        ("DISK INFO", "Get-Disk | Format-Table Number, FriendlyName, HealthStatus, OperationalStatus -AutoSize", 106),
        ("STARTUP ITEMS", "Get-CimInstance Win32_StartupCommand | Select-Object Name, Command, Location", 134),
        ("HOTFIXES", "Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 10 HotFixID, InstalledOn", 104),
    };

    private readonly Card _inputCard;
    private readonly TextBox _input = new();
    private readonly TextBox _output = new();
    private readonly NeonButton _run = Button("RUN", primary: true, width: 90, height: 32);
    private readonly NeonButton _admin = Button("ELEVATED CMD", width: 150, height: 32);
    private readonly NeonButton _cmdMode = Button("CMD", width: 62, height: 26);
    private readonly NeonButton _psMode = Button("POWERSHELL", width: 116, height: 26);
    private readonly Panel _quickBar = new();
    private bool _powershell;

    public ConsolePage()
        : base("COMMAND SHELL", "Run any Windows command in cmd or PowerShell and keep the transcript.")
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _inputCard = Panel("WINDOWS COMMAND", "AD-HOC");
        _inputCard.Margin = new Padding(0, 0, 0, 10);

        _input.BorderStyle = BorderStyle.None;
        _input.BackColor = Theme.Surface2;
        _input.ForeColor = Theme.Text;
        _input.Font = Theme.Mono;

        _inputCard.Controls.Add(_input);
        _inputCard.Controls.Add(_run);
        _inputCard.Controls.Add(_admin);
        _inputCard.Controls.Add(_cmdMode);
        _inputCard.Controls.Add(_psMode);

        void LayoutInput()
        {
            int right = _inputCard.ClientSize.Width - 18;

            _psMode.SetBounds(right - _psMode.Width, 12, _psMode.Width, 26);
            _cmdMode.SetBounds(right - _psMode.Width - _cmdMode.Width - 8, 12, _cmdMode.Width, 26);

            _admin.SetBounds(right - _admin.Width, 52, _admin.Width, 32);
            right -= _admin.Width + 8;
            _run.SetBounds(right - _run.Width, 52, _run.Width, 32);
            right -= _run.Width + 12;
            _input.SetBounds(20, 58, Math.Max(120, right - 20), 24);
        }

        _inputCard.Resize += (_, _) => LayoutInput();
        LayoutInput();
        root.Controls.Add(_inputCard, 0, 0);

        _quickBar.Dock = DockStyle.Fill;
        _quickBar.BackColor = Theme.Bg;
        _quickBar.Margin = new Padding(0, 0, 0, 10);
        root.Controls.Add(_quickBar, 0, 1);

        Card outputCard = Panel("TRANSCRIPT", "STDOUT / STDERR");
        _output.Multiline = true;
        _output.ReadOnly = true;
        _output.WordWrap = false;
        _output.ScrollBars = ScrollBars.Both;
        _output.BorderStyle = BorderStyle.None;
        _output.BackColor = Theme.Surface;
        _output.ForeColor = Theme.Text;
        _output.Font = Theme.MonoSmall;
        _output.Dock = DockStyle.Fill;
        _output.Text =
            "PC MEDIC // COMMAND SHELL\r\n" +
            "Pick CMD or POWERSHELL for the runner; commands use your current token.\r\n" +
            "ELEVATED CMD / ELEVATED PS opens a prompt with administrator rights.\r\n\r\n";
        outputCard.Controls.Add(_output);
        root.Controls.Add(outputCard, 0, 2);

        Controls.Add(root);

        _run.Click += (_, _) => Execute(_input.Text);
        _cmdMode.Click += (_, _) => SetShell(false);
        _psMode.Click += (_, _) => SetShell(true);

        _admin.Click += (_, _) =>
        {
            if (_powershell)
            {
                Tools.LaunchTool(
                    "Elevated PowerShell",
                    "powershell.exe",
                    $"-NoExit -Command Set-Location -LiteralPath '{Tools.PortableRoot}'",
                    elevated: true);
                return;
            }

            Tools.LaunchTool("Elevated command prompt", "cmd.exe", "/k cd /d " + Tools.PortableRoot, elevated: true);
        };

        _input.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            Execute(_input.Text);
        };

        SetShell(false);
    }

    /// <summary>Swaps the runner, the button labels and the quick command set.</summary>
    private void SetShell(bool powershell)
    {
        _powershell = powershell;

        _cmdMode.Primary = !powershell;
        _psMode.Primary = powershell;
        _cmdMode.Invalidate();
        _psMode.Invalidate();

        _admin.Text = powershell ? "ELEVATED PS" : "ELEVATED CMD";
        _admin.Invalidate();

        _inputCard.Header = powershell ? "POWERSHELL" : "WINDOWS COMMAND";
        _inputCard.Invalidate();

        _input.PlaceholderText = powershell
            ? "Type a PowerShell command, e.g. Get-Service | Where-Object Status -eq 'Stopped'"
            : "Type a Windows command, e.g. ipconfig /all";

        BuildQuickChips();
    }

    private void BuildQuickChips()
    {
        _quickBar.SuspendLayout();
        foreach (Control control in _quickBar.Controls.Cast<Control>().ToArray()) control.Dispose();
        _quickBar.Controls.Clear();

        int left = 0;
        foreach ((string label, string command, int width) in _powershell ? PowerShellQuick : CommandQuick)
        {
            var chip = Button(label, width: width, height: 30);
            chip.SetBounds(left, 0, width, 30);
            chip.Click += (_, _) =>
            {
                _input.Text = command;
                Execute(command);
            };

            _quickBar.Controls.Add(chip);
            left += width + 8;
        }

        _quickBar.ResumeLayout();
    }

    private async void Execute(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        bool powershell = _powershell;
        _run.Enabled = false;
        using IDisposable busy = Working();
        Append($"{(powershell ? "PS> " : "> ")}{command}");
        ActivityLog.Info($"{(powershell ? "PowerShell" : "Command")}: {command}");

        try
        {
            CmdResult result = await Task.Run(() => powershell
                ? Cmd.RunPowerShell(command, 180000)
                : Cmd.Run("cmd.exe", "/c " + command, 180000));

            if (!string.IsNullOrWhiteSpace(result.Output)) Append(result.Output.TrimEnd());
            if (!string.IsNullOrWhiteSpace(result.Error)) Append(result.Error.TrimEnd());
            Append($"[exit {result.ExitCode}]");

            if (result.Ok) ActivityLog.Success($"Command finished: {command}");
            else ActivityLog.Warn($"Command exited {result.ExitCode}: {command}");
        }
        catch (Exception ex)
        {
            Append(ex.Message);
            ActivityLog.Error(ex.Message);
        }
        finally
        {
            _run.Enabled = true;
        }
    }

    private void Append(string text)
    {
        _output.AppendText(text + "\r\n");
        _output.SelectionStart = _output.TextLength;
        _output.ScrollToCaret();
    }
}
