using Microsoft.Win32;

namespace PC_Medic;

internal enum RegistryCategory
{
    Uninstall,
    AppPath,
    SharedDll,
    Autorun,
}

/// <summary>One registry entry whose target is provably gone.</summary>
internal sealed record RegistryIssue
{
    public required RegistryCategory Category { get; init; }

    /// <summary>"HKLM" or "HKCU".</summary>
    public required string HiveName { get; init; }

    /// <summary>Key that owns the entry, relative to the hive.</summary>
    public required string KeyPath { get; init; }

    /// <summary>Subkey to delete, when the entry is a whole key.</summary>
    public string? SubKey { get; init; }

    /// <summary>Value to delete, when the entry is a single value.</summary>
    public string? ValueName { get; init; }

    public required string Display { get; init; }

    /// <summary>The path the entry points at that no longer exists.</summary>
    public required string MissingPath { get; init; }

    /// <summary>Reported, never deleted here: stale autoruns are fixed on the Startup Apps page.</summary>
    public bool ReviewOnly { get; init; }

    public bool MachineWide => HiveName == "HKLM";

    public string RegistryPath => SubKey is null
        ? $"{HiveName}\\{KeyPath}"
        : $"{HiveName}\\{KeyPath}\\{SubKey}";

    public string CategoryLabel => Category switch
    {
        RegistryCategory.Uninstall => "UNINSTALL ENTRY",
        RegistryCategory.AppPath => "APP PATH",
        RegistryCategory.SharedDll => "SHARED DLL",
        _ => "STALE AUTORUN",
    };

    public Color CategoryTone => Category switch
    {
        RegistryCategory.Uninstall => Theme.Violet,
        RegistryCategory.AppPath => Theme.Accent,
        RegistryCategory.SharedDll => Theme.Warn,
        _ => Theme.TextDim,
    };
}

/// <summary>
/// A deliberately narrow registry audit, in the spirit of Little Registry Cleaner but built
/// around proof instead of heuristics: an entry is only reported when the file it points at
/// is missing from a fixed drive that is mounted right now. Removable and network paths are
/// never treated as evidence, MSI and Windows-managed entries are left alone, and every
/// removal first exports the owning key to a .reg file.
/// </summary>
internal static class RegistryService
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UninstallKey32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UserUninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string AppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
    private const string AppPathsKey32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths";
    private const string UserAppPathsKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths";
    private const string SharedDllsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs";

    public static string BackupRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCMedic",
        "registry-backups");

    public static List<RegistryIssue> Scan(IProgress<int>? progress = null)
    {
        var issues = new List<RegistryIssue>();

        progress?.Report(0);

        ScanUninstall(issues, RegistryHive.LocalMachine, UninstallKey);
        ScanUninstall(issues, RegistryHive.LocalMachine, UninstallKey32);
        ScanUninstall(issues, RegistryHive.CurrentUser, UserUninstallKey);

        progress?.Report(30);

        ScanAppPaths(issues, RegistryHive.LocalMachine, AppPathsKey);
        ScanAppPaths(issues, RegistryHive.LocalMachine, AppPathsKey32);
        ScanAppPaths(issues, RegistryHive.CurrentUser, UserAppPathsKey);

        progress?.Report(60);

        ScanSharedDlls(issues);
        progress?.Report(80);

        ScanAutorun(issues);
        progress?.Report(100);

        return issues
            .OrderBy(issue => issue.ReviewOnly)
            .ThenBy(issue => issue.Category)
            .ThenBy(issue => issue.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Removes the selected entries, exporting their keys first. Nothing is deleted if the backup fails.</summary>
    public static (bool Ok, string Message) Remove(IReadOnlyList<RegistryIssue> issues)
    {
        if (issues.Count == 0) return (false, "Nothing is selected.");

        if (issues.Any(issue => issue.MachineWide) && !Native.IsElevated)
        {
            return (false, "Machine-wide keys need an elevated run \u2014 use RESTART AS ADMIN first.");
        }

        string folder = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            return (false, $"Backup folder could not be created: {ex.Message}");
        }

        int index = 0;
        foreach (string target in issues.Select(BackupTarget).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            index++;
            string file = Path.Combine(folder, $"{index:00}-{Theme.Truncate(SafeFileName(target), 80)}.reg");
            CmdResult export = Cmd.Run("reg.exe", $"export \"{target}\" \"{file}\" /y", 20000);

            if (!export.Ok || !File.Exists(file) || new FileInfo(file).Length < 32)
            {
                return (false, $"Backup for {target} failed ({export.Reason}). Nothing was removed.");
            }
        }

        int removed = 0;
        var failed = new List<string>();

        foreach (RegistryIssue issue in issues)
        {
            try
            {
                if (Delete(issue)) removed++;
                else failed.Add(issue.Display);
            }
            catch (Exception ex)
            {
                failed.Add($"{issue.Display} ({ex.Message})");
            }
        }

        if (failed.Count > 0)
        {
            ActivityLog.Warn($"Registry clean skipped: {Theme.Truncate(string.Join(", ", failed), 200)}");
        }

        return removed == 0
            ? (false, "Nothing could be removed \u2014 see the activity log.")
            : (true, $"{removed} of {issues.Count} removed \u2014 backup {Path.GetFileName(folder)}");
    }

    public static string? LatestBackupFolder()
    {
        try
        {
            if (!Directory.Exists(BackupRoot)) return null;

            return Directory.GetDirectories(BackupRoot)
                .OrderByDescending(folder => Path.GetFileName(folder), StringComparer.Ordinal)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Imports every .reg file from the most recent backup folder.</summary>
    public static (bool Ok, string Message) RestoreLatest()
    {
        string? folder = LatestBackupFolder();
        if (folder is null) return (false, "No registry backup exists yet.");

        string[] files;

        try
        {
            files = Directory.GetFiles(folder, "*.reg");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }

        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        if (files.Length == 0) return (false, "The latest backup folder is empty.");

        bool machineKeys = files.Any(file => ReadText(file).Contains("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase));
        if (machineKeys && !Native.IsElevated)
        {
            return (false, "That backup contains machine-wide keys \u2014 restart as admin to restore it.");
        }

        int restored = 0;
        foreach (string file in files)
        {
            CmdResult result = Cmd.Run("reg.exe", $"import \"{file}\"", 30000);
            if (result.Ok) restored++;
            else ActivityLog.Warn($"Registry restore: {Path.GetFileName(file)} \u2014 {result.Reason}");
        }

        return restored == 0
            ? (false, "Restore failed \u2014 see the activity log.")
            : (true, $"{restored} of {files.Length} backup files restored");
    }

    private static void ScanUninstall(List<RegistryIssue> issues, RegistryHive hive, string keyPath)
    {
        string hiveName = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";

        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using RegistryKey? root = baseKey.OpenSubKey(keyPath);
            if (root is null) return;

            foreach (string name in root.GetSubKeyNames())
            {
                using RegistryKey? entry = root.OpenSubKey(name);
                if (entry is null) continue;

                string display = (entry.GetValue("DisplayName") as string ?? string.Empty).Trim();
                if (display.Length == 0) continue;
                if (ManagedByWindows(entry)) continue;

                string command = (entry.GetValue("UninstallString") as string)
                    ?? (entry.GetValue("QuietUninstallString") as string)
                    ?? string.Empty;

                if (command.Length == 0) continue;
                if (command.Contains("msiexec", StringComparison.OrdinalIgnoreCase)) continue;

                string? target = ExecutableFrom(command);
                if (!IsProvablyMissing(target)) continue;
                if (StillInstalled(entry)) continue;

                issues.Add(new RegistryIssue
                {
                    Category = RegistryCategory.Uninstall,
                    HiveName = hiveName,
                    KeyPath = keyPath,
                    SubKey = name,
                    Display = display,
                    MissingPath = Expanded(target) ?? target ?? command,
                });
            }
        }
        catch (Exception ex)
        {
            ActivityLog.Warn($"Uninstall scan skipped ({keyPath}): {ex.Message}");
        }
    }

    private static void ScanAppPaths(List<RegistryIssue> issues, RegistryHive hive, string keyPath)
    {
        string hiveName = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";

        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using RegistryKey? root = baseKey.OpenSubKey(keyPath);
            if (root is null) return;

            foreach (string name in root.GetSubKeyNames())
            {
                using RegistryKey? entry = root.OpenSubKey(name);
                if (entry is null) continue;

                string target = (entry.GetValue(null) as string ?? string.Empty).Trim();
                if (target.Length == 0) continue;
                if (!IsProvablyMissing(target)) continue;

                issues.Add(new RegistryIssue
                {
                    Category = RegistryCategory.AppPath,
                    HiveName = hiveName,
                    KeyPath = keyPath,
                    SubKey = name,
                    Display = name,
                    MissingPath = Expanded(target) ?? target,
                });
            }
        }
        catch (Exception ex)
        {
            ActivityLog.Warn($"App Paths scan skipped ({keyPath}): {ex.Message}");
        }
    }

    private static void ScanSharedDlls(List<RegistryIssue> issues)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? root = baseKey.OpenSubKey(SharedDllsKey);
            if (root is null) return;

            foreach (string valueName in root.GetValueNames())
            {
                if (!IsProvablyMissing(valueName)) continue;

                issues.Add(new RegistryIssue
                {
                    Category = RegistryCategory.SharedDll,
                    HiveName = "HKLM",
                    KeyPath = SharedDllsKey,
                    ValueName = valueName,
                    Display = SafeFileName(Path.GetFileName(valueName.Trim('"'))) is { Length: > 0 } file
                        ? file
                        : valueName,
                    MissingPath = Expanded(valueName) ?? valueName,
                });
            }
        }
        catch (Exception ex)
        {
            ActivityLog.Warn($"Shared DLL scan skipped: {ex.Message}");
        }
    }

    /// <summary>Stale Run values are listed for review but cleaned from the Startup Apps page.</summary>
    private static void ScanAutorun(List<RegistryIssue> issues)
    {
        try
        {
            foreach (StartupEntry entry in StartupService.Load())
            {
                if (entry.Kind != StartupKind.RegistryRun) continue;
                if (string.IsNullOrWhiteSpace(entry.FilePath)) continue;
                if (!IsProvablyMissing(entry.FilePath)) continue;

                issues.Add(new RegistryIssue
                {
                    Category = RegistryCategory.Autorun,
                    HiveName = entry.Hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU",
                    KeyPath = entry.KeyPath,
                    ValueName = entry.ValueName,
                    Display = entry.Name,
                    MissingPath = entry.FilePath,
                    ReviewOnly = true,
                });
            }
        }
        catch (Exception ex)
        {
            ActivityLog.Warn($"Autorun scan skipped: {ex.Message}");
        }
    }

    /// <summary>What the backup export has to capture: the subkey itself, or the key holding the value.</summary>
    private static string BackupTarget(RegistryIssue issue) => issue.SubKey is null
        ? $"{issue.HiveName}\\{issue.KeyPath}"
        : $"{issue.HiveName}\\{issue.KeyPath}\\{issue.SubKey}";

    private static bool Delete(RegistryIssue issue)
    {
        RegistryHive hive = issue.MachineWide ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;

        using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using RegistryKey? parent = baseKey.OpenSubKey(issue.KeyPath, writable: true);
        if (parent is null) return false;

        if (issue.SubKey is not null)
        {
            if (!parent.GetSubKeyNames().Contains(issue.SubKey, StringComparer.OrdinalIgnoreCase)) return false;
            parent.DeleteSubKeyTree(issue.SubKey, throwOnMissingSubKey: false);
            return true;
        }

        if (issue.ValueName is null) return false;
        string? match = parent.GetValueNames()
            .FirstOrDefault(name => string.Equals(name, issue.ValueName, StringComparison.OrdinalIgnoreCase));

        if (match is null) return false;
        parent.DeleteValue(match, throwOnMissingValue: false);
        return true;
    }

    private static bool ManagedByWindows(RegistryKey entry)
    {
        if (Int(entry, "SystemComponent") == 1) return true;
        if (Int(entry, "NoRemove") == 1) return true;
        if (Int(entry, "WindowsInstaller") == 1) return true;
        if ((entry.GetValue("ParentKeyName") as string ?? string.Empty).Length > 0) return true;
        if ((entry.GetValue("ParentDisplayName") as string ?? string.Empty).Length > 0) return true;
        return false;
    }

    /// <summary>An install location or icon that still resolves means the app is there, whatever the uninstaller says.</summary>
    private static bool StillInstalled(RegistryKey entry)
    {
        if ((entry.GetValue("InstallLocation") as string) is { Length: > 0 } location)
        {
            string? expanded = Expanded(location);
            if (expanded is not null && (Directory.Exists(expanded) || File.Exists(expanded))) return true;
        }

        if ((entry.GetValue("DisplayIcon") as string) is { Length: > 0 } icon)
        {
            string? expanded = Expanded(icon);
            if (expanded is not null && !IsProvablyMissing(expanded)) return true;
        }

        return false;
    }

    private static int Int(RegistryKey key, string name)
    {
        try
        {
            return key.GetValue(name) switch
            {
                int value => value,
                long value => (int)value,
                string text when int.TryParse(text, out int parsed) => parsed,
                _ => 0,
            };
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Pulls the executable out of a command line, honouring quotes and unquoted paths with spaces.</summary>
    internal static string? ExecutableFrom(string command)
    {
        string text = command.Trim();
        if (text.Length == 0) return null;

        if (text[0] == '"')
        {
            int close = text.IndexOf('"', 1);
            return close > 1 ? text[1..close] : null;
        }

        int exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe >= 0)
        {
            string candidate = text[..(exe + 4)];
            return candidate.Length > 4 ? candidate : null;
        }

        int space = text.IndexOf(' ');
        return space > 0 ? text[..space] : text;
    }

    /// <summary>Expands a registry path; returns null when it cannot be resolved honestly.</summary>
    internal static string? Expanded(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        string text = raw.Trim().Trim('"');

        int comma = text.LastIndexOf(',');
        if (comma > 2 && text.Length - comma <= 3) text = text[..comma];

        try
        {
            text = Environment.ExpandEnvironmentVariables(text);
        }
        catch
        {
            return null;
        }

        if (text.Contains('%') || text.Contains('\0')) return null;
        return text;
    }

    /// <summary>True only when the path is rooted, on a mounted fixed drive, and absent.</summary>
    internal static bool IsProvablyMissing(string? raw)
    {
        string? path = Expanded(raw);
        if (path is null || path.Length < 4) return false;
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        if (!Path.IsPathRooted(path)) return false;

        string? root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root)) return false;

        try
        {
            var drive = new DriveInfo(root);
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady) return false;
        }
        catch
        {
            return false;
        }

        try
        {
            return !File.Exists(path) && !Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static string ReadText(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SafeFileName(string text)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(text.Length);

        foreach (char character in text)
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        }

        return builder.ToString();
    }
}
