using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace PC_Medic;

internal sealed record PortableApp(
    string Title,
    string Folder,
    string ExeName,
    string Url,
    string Glyph,
    string Note,
    RigMark Mark,
    string WingetId,
    string InstallName);

/// <summary>
/// Windows tool launcher + portable benchmark detection, ported from PC-Utilities.
/// Portable apps are looked up beside the executable so the suite stays USB-friendly.
/// </summary>
internal static class Tools
{
    public static readonly PortableApp[] Portables =
    {
        new("CINEBENCH", "CinebenchPortable", "Cinebench.exe", "https://www.maxon.net/en/downloads/cinebench-2024-downloads", "\uE9D9", "CPU render benchmark", RigMark.Cinebench, "Maxon.CinebenchR23", "Cinebench"),
        new("FURMARK", "FurMarkPortable", "FurMark_GUI.exe", "https://geeks3d.com/furmark/downloads/", "\uE7F4", "GPU stress + thermals", RigMark.FurMark, "Geeks3D.FurMark.1", "FurMark"),
        new("HWINFO64", "HardwareInfoPortable", "HWiNFO64.exe", "https://www.hwinfo.com/download/", "\uE9D2", "Sensors + full specs", RigMark.HwInfo, "REALiX.HWiNFO", "HWiNFO"),
        new("CRYSTALDISKMARK", "CrystalDiskMarkPortable", "DiskMark64.exe", "https://portableapps.com/apps/utilities/crystaldiskmark_portable", "\uE958", "Sequential + random I/O", RigMark.DiskMark, "CrystalDewWorld.CrystalDiskMark", "CrystalDiskMark"),
        new("CRYSTALDISKINFO", "CrystalDiskInfoPortable", "DiskInfo64.exe", "https://portableapps.com/apps/utilities/crystaldiskinfo_portable", "\uE7BA", "Drive health / SMART", RigMark.DiskInfo, "CrystalDewWorld.CrystalDiskInfo", "CrystalDiskInfo"),
    };

    public static string PortableRoot => AppContext.BaseDirectory;

    public static string SystemExe(string fileName) => Path.Combine(Environment.SystemDirectory, fileName);

    /// <summary>A companion exe built beside PC Medic, such as the Disk Defrag tool; null when absent.</summary>
    public static string? Companion(string fileName)
    {
        try
        {
            string beside = Path.Combine(AppContext.BaseDirectory, fileName);
            return File.Exists(beside) ? beside : null;
        }
        catch
        {
            return null;
        }
    }

    public static string? ResolvePortable(PortableApp app)
    {
        string? remembered = StoredPath(app);
        if (remembered is not null && File.Exists(remembered)) return remembered;

        try
        {
            string folder = Path.Combine(PortableRoot, app.Folder);
            string direct = Path.Combine(folder, app.ExeName);
            if (File.Exists(direct)) return direct;
            if (!Directory.Exists(folder)) return null;

            foreach (string candidate in Directory.EnumerateFiles(folder, app.ExeName, SearchOption.AllDirectories))
            {
                return candidate;
            }
        }
        catch (Exception ex)
        {
            ActivityLog.Warn($"{app.Title}: {ex.Message}");
        }

        return null;
    }

    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCMedic",
        "rig-paths.json");

    private static Dictionary<string, string>? _customPaths;

    /// <summary>The remembered custom executable for a rig, if the user picked one.</summary>
    public static string? StoredPath(PortableApp app)
        => Paths.TryGetValue(app.Folder, out string? path) ? path : null;

    public static (bool Ok, string Message) SetCustomPath(PortableApp app, string executable)
    {
        try
        {
            string full = Path.GetFullPath(executable);
            if (!File.Exists(full)) return (false, "That file does not exist.");

            Dictionary<string, string> paths = Paths;
            paths[app.Folder] = full;

            try
            {
                SavePaths();
            }
            catch (Exception ex)
            {
                // Memory and disk stay honest: a path that could not be stored is not remembered at all,
                // so the tile never claims CUSTOM for something the next launch will forget.
                paths.Remove(app.Folder);
                return (false, $"found it, but the path could not be remembered ({ex.Message})");
            }

            return (true, full);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static void ClearCustomPath(PortableApp app)
    {
        if (!Paths.Remove(app.Folder)) return;

        try
        {
            SavePaths();
        }
        catch (Exception ex)
        {
            ActivityLog.Warn($"Remembered rig paths could not be saved: {ex.Message}");
        }
    }

    private static Dictionary<string, string> Paths
    {
        get
        {
            if (_customPaths is not null) return _customPaths;

            try
            {
                if (File.Exists(StorePath))
                {
                    Dictionary<string, string>? loaded =
                        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(StorePath));

                    if (loaded is not null)
                    {
                        _customPaths = new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
                        return _customPaths;
                    }
                }
            }
            catch (Exception ex)
            {
                ActivityLog.Warn($"Remembered rig paths unreadable: {ex.Message}");
            }

            _customPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return _customPaths;
        }
    }

    /// <summary>
    /// Writes beside the store and swaps it in, so an interrupted save can never leave a truncated file
    /// that would read back as "no remembered rigs". Throws on failure so the caller can say so.
    /// </summary>
    private static void SavePaths()
    {
        string json = JsonSerializer.Serialize(Paths, new JsonSerializerOptions { WriteIndented = true });

        string? folder = Path.GetDirectoryName(StorePath);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        string temp = StorePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, StorePath, overwrite: true);
    }

    public static (bool Ok, string Message) Launch(string fileName, string? arguments = null, bool elevated = false)
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true,
            };

            if (elevated) start.Verb = "runas";
            Process.Start(start);
            return (true, Path.GetFileName(fileName));
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static (bool Ok, string Message) LaunchTool(string title, string fileName, string? arguments = null, bool elevated = false)
    {
        (bool ok, string message) = Launch(fileName, arguments, elevated);
        if (ok) ActivityLog.Success($"{title} launched.");
        else ActivityLog.Error($"{title} failed: {message}");
        return (ok, message);
    }

    public static (bool Ok, string Message) OpenUrl(string url) => Launch(url);

    public static (bool Ok, string Message) LaunchPortable(PortableApp app)
    {
        string? path = ResolvePortable(app);
        if (path is null)
        {
            ActivityLog.Warn($"{app.Title} is not in {app.Folder}. Opening the download page.");
            OpenUrl(app.Url);
            return (false, "Not installed");
        }

        (bool ok, string message) = Launch(path);
        if (ok) ActivityLog.Success($"{app.Title} launched.");
        else ActivityLog.Error($"{app.Title} failed: {message}");
        return (ok, message);
    }

    public static int MenuShowDelay
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                if (key?.GetValue("MenuShowDelay") is string raw && int.TryParse(raw, out int value)) return value;
            }
            catch
            {
                // Unreadable value: report the Windows default.
            }

            return 400;
        }
    }

    public static (bool Ok, string Message) SetMenuShowDelay(int milliseconds)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true)
                ?? throw new InvalidOperationException("Desktop key is not writable.");

            key.SetValue("MenuShowDelay", milliseconds.ToString(), RegistryValueKind.String);
            ActivityLog.Success($"Menu animation delay set to {milliseconds} ms.");
            return (true, $"{milliseconds} ms");
        }
        catch (Exception ex)
        {
            ActivityLog.Error($"Animation speed: {ex.Message}");
            return (false, ex.Message);
        }
    }

    public static (bool Ok, string Message) UnlockUltimatePowerPlan()
    {
        CmdResult result = Cmd.Run("powercfg.exe", "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61", 10000);
        if (result.Ok)
        {
            ActivityLog.Success("Ultimate Performance plan unlocked.");
            return (true, "Unlocked");
        }

        ActivityLog.Warn($"Ultimate plan needs an elevated run: {result.Reason}");
        return (false, result.Reason);
    }
}
