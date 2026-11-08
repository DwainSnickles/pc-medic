using Microsoft.Win32;

namespace PC_Medic;

/// <summary>
/// Reads and toggles startup programs exactly the way Task Manager does: Run keys plus
/// Startup-folder shortcuts, with enablement tracked in Explorer\StartupApproved.
/// </summary>
internal static class StartupService
{
    private const string UserRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string UserApproved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string MachineRun = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string MachineRun32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string MachineApproved = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public static List<StartupEntry> Load()
    {
        var entries = new List<StartupEntry>();

        AddRunKey(entries, RegistryHive.CurrentUser, RegistryView.Registry64, UserRun, UserApproved, machineWide: false);
        AddRunKey(entries, RegistryHive.LocalMachine, RegistryView.Registry64, MachineRun, MachineApproved, machineWide: true);
        AddRunKey(entries, RegistryHive.LocalMachine, RegistryView.Registry32, MachineRun32, MachineApproved, machineWide: true);
        AddStartupFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.Startup), machineWide: false);
        AddStartupFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), machineWide: true);

        return entries
            .OrderByDescending(e => e.EstimatedImpact)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static (bool Ok, string Message) SetEnabled(StartupEntry entry, bool enabled)
    {
        try
        {
            return entry.Kind == StartupKind.StartupFolder
                ? SetFolderEnabled(entry, enabled)
                : SetRegistryEnabled(entry, enabled);
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Administrator privileges are required for this entry.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static (bool Ok, string Message) SetRegistryEnabled(StartupEntry entry, bool enabled)
    {
        using RegistryKey baseKey = RegistryKey.OpenBaseKey(entry.Hive, entry.View);
        using RegistryKey approved = baseKey.CreateSubKey(entry.ApprovedPath, writable: true)
            ?? throw new InvalidOperationException("Startup approval store is unavailable.");

        var state = new byte[12];
        state[0] = enabled ? (byte)0x02 : (byte)0x03;
        approved.SetValue(entry.ValueName, state, RegistryValueKind.Binary);
        return (true, enabled ? "Enabled" : "Disabled");
    }

    private static (bool Ok, string Message) SetFolderEnabled(StartupEntry entry, bool enabled)
    {
        const string DisabledSuffix = ".pcmedic-disabled";
        if (enabled)
        {
            if (File.Exists(entry.FolderFile + DisabledSuffix))
            {
                File.Move(entry.FolderFile + DisabledSuffix, entry.FolderFile);
                return (true, "Enabled");
            }

            return (true, "Already enabled");
        }

        if (!File.Exists(entry.FolderFile)) return (false, "Shortcut is missing.");
        File.Move(entry.FolderFile, entry.FolderFile + DisabledSuffix);
        return (true, "Disabled");
    }

    private static void AddRunKey(
        List<StartupEntry> entries,
        RegistryHive hive,
        RegistryView view,
        string path,
        string approvedPath,
        bool machineWide)
    {
        string source = machineWide
            ? (view == RegistryView.Registry32 ? "HKLM Run (32-bit)" : "HKLM Run")
            : "HKCU Run";

        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? runKey = baseKey.OpenSubKey(path);
            if (runKey is null) return;
            using RegistryKey? approved = baseKey.OpenSubKey(approvedPath);

            foreach (string name in runKey.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (runKey.GetValue(name) is not string command || string.IsNullOrWhiteSpace(command)) continue;

                string file = ExtractExecutable(command);
                entries.Add(new StartupEntry
                {
                    Name = name,
                    Command = command,
                    FilePath = file,
                    Source = source,
                    Kind = StartupKind.RegistryRun,
                    MachineWide = machineWide,
                    Hive = hive,
                    View = view,
                    KeyPath = path,
                    ApprovedPath = approvedPath,
                    ValueName = name,
                    Enabled = IsApprovedEnabled(approved, name),
                    EstimatedImpact = EstimateImpact(command, file),
                });
            }
        }
        catch (Exception ex)
        {
            ActivityLog.Warn($"Could not read {source}: {ex.Message}");
        }
    }

    private static void AddStartupFolder(List<StartupEntry> entries, string folder, bool machineWide)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

        try
        {
            foreach (string file in Directory.EnumerateFiles(folder))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                bool disabled = file.EndsWith(".pcmedic-disabled", StringComparison.OrdinalIgnoreCase);
                string original = disabled ? file[..^".pcmedic-disabled".Length] : file;

                entries.Add(new StartupEntry
                {
                    Name = Path.GetFileNameWithoutExtension(original),
                    Command = original,
                    FilePath = original,
                    Source = machineWide ? "All users startup" : "User startup folder",
                    Kind = StartupKind.StartupFolder,
                    MachineWide = machineWide,
                    FolderFile = original,
                    Enabled = !disabled,
                    EstimatedImpact = Impact.Low,
                });
            }
        }
        catch (Exception ex)
        {
            ActivityLog.Warn($"Could not read startup folder: {ex.Message}");
        }
    }

    private static bool IsApprovedEnabled(RegistryKey? approved, string name)
    {
        try
        {
            if (approved?.GetValue(name) is not byte[] state || state.Length == 0) return true;
            // Even first byte = enabled (0x02/0x06), odd = disabled by the user.
            return (state[0] & 1) == 0;
        }
        catch
        {
            return true;
        }
    }

    private static string ExtractExecutable(string command)
    {
        string expanded = Environment.ExpandEnvironmentVariables(command.Trim());
        if (expanded.StartsWith('"'))
        {
            int end = expanded.IndexOf('"', 1);
            return end > 1 ? expanded[1..end] : expanded.Trim('"');
        }

        int exe = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0) return expanded[..(exe + 4)];

        int space = expanded.IndexOf(' ');
        return space > 0 ? expanded[..space] : expanded;
    }

    private static Impact EstimateImpact(string command, string filePath)
    {
        long size = 0;
        try
        {
            if (File.Exists(filePath)) size = new FileInfo(filePath).Length;
        }
        catch
        {
            // Unreadable target: fall through with size 0.
        }

        string probe = $"{filePath} {command}".ToLowerInvariant();
        bool updaterLike = probe.Contains("update") || probe.Contains("updater") ||
                           probe.Contains("helper") || probe.Contains("telemetry") ||
                           probe.Contains("assistant") || probe.Contains("launcher");

        if (size > 25_000_000 || updaterLike) return Impact.High;
        if (size > 8_000_000) return Impact.Medium;
        return Impact.Low;
    }
}
