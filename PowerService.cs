using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PC_Medic;

internal sealed record PowerSetting(string Key, string Label, string SubGroup, string Setting, string Note);

internal readonly record struct PowerValue(int? Ac, int? Dc)
{
    public bool Known => Ac.HasValue || Dc.HasValue;
}

/// <summary>
/// Sleep/power tuning over powercfg, plus the curated "Optimize Sleep" profile.
/// Every applied change is backed up so the user can roll back.
/// </summary>
internal static class PowerService
{
    private const string SubSleep = "238c9fa8-0aad-41ed-83f4-97be242c8f20";
    private const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
    private const string SubVideo = "7516b95f-f776-4464-8c53-06167f40cc99";
    private const string SubUsb = "2a737441-1930-4402-8d77-b2bebba308a3";
    private const string SubDisk = "0012ee47-9041-4b5d-9b77-535fba8b1442";

    private static readonly Regex HexIndex = new(@"0x([0-9A-Fa-f]{1,8})", RegexOptions.Compiled);
    private static readonly string BackupFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCMedic",
        "sleep-profile-backup.json");

    private static readonly string CpuCapFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCMedic",
        "cpu-ceiling-backup.json");

    public static readonly PowerSetting ScreenOff =
        new("screen", "Turn off display", SubVideo, "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e", "Minutes until the monitor sleeps.");

    public static readonly PowerSetting SleepAfter =
        new("sleep", "Sleep after", SubSleep, "29f6c1db-86da-48c5-9fdb-f2b67b1f44da", "Idle time before the PC enters standby.");

    public static readonly PowerSetting HibernateAfter =
        new("hibernate", "Hibernate after", SubSleep, "9d7815a6-7ee4-497e-8888-515a05f02364", "Idle time before writing the session to disk.");

    public static readonly PowerSetting WakeTimers =
        new("wake", "Allow wake timers", SubSleep, "bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d", "Background tasks that wake the machine at night.");

    public static readonly PowerSetting HybridSleep =
        new("hybrid", "Hybrid sleep", SubSleep, "94ac6d29-73ce-41a6-809f-6363ba21b47e", "Keeps a disk copy of the session when sleeping.");

    public static readonly PowerSetting UsbSuspend =
        new("usb", "USB selective suspend", SubGroup: SubUsb, Setting: "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", Note: "Drops idle USB devices to save power.");

    public static readonly PowerSetting ProcessorMax =
        new("cpumax", "Maximum processor state", SubProcessor, "bc5038f7-23e0-4960-96da-33abaf5935ec", "Caps how high the CPU may clock.");

    public static readonly PowerSetting DiskIdle =
        new("disk", "Turn off hard disk", SubDisk, "6738e2c4-e8a5-4a42-b16a-e040e769756e", "Spins down idle drives after this delay.");

    public static IReadOnlyList<PowerSetting> AllSettings => new[]
    {
        ScreenOff, SleepAfter, HibernateAfter, WakeTimers, HybridSleep, UsbSuspend, DiskIdle,
    };

    private static readonly Dictionary<string, string> PlanNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["381b4222-f694-41f0-9685-ff5bb260df2e"] = "Balanced",
        ["8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"] = "High performance",
        ["a1841308-3541-4fab-bc81-f71556f20b4a"] = "Power saver",
        ["e9a42b02-d5df-448d-aa00-03f14749eb61"] = "Ultimate performance",
    };

    public static string ActivePlanName
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes");
                if (key?.GetValue("ActivePowerScheme") is string guid && PlanNames.TryGetValue(guid, out string? name))
                {
                    return name;
                }
            }
            catch
            {
                // Fall through to a neutral label.
            }

            return "Active scheme";
        }
    }

    public static PowerValue Read(PowerSetting setting)
    {
        CmdResult result = Cmd.Run("powercfg.exe", $"/q SCHEME_CURRENT {setting.SubGroup} {setting.Setting}", 8000);
        if (!result.Ok) return default;

        var indexes = new List<int>();
        foreach (string line in result.Output.Split('\n'))
        {
            foreach (Match match in HexIndex.Matches(line))
            {
                indexes.Add(Convert.ToInt32(match.Groups[1].Value, 16));
            }
        }

        if (indexes.Count == 0) return default;
        if (indexes.Count == 1) return new PowerValue(indexes[0], indexes[0]);
        return new PowerValue(indexes[^2], indexes[^1]);
    }

    public static (bool Ok, string Message) Apply(PowerSetting setting, int acSeconds, int? dcSeconds = null)
    {
        int dc = dcSeconds ?? acSeconds;
        CmdResult acResult = Cmd.Run("powercfg.exe", $"/setacvalueindex SCHEME_CURRENT {setting.SubGroup} {setting.Setting} {acSeconds}", 8000);
        CmdResult dcResult = Cmd.Run("powercfg.exe", $"/setdcvalueindex SCHEME_CURRENT {setting.SubGroup} {setting.Setting} {dc}", 8000);
        Cmd.Run("powercfg.exe", "/setactive SCHEME_CURRENT", 8000);

        if (acResult.Ok && dcResult.Ok) return (true, "Applied");
        return (false, acResult.Ok ? dcResult.Reason : acResult.Reason);
    }

    /// <summary>
    /// The Optimize Sleep function: a coherent idle-to-sleep ladder that stops the machine
    /// from half-sleeping, waking itself, or timing out mid-session.
    /// </summary>
    public static List<PowerStep> OptimizeSleep()
    {
        var steps = new List<PowerStep>();
        bool hibernate = MachineInfo.HibernateAvailable;
        SaveBackup();

        steps.Add(Step("Display off after 10 min", ScreenOff, 600, 300));
        steps.Add(Step("Sleep after 30 min", SleepAfter, 1800, 1200));

        if (hibernate)
        {
            steps.Add(Step("Hibernate after 2 h", HibernateAfter, 7200, 5400));
            steps.Add(Step("Hybrid sleep enabled", HybridSleep, 1, 1));
        }
        else
        {
            steps.Add(new PowerStep("Hibernate ladder", false, "Hibernation is not enabled on this PC (no hiberfil.sys)."));
        }

        steps.Add(Step("Wake timers disabled", WakeTimers, 0, 0));
        steps.Add(Step("USB selective suspend off on AC", UsbSuspend, 0, 1));
        steps.Add(Step("Disk idle 20 min", DiskIdle, 1200, 600));

        return steps;
    }

    public static (bool Ok, string Message) RestoreBackup()
    {
        Dictionary<string, int[]>? backup;
        try
        {
            if (!File.Exists(BackupFile)) return (false, "No Optimize Sleep backup recorded yet.");
            backup = JsonSerializer.Deserialize<Dictionary<string, int[]>>(File.ReadAllText(BackupFile));
        }
        catch (Exception ex)
        {
            return (false, $"Backup unreadable: {ex.Message}");
        }

        if (backup is null || backup.Count == 0) return (false, "Backup is empty.");

        int restored = 0;
        var failures = new List<string>();
        foreach (PowerSetting setting in AllSettings)
        {
            if (!backup.TryGetValue(setting.Key, out int[]? values) || values.Length < 2) continue;
            if (values[0] < 0 || values[1] < 0) continue; // was unknown
            (bool ok, string _) = Apply(setting, values[0], values[1]);
            if (ok) restored++;
            else failures.Add(setting.Label);
        }

        if (failures.Count == 0) return (true, $"Restored {restored} sleep settings.");
        return (restored > 0, $"Restored {restored}; failed: {string.Join(", ", failures)}");
    }

    /// <summary>Current processor ceiling in percent; 100 when the active plan does not cap it.</summary>
    public static int CpuCeiling
    {
        get
        {
            PowerValue value = Read(ProcessorMax);
            int ceiling = value.Ac ?? 100;
            return ceiling <= 0 || ceiling > 100 ? 100 : ceiling;
        }
    }

    public static bool CpuCapped => CpuCeiling < 100;

    /// <summary>
    /// Reduces CPU heat, power draw and clock speed by lowering the plan's maximum processor state.
    /// The previous ceiling is stored first, so <see cref="RestoreCpuCeiling"/> can put it back.
    /// </summary>
    public static (bool Ok, string Message) CapCpu(int percent)
    {
        int current = CpuCeiling;
        if (current <= percent) return (false, $"The processor ceiling is already {current}%.");

        SaveCpuCeiling(current);
        (bool ok, string message) = Apply(ProcessorMax, percent);
        return ok ? (true, $"Processor ceiling {current}% \u2192 {percent}%.") : (false, message);
    }

    public static (bool Ok, string Message) RestoreCpuCeiling()
    {
        int previous = 100;

        try
        {
            if (File.Exists(CpuCapFile)) previous = JsonSerializer.Deserialize<int>(File.ReadAllText(CpuCapFile));
        }
        catch
        {
            // An unreadable backup simply means "no cap".
        }

        if (previous <= 0 || previous > 100) previous = 100;

        (bool ok, string message) = Apply(ProcessorMax, previous);
        return ok ? (true, $"Processor ceiling restored to {previous}%.") : (false, message);
    }

    private static void SaveCpuCeiling(int percent)
    {
        try
        {
            string? folder = Path.GetDirectoryName(CpuCapFile);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(CpuCapFile, JsonSerializer.Serialize(percent));
        }
        catch (Exception ex)
        {
            ActivityLog.Warn($"Could not back up the processor ceiling: {ex.Message}");
        }
    }

    public static void SetHibernate(bool enabled) => Cmd.Run("powercfg.exe", enabled ? "/hibernate on" : "/hibernate off", 15000);

    public static bool FastStartupEnabled
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
                return key?.GetValue("HiberbootEnabled") is int value && value == 1;
            }
            catch
            {
                return false;
            }
        }
    }

    public static (bool Ok, string Message) SetFastStartup(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", writable: true)
                ?? throw new InvalidOperationException("Power policy key is not writable.");

            key.SetValue("HiberbootEnabled", enabled ? 1 : 0, RegistryValueKind.DWord);
            return (true, enabled ? "Fast Startup armed" : "Fast Startup disabled");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Administrator privileges are required for Fast Startup.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Runs Optimize Sleep off the UI thread and writes every step to the activity feed.</summary>
    public static async Task<List<PowerStep>> OptimizeSleepAsync()
    {
        List<PowerStep> steps = await Task.Run(OptimizeSleep);
        foreach (PowerStep step in steps)
        {
            ActivityLog.Write(step.Ok ? LogLevel.Success : LogLevel.Warn, $"{step.Label}: {step.Detail}");
        }

        return steps;
    }

    private static PowerStep Step(string label, PowerSetting setting, int ac, int dc)
    {
        (bool ok, string message) = Apply(setting, ac, dc);
        return new PowerStep(label, ok, message);
    }

    private static void SaveBackup()
    {
        try
        {
            var snapshot = new Dictionary<string, int[]>();
            foreach (PowerSetting setting in AllSettings)
            {
                PowerValue value = Read(setting);
                snapshot[setting.Key] = new[] { value.Ac ?? -1, value.Dc ?? -1 };
            }

            string? folder = Path.GetDirectoryName(BackupFile);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(BackupFile, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            ActivityLog.Warn($"Could not save sleep backup: {ex.Message}");
        }
    }
}
