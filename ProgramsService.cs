using System.Diagnostics;
using Microsoft.Win32;

namespace PC_Medic;

/// <summary>One registered application from the Add or Remove Programs registry.</summary>
internal sealed class ProgramEntry
{
    public required string DisplayName { get; init; }

    public string Version { get; init; } = string.Empty;

    public string Publisher { get; init; } = string.Empty;

    public ulong SizeBytes { get; init; }

    /// <summary>Already normalised to 2026-01-31, or empty.</summary>
    public string InstallDate { get; init; } = string.Empty;

    public string UninstallCommand { get; init; } = string.Empty;

    public string InstallLocation { get; init; } = string.Empty;

    /// <summary>"HKLM" or "HKCU".</summary>
    public string HiveName { get; init; } = "HKLM";

    public string KeyPath { get; init; } = string.Empty;

    public required string SubKey { get; init; }

    /// <summary>True only when the uninstaller is provably gone from a mounted fixed drive.</summary>
    public bool Dead { get; init; }

    public string DeadReason { get; init; } = string.Empty;

    public bool MachineWide => HiveName == "HKLM";

    public bool IsMsi => UninstallCommand.Contains("msiexec", StringComparison.OrdinalIgnoreCase);

    public bool HasUninstaller => UninstallCommand.Length > 0;

    public string SizeLabel => SizeBytes == 0 ? string.Empty : Theme.Bytes(SizeBytes);

    public string RegistryPath => $"{HiveName}\\{KeyPath}\\{SubKey}";

    public string DetailLine
    {
        get
        {
            if (Dead) return $"DEAD  {Theme.Truncate(DeadReason, 96)}";

            var parts = new List<string>(4);
            if (Publisher.Length > 0) parts.Add(Theme.Truncate(Publisher, 34));
            if (Version.Length > 0) parts.Add($"v{Version}");
            if (SizeLabel.Length > 0) parts.Add(SizeLabel);
            if (InstallDate.Length > 0) parts.Add(InstallDate);
            if (parts.Count == 0) parts.Add(HasUninstaller ? "NO PUBLISHER RECORDED" : "NO UNINSTALLER REGISTERED");

            return string.Join("  \u2022  ", parts);
        }
    }
}

/// <summary>
/// Reads the uninstall registry the way Programs and Features does, then separates the entries
/// that still work from the ones whose uninstaller is provably gone. Dead entries are cleared
/// through RegistryService, so they get the same .reg backup before anything is deleted.
/// </summary>
internal static class ProgramsService
{
    private const string MachineKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string MachineKey32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UserKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<ProgramEntry> Load()
    {
        var entries = new List<ProgramEntry>();

        Scan(entries, RegistryHive.LocalMachine, MachineKey, "HKLM");
        Scan(entries, RegistryHive.LocalMachine, MachineKey32, "HKLM");
        Scan(entries, RegistryHive.CurrentUser, UserKey, "HKCU");

        return entries
            .GroupBy(entry => entry.RegistryPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(entry => entry.Dead ? 0 : 1)
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Starts the registered uninstaller, elevating only when the entry is machine-wide.</summary>
    public static (bool Ok, string Message) Uninstall(ProgramEntry entry)
    {
        if (!entry.HasUninstaller)
        {
            return (false, $"{entry.DisplayName} has no uninstaller registered.");
        }

        string? exe = RegistryService.ExecutableFrom(entry.UninstallCommand);
        if (string.IsNullOrWhiteSpace(exe))
        {
            return (false, "The uninstall command could not be parsed.");
        }

        string trimmed = entry.UninstallCommand.Trim();
        int at = trimmed.IndexOf(exe, StringComparison.OrdinalIgnoreCase);
        string args = at < 0 ? string.Empty : trimmed[(at + exe.Length)..].Trim();

        string target = exe;
        if (!Path.IsPathRooted(exe) &&
            Path.GetFileName(exe).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
        {
            target = Path.Combine(Environment.SystemDirectory, "msiexec.exe");
        }

        var start = new ProcessStartInfo
        {
            FileName = target,
            Arguments = args,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(target) ?? AppContext.BaseDirectory,
        };

        if (entry.MachineWide && !Native.IsElevated) start.Verb = "runas";

        try
        {
            Process.Start(start);
            return (true, $"Uninstaller started for {entry.DisplayName} \u2014 rescan when it finishes.");
        }
        catch (Exception ex)
        {
            return (false, $"Uninstaller did not start: {ex.Message}");
        }
    }

    /// <summary>Dead entries are whole uninstall keys, so they travel as registry issues.</summary>
    public static List<RegistryIssue> ToIssues(IEnumerable<ProgramEntry> entries) => entries
        .Select(entry => new RegistryIssue
        {
            Category = RegistryCategory.Uninstall,
            HiveName = entry.HiveName,
            KeyPath = entry.KeyPath,
            SubKey = entry.SubKey,
            Display = entry.DisplayName,
            MissingPath = entry.DeadReason,
        })
        .ToList();

    private static void Scan(List<ProgramEntry> entries, RegistryHive hive, string keyPath, string hiveName)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using RegistryKey? root = baseKey.OpenSubKey(keyPath);
            if (root is null) return;

            foreach (string subKey in root.GetSubKeyNames())
            {
                using RegistryKey? key = root.OpenSubKey(subKey);
                if (key is null) continue;

                string name = (key.GetValue("DisplayName") as string ?? string.Empty).Trim();
                if (name.Length == 0 || Hidden(name, key)) continue;

                string command = FirstCommand(key);
                string location = (key.GetValue("InstallLocation") as string ?? string.Empty).Trim();
                (bool dead, string reason) = Assess(command, location, key.GetValue("DisplayIcon") as string);

                entries.Add(new ProgramEntry
                {
                    DisplayName = name,
                    Version = (key.GetValue("DisplayVersion") as string ?? string.Empty).Trim(),
                    Publisher = (key.GetValue("Publisher") as string ?? string.Empty).Trim(),
                    SizeBytes = (ulong)Math.Max(0, Number(key, "EstimatedSize")) * 1024UL,
                    InstallDate = DateLabel(key.GetValue("InstallDate") as string),
                    UninstallCommand = command,
                    InstallLocation = location,
                    HiveName = hiveName,
                    KeyPath = keyPath,
                    SubKey = subKey,
                    Dead = dead,
                    DeadReason = reason,
                });
            }
        }
        catch (Exception ex)
        {
            ActivityLog.Warn($"Program list skipped ({keyPath}): {ex.Message}");
        }
    }

    /// <summary>Hides updates, components and anything Windows says not to show, as Programs and Features does.</summary>
    private static bool Hidden(string name, RegistryKey key)
    {
        if (Number(key, "SystemComponent") == 1) return true;
        if (Number(key, "NoRemove") == 1) return true;
        if ((key.GetValue("ParentKeyName") as string ?? string.Empty).Length > 0) return true;
        if ((key.GetValue("ParentDisplayName") as string ?? string.Empty).Length > 0) return true;

        if ((key.GetValue("ReleaseType") as string ?? string.Empty) is { Length: > 0 } release &&
            (release.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
             release.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Cumulative updates are named KB1234567 and carry no publisher.
        if (name.Length > 2 &&
            (name[0] is 'K' or 'k') &&
            (name[1] is 'B' or 'b') &&
            name[2..].All(char.IsDigit))
        {
            return true;
        }

        return false;
    }

    private static string FirstCommand(RegistryKey key)
    {
        if ((key.GetValue("UninstallString") as string) is { Length: > 0 } normal) return normal.Trim();
        if ((key.GetValue("QuietUninstallString") as string) is { Length: > 0 } quiet) return quiet.Trim();
        return string.Empty;
    }

    /// <summary>
    /// Dead means proven: an uninstaller on a mounted fixed drive that is gone, while neither the
    /// install folder nor the recorded icon survives. MSI entries are never called dead, because
    /// proving that Windows Installer dropped a product needs a database read this app avoids.
    /// </summary>
    private static (bool Dead, string Reason) Assess(string command, string location, string? icon)
    {
        if (command.Length == 0) return (false, string.Empty);
        if (command.Contains("msiexec", StringComparison.OrdinalIgnoreCase)) return (false, string.Empty);

        string? exe = RegistryService.ExecutableFrom(command);
        if (!RegistryService.IsProvablyMissing(exe)) return (false, string.Empty);
        if (LocationPresent(location)) return (false, string.Empty);
        if (icon is { Length: > 0 } && !RegistryService.IsProvablyMissing(icon)) return (false, string.Empty);

        return (true, $"Uninstaller missing: {RegistryService.Expanded(exe) ?? exe}");
    }

    private static bool LocationPresent(string location) =>
        location.Length > 0 && !RegistryService.IsProvablyMissing(location);

    private static long Number(RegistryKey key, string name)
    {
        try
        {
            return key.GetValue(name) switch
            {
                int value => value,
                long value => value,
                string text when long.TryParse(text, out long parsed) => parsed,
                _ => 0,
            };
        }
        catch
        {
            return 0;
        }
    }

    private static string DateLabel(string? raw)
    {
        if (raw is null || raw.Length != 8) return string.Empty;
        if (!raw.All(char.IsDigit)) return string.Empty;
        return $"{raw[..4]}-{raw[4..6]}-{raw[6..]}";
    }
}
