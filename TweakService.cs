using Microsoft.Win32;

namespace PC_Medic;

/// <summary>Result of applying or reverting one tweak.</summary>
internal sealed record TweakOutcome(bool Ok, string Message);

/// <summary>One registry value a tweak writes, so its original can be captured first.</summary>
internal sealed record TweakPoint(RegistryHive Hive, string Path, string Name)
{
    public string HiveName => Hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";
}

/// <summary>A reversible performance setting: how to read it, how to flip it, and what it changes.</summary>
internal sealed class Tweak
{
    public required string Key { get; init; }

    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    /// <summary>Short scope badge: HKCU for per-user, ADMIN for machine-wide.</summary>
    public required string Badge { get; init; }

    public Color Tone { get; init; } = Theme.Accent;

    /// <summary>Machine-wide writes need an elevated run.</summary>
    public bool RequiresAdmin { get; init; }

    /// <summary>Registry values this tweak writes; they are snapshotted before the first change.</summary>
    public IReadOnlyList<TweakPoint> Points { get; init; } = Array.Empty<TweakPoint>();

    /// <summary>True when the tweak toggles hibernation through powercfg instead of the registry.</summary>
    public bool HibernationFlag { get; init; }

    /// <summary>True when the performance-oriented state is in force.</summary>
    public required Func<bool> IsApplied { get; init; }

    /// <summary>Applies the tweak when true, restores the Windows default when false.</summary>
    public required Func<bool, TweakOutcome> Apply { get; init; }
}

/// <summary>
/// Six performance switches, each one a documented registry value or powercfg flag.
/// Nothing here is a one-way door: applying false puts the stock value back.
/// </summary>
internal static class TweakService
{
    private const string VisualEffectsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string GameConfigKey = @"System\GameConfigStore";
    private const string GameDvrKey = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string DataCollectionKey = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection";
    private const string BackgroundKey = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";

    public static IReadOnlyList<Tweak> All { get; } = Build();

    internal static string HibernateFile =>
        Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "hiberfil.sys");

    private static List<Tweak> Build() => new()
    {
        new Tweak
        {
            Key = "visual",
            Title = "BEST PERFORMANCE",
            Subtitle = "Best-performance visual effects profile",
            Badge = "HKCU",
            Tone = Theme.Accent,
            Points = new[] { new TweakPoint(RegistryHive.CurrentUser, VisualEffectsKey, "VisualFXSetting") },
            IsApplied = () => ReadDword(RegistryHive.CurrentUser, VisualEffectsKey, "VisualFXSetting", 0) == 2,
            Apply = performance => WriteDword(
                RegistryHive.CurrentUser,
                VisualEffectsKey,
                "VisualFXSetting",
                performance ? 2 : 0,
                performance
                    ? "Visual effects set to best performance."
                    : "Visual effects back to let Windows decide."),
        },
        new Tweak
        {
            Key = "transparency",
            Title = "TRANSPARENCY OFF",
            Subtitle = "Acrylic blur on Start, taskbar and flyouts",
            Badge = "HKCU",
            Tone = Theme.Violet,
            Points = new[] { new TweakPoint(RegistryHive.CurrentUser, PersonalizeKey, "EnableTransparency") },
            IsApplied = () => ReadDword(RegistryHive.CurrentUser, PersonalizeKey, "EnableTransparency", 1) == 0,
            Apply = off => WriteDword(
                RegistryHive.CurrentUser,
                PersonalizeKey,
                "EnableTransparency",
                off ? 0 : 1,
                off ? "Transparency blur disabled." : "Transparency blur restored."),
        },
        new Tweak
        {
            Key = "background",
            Title = "BACKGROUND APPS OFF",
            Subtitle = "Store apps suspended once they leave the foreground",
            Badge = "HKCU",
            Tone = Theme.Violet,
            Points = new[] { new TweakPoint(RegistryHive.CurrentUser, BackgroundKey, "GlobalUserDisabled") },
            IsApplied = () => ReadDword(RegistryHive.CurrentUser, BackgroundKey, "GlobalUserDisabled", 0) == 1,
            Apply = off => WriteDword(
                RegistryHive.CurrentUser,
                BackgroundKey,
                "GlobalUserDisabled",
                off ? 1 : 0,
                off ? "Background apps disabled." : "Background apps allowed again."),
        },
        new Tweak
        {
            Key = "gamedvr",
            Title = "GAME DVR OFF",
            Subtitle = "Background recording and the capture overlay",
            Badge = "HKCU",
            Tone = Theme.Good,
            Points = new[]
            {
                new TweakPoint(RegistryHive.CurrentUser, GameConfigKey, "GameDVR_Enabled"),
                new TweakPoint(RegistryHive.CurrentUser, GameDvrKey, "AppCaptureEnabled"),
            },
            IsApplied = () => ReadDword(RegistryHive.CurrentUser, GameConfigKey, "GameDVR_Enabled", 1) == 0,
            Apply = off => SetGameDvr(off),
        },
        new Tweak
        {
            Key = "telemetry",
            Title = "TELEMETRY OFF",
            Subtitle = "Diagnostic data policy pinned to Security (0)",
            Badge = "ADMIN",
            Tone = Theme.Warn,
            RequiresAdmin = true,
            Points = new[] { new TweakPoint(RegistryHive.LocalMachine, DataCollectionKey, "AllowTelemetry") },
            IsApplied = () => ReadDword(RegistryHive.LocalMachine, DataCollectionKey, "AllowTelemetry", 3) == 0,
            Apply = off => off
                ? WriteDword(
                    RegistryHive.LocalMachine,
                    DataCollectionKey,
                    "AllowTelemetry",
                    0,
                    "Diagnostic data policy set to Security (0).")
                : RemoveValue(
                    RegistryHive.LocalMachine,
                    DataCollectionKey,
                    "AllowTelemetry",
                    "Telemetry policy removed \u2014 the Windows default applies again."),
        },
        new Tweak
        {
            Key = "hibernate",
            Title = "HIBERNATION OFF",
            Subtitle = "Releases hiberfil.sys; Fast Startup goes with it",
            Badge = "ADMIN",
            Tone = Theme.Warn,
            RequiresAdmin = true,
            HibernationFlag = true,
            IsApplied = () => !File.Exists(HibernateFile),
            Apply = off => SetHibernate(off),
        },
    };

    private static TweakOutcome SetGameDvr(bool off)
    {
        TweakOutcome config = WriteDword(
            RegistryHive.CurrentUser,
            GameConfigKey,
            "GameDVR_Enabled",
            off ? 0 : 1,
            "Game DVR updated.");

        if (!config.Ok) return config;

        TweakOutcome capture = WriteDword(
            RegistryHive.CurrentUser,
            GameDvrKey,
            "AppCaptureEnabled",
            off ? 0 : 1,
            "Game DVR updated.");

        if (!capture.Ok) return capture;

        return new TweakOutcome(
            true,
            off ? "Game DVR and background capture disabled." : "Game DVR capture restored.");
    }

    private static TweakOutcome SetHibernate(bool off)
    {
        CmdResult result = Cmd.Run("powercfg.exe", off ? "/hibernate off" : "/hibernate on", 30000);

        if (result.Ok)
        {
            return new TweakOutcome(
                true,
                off ? "Hibernation disabled; hiberfil.sys released." : "Hibernation enabled.");
        }

        return new TweakOutcome(false, $"powercfg: {result.Reason}");
    }

    private static int ReadDword(RegistryHive hive, string path, string name, int fallback)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using RegistryKey? key = baseKey.OpenSubKey(path);

            return key?.GetValue(name) switch
            {
                int number => number,
                long number => (int)number,
                string text when int.TryParse(text, out int parsed) => parsed,
                _ => fallback,
            };
        }
        catch
        {
            return fallback;
        }
    }

    private static TweakOutcome WriteDword(RegistryHive hive, string path, string name, int value, string success)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using RegistryKey key = baseKey.CreateSubKey(path, writable: true);
            key.SetValue(name, value, RegistryValueKind.DWord);
            return new TweakOutcome(true, success);
        }
        catch (UnauthorizedAccessException)
        {
            return new TweakOutcome(false, "Administrator rights required \u2014 use RESTART AS ADMIN.");
        }
        catch (Exception ex)
        {
            return new TweakOutcome(false, ex.Message);
        }
    }

    private static TweakOutcome RemoveValue(RegistryHive hive, string path, string name, string success)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using RegistryKey? key = baseKey.OpenSubKey(path, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
            return new TweakOutcome(true, success);
        }
        catch (UnauthorizedAccessException)
        {
            return new TweakOutcome(false, "Administrator rights required \u2014 use RESTART AS ADMIN.");
        }
        catch (Exception ex)
        {
            return new TweakOutcome(false, ex.Message);
        }
    }
}
