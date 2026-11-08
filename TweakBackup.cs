using System.Text.Json;
using Microsoft.Win32;

namespace PC_Medic;

/// <summary>One captured value. A null Value means the value did not exist before PC Medic wrote it.</summary>
internal sealed class SavedValue
{
    public string Hive { get; set; } = "HKCU";

    public string Path { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Kind { get; set; }

    public string? Value { get; set; }
}

/// <summary>The originals for one tweak, captured the first time it is applied.</summary>
internal sealed class TweakRecord
{
    public List<SavedValue> Values { get; set; } = new();

    /// <summary>Hibernation is a powercfg switch, so its original is a flag rather than a value.</summary>
    public bool? HibernateEnabled { get; set; }

    public DateTime CapturedAt { get; set; }
}

/// <summary>One journal entry: the switch that flipped and the exact values it replaced.</summary>
internal sealed class TweakChange
{
    public DateTime At { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>True when the switch was turned on; false when it was put back.</summary>
    public bool Enabled { get; set; }

    /// <summary>True when REVERT ALL, rather than a single toggle, made this change.</summary>
    public bool FromRevertAll { get; set; }

    public List<SavedValue> Previous { get; set; } = new();

    public bool? HibernatePrevious { get; set; }

    public string Message { get; set; } = string.Empty;
}

internal sealed class TweakSnapshot
{
    public Dictionary<string, TweakRecord> Tweaks { get; set; } = new();

    /// <summary>Append-only journal of every change, oldest first.</summary>
    public List<TweakChange> History { get; set; } = new();
}

/// <summary>
/// Remembers what every tweak overwrote, so a single revert can put Windows back exactly as it was.
/// The file lives beside the other portable state and is emptied as each tweak is restored.
/// </summary>
internal static class TweakBackup
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string StorePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCMedic",
        "tweaks.json");

    public static int Count => Load().Tweaks.Count;

    /// <summary>Every recorded change, oldest first.</summary>
    public static IReadOnlyList<TweakChange> History => Load().History;

    public static int HistoryCount => Load().History.Count;

    /// <summary>Reads the live values a tweak is about to replace; call this before the change is made.</summary>
    public static TweakChange Prepare(Tweak tweak, bool enabled)
    {
        var change = new TweakChange
        {
            At = DateTime.Now,
            Key = tweak.Key,
            Title = tweak.Title,
            Enabled = enabled,
        };

        foreach (TweakPoint point in tweak.Points)
        {
            (string? value, string? kind) = ReadValue(point);

            change.Previous.Add(new SavedValue
            {
                Hive = point.HiveName,
                Path = point.Path,
                Name = point.Name,
                Kind = kind,
                Value = value,
            });
        }

        if (tweak.HibernationFlag) change.HibernatePrevious = File.Exists(TweakService.HibernateFile);

        return change;
    }

    /// <summary>Appends a successful change to the journal.</summary>
    public static void Record(TweakChange change, TweakOutcome outcome, bool fromRevertAll = false)
    {
        change.Message = outcome.Message;
        change.FromRevertAll = fromRevertAll;

        TweakSnapshot snapshot = Load();
        Append(snapshot, change);
        Save(snapshot);
    }

    /// <summary>Puts the newest journal entry's values back and drops the entry.</summary>
    public static (bool Ok, string Message) UndoLast()
    {
        TweakSnapshot snapshot = Load();
        if (snapshot.History.Count == 0) return (false, "No tweak changes recorded yet.");

        TweakChange change = snapshot.History[^1];
        var failures = new List<string>();

        foreach (SavedValue saved in change.Previous)
        {
            RestoreValue(saved, failures);
        }

        if (change.HibernatePrevious is { } enabled && File.Exists(TweakService.HibernateFile) != enabled)
        {
            if (!Native.IsElevated)
            {
                failures.Add("Hibernation: needs an admin run");
            }
            else
            {
                CmdResult result = Cmd.Run("powercfg.exe", enabled ? "/hibernate on" : "/hibernate off", 30000);
                if (!result.Ok) failures.Add($"Hibernation: {result.Reason}");
            }
        }

        if (failures.Count > 0) return (false, $"{change.Title}: {failures[0]}");

        snapshot.History.RemoveAt(snapshot.History.Count - 1);
        if (!Save(snapshot)) return (false, "Values are back, but the journal could not be written.");

        return (true, $"Undid {change.Title} \u2014 the values from {change.At:HH:mm:ss} are back.");
    }

    private static void Append(TweakSnapshot snapshot, TweakChange change)
    {
        snapshot.History.Add(change);

        const int max = 200;
        if (snapshot.History.Count > max) snapshot.History.RemoveRange(0, snapshot.History.Count - max);
    }

    /// <summary>Captures the originals once; applying the same tweak again keeps the first snapshot.</summary>
    public static bool Capture(Tweak tweak)
    {
        TweakSnapshot snapshot = Load();
        if (snapshot.Tweaks.ContainsKey(tweak.Key)) return true;

        var record = new TweakRecord { CapturedAt = DateTime.Now };

        foreach (TweakPoint point in tweak.Points)
        {
            (string? value, string? kind) = ReadValue(point);

            record.Values.Add(new SavedValue
            {
                Hive = point.HiveName,
                Path = point.Path,
                Name = point.Name,
                Kind = kind,
                Value = value,
            });
        }

        if (tweak.HibernationFlag) record.HibernateEnabled = File.Exists(TweakService.HibernateFile);

        snapshot.Tweaks[tweak.Key] = record;
        return Save(snapshot);
    }

    /// <summary>Puts every captured value back and clears the records it could restore.</summary>
    public static (int Restored, List<string> Failures, string Message) RevertAll(IReadOnlyList<Tweak> tweaks)
    {
        TweakSnapshot snapshot = Load();
        var failures = new List<string>();
        int restored = 0;

        foreach (Tweak tweak in tweaks)
        {
            if (!snapshot.Tweaks.TryGetValue(tweak.Key, out TweakRecord? record) || record is null)
            {
                // No snapshot: only undo what is applied, and only back to the stock default.
                if (!tweak.IsApplied()) continue;

                if (tweak.RequiresAdmin && !Native.IsElevated)
                {
                    failures.Add($"{tweak.Title}: needs an admin run");
                    continue;
                }

                TweakChange unsnapshotted = Prepare(tweak, enabled: false);
                TweakOutcome outcome = tweak.Apply(false);

                if (outcome.Ok)
                {
                    restored++;
                    unsnapshotted.Message = outcome.Message;
                    unsnapshotted.FromRevertAll = true;
                    Append(snapshot, unsnapshotted);
                }
                else
                {
                    failures.Add($"{tweak.Title}: {outcome.Message}");
                }

                continue;
            }

            bool ok = true;
            TweakChange change = Prepare(tweak, enabled: false);

            foreach (SavedValue saved in record.Values)
            {
                ok &= RestoreValue(saved, failures);
            }

            if (record.HibernateEnabled is { } enabled && File.Exists(TweakService.HibernateFile) != enabled)
            {
                if (!Native.IsElevated)
                {
                    failures.Add("Hibernation: needs an admin run");
                    ok = false;
                }
                else
                {
                    CmdResult result = Cmd.Run("powercfg.exe", enabled ? "/hibernate on" : "/hibernate off", 30000);
                    if (!result.Ok)
                    {
                        failures.Add($"Hibernation: {result.Reason}");
                        ok = false;
                    }
                }
            }

            if (!ok) continue;

            restored++;
            change.Message = "Captured original restored.";
            change.FromRevertAll = true;
            Append(snapshot, change);
            snapshot.Tweaks.Remove(tweak.Key);
        }

        if (restored > 0) Save(snapshot);

        string message = restored == 0
            ? "Nothing was reverted."
            : $"Reverted {restored} tweak{(restored == 1 ? string.Empty : "s")}" +
              (failures.Count == 0
                  ? " \u2014 every captured value is back."
                  : $" \u2014 {failures.Count} step{(failures.Count == 1 ? string.Empty : "s")} skipped.");

        return (restored, failures, message);
    }

    private static bool RestoreValue(SavedValue saved, List<string> failures)
    {
        RegistryHive hive = saved.Hive == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;

        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

            if (saved.Value is null)
            {
                using RegistryKey? key = baseKey.OpenSubKey(saved.Path, writable: true);
                key?.DeleteValue(saved.Name, throwOnMissingValue: false);
                return true;
            }

            using RegistryKey created = baseKey.CreateSubKey(saved.Path, writable: true);

            if (saved.Kind == "STRING")
            {
                created.SetValue(saved.Name, saved.Value, RegistryValueKind.String);
                return true;
            }

            created.SetValue(saved.Name, int.Parse(saved.Value), RegistryValueKind.DWord);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            failures.Add($"{saved.Name}: administrator rights required");
        }
        catch (Exception ex)
        {
            failures.Add($"{saved.Name}: {ex.Message}");
        }

        return false;
    }

    private static (string? Value, string? Kind) ReadValue(TweakPoint point)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(point.Hive, RegistryView.Registry64);
            using RegistryKey? key = baseKey.OpenSubKey(point.Path);

            return key?.GetValue(point.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) switch
            {
                int number => (number.ToString(), "DWORD"),
                long number => (number.ToString(), "DWORD"),
                string text => (text, "STRING"),
                _ => (null, null),
            };
        }
        catch
        {
            return (null, null);
        }
    }

    private static TweakSnapshot Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return new TweakSnapshot();
            return JsonSerializer.Deserialize<TweakSnapshot>(File.ReadAllText(StorePath)) ?? new TweakSnapshot();
        }
        catch
        {
            return new TweakSnapshot();
        }
    }

    private static bool Save(TweakSnapshot snapshot)
    {
        try
        {
            string? folder = Path.GetDirectoryName(StorePath);
            if (folder is not null) Directory.CreateDirectory(folder);

            File.WriteAllText(StorePath, JsonSerializer.Serialize(snapshot, JsonOptions));
            return true;
        }
        catch (Exception ex)
        {
            ActivityLog.Error($"Tweak snapshot could not be saved: {ex.Message}");
            return false;
        }
    }
}
