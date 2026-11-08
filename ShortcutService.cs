using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace PC_Medic;

internal enum ShortcutStatus
{
    Valid,
    Broken,
    Unverified,
}

internal sealed record ShortcutEntry(
    string FilePath,
    string Name,
    string Area,
    string Target,
    ShortcutStatus Status,
    string Detail);

internal readonly record struct ShortcutScan(IReadOnlyList<ShortcutEntry> Entries, int Scanned, int Healthy, int Suppressed);

/// <summary>
/// Broken-shortcut audit. Every .lnk under the desktop, Start Menu and Startup folders is resolved
/// with IShellLinkW and checked against the filesystem; removal goes through the Recycle Bin so a
/// mistake can be undone from Windows itself.
/// </summary>
internal static class ShortcutService
{
    private const uint StgmRead = 0;

    private const uint DeleteOperation = 0x0003;
    private const ushort AllowUndo = 0x0040;
    private const ushort NoConfirmation = 0x0010;
    private const ushort Silent = 0x0004;
    private const ushort NoErrorUi = 0x0400;

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file,
            int maxCharacters,
            IntPtr findData,
            uint flags);
    }

    [ComImport]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);

        [PreserveSig]
        int IsDirty();

        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);

        void Save([MarshalAs(UnmanagedType.LPWStr)] string? fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);

        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);

        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr Window;
        public uint Function;
        [MarshalAs(UnmanagedType.LPWStr)] public string From;
        [MarshalAs(UnmanagedType.LPWStr)] public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Aborted;
        public IntPtr NameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref ShFileOpStruct operation);

    public static ShortcutScan Scan()
    {
        var problems = new List<ShortcutEntry>();
        int scanned = 0;
        int healthy = 0;

        HashSet<string> kept = LoadKept();
        int suppressed = 0;

        foreach ((string area, List<string> folders) in Roots())
        {
            foreach (string folder in folders)
            {
                foreach (string file in Enumerate(folder))
                {
                    scanned++;

                    ShortcutEntry entry = Resolve(file, area);
                    if (entry.Status == ShortcutStatus.Valid)
                    {
                        healthy++;
                        continue;
                    }

                    if (kept.Contains(Key(entry)))
                    {
                        suppressed++;
                        continue;
                    }

                    problems.Add(entry);
                }
            }
        }

        return new ShortcutScan(problems, scanned, healthy, suppressed);
    }

    /// <summary>
    /// Runs the audit on a dedicated STA thread. IShellLinkW is apartment-threaded, so resolving tens
    /// of thousands of links from a thread-pool thread stalls instead of finishing.
    /// </summary>
    public static Task<ShortcutScan> ScanAsync()
    {
        var completion = new TaskCompletionSource<ShortcutScan>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(Scan());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "PC Medic shortcut audit",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return completion.Task;
    }

    /// <summary>Sends the ticked shortcuts to the Recycle Bin; nothing is deleted outright.</summary>
    public static (bool Ok, string Message) Recycle(IReadOnlyList<ShortcutEntry> entries)
    {
        if (entries.Count == 0) return (false, "Nothing is ticked.");

        int moved = 0;
        var failed = new List<string>();

        foreach (ShortcutEntry entry in entries)
        {
            if (!File.Exists(entry.FilePath))
            {
                moved++;
                continue;
            }

            if (SendToRecycleBin(entry.FilePath)) moved++;
            else failed.Add(entry.Name);
        }

        if (failed.Count == 0) return (true, $"{moved} shortcuts moved to the Recycle Bin.");
        return (moved > 0, $"{moved} moved to the Recycle Bin; {failed.Count} could not be recycled: {Theme.Truncate(string.Join(", ", failed), 110)}");
    }

    /// <summary>
    /// Remembers inspected shortcuts as fine, so later audits stop reporting them. The key includes the
    /// target: a link that is later retargeted at something missing comes back for another look.
    /// </summary>
    public static (bool Ok, string Message) Keep(IReadOnlyList<ShortcutEntry> entries)
    {
        if (entries.Count == 0) return (false, "Nothing is ticked.");

        HashSet<string> kept = LoadKept();
        int added = entries.Count(entry => kept.Add(Key(entry)));

        if (!SaveKept(kept)) return (false, "The keep-list could not be written.");
        return (true, added == 1 ? "1 shortcut marked as not dead." : $"{added} shortcuts marked as not dead.");
    }

    private static readonly string KeepFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCMedic",
        "shortcut-keep.json");

    private static string Key(ShortcutEntry entry) =>
        $"{entry.FilePath.ToLowerInvariant()}|{entry.Target.ToLowerInvariant()}";

    private static HashSet<string> LoadKept()
    {
        try
        {
            if (!File.Exists(KeepFile)) return new HashSet<string>(StringComparer.Ordinal);
            string[]? keys = JsonSerializer.Deserialize<string[]>(File.ReadAllText(KeepFile));
            return new HashSet<string>(keys ?? Array.Empty<string>(), StringComparer.Ordinal);
        }
        catch
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static bool SaveKept(HashSet<string> keys)
    {
        try
        {
            string? folder = Path.GetDirectoryName(KeepFile);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            File.WriteAllText(
                KeepFile,
                JsonSerializer.Serialize(
                    keys.OrderBy(key => key, StringComparer.Ordinal).ToArray(),
                    new JsonSerializerOptions { WriteIndented = true }));

            return true;
        }
        catch (Exception ex)
        {
            ActivityLog.Warn($"Could not save the shortcut keep-list: {ex.Message}");
            return false;
        }
    }

    private static ShortcutEntry Resolve(string file, string area)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        object? link = null;

        try
        {
            link = new ShellLink();
            var shellLink = (IShellLinkW)link;
            ((IPersistFile)link).Load(file, StgmRead);

            var buffer = new StringBuilder(1024);
            shellLink.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
            string target = Environment.ExpandEnvironmentVariables(buffer.ToString().Trim());

            if (string.IsNullOrWhiteSpace(target))
            {
                return new ShortcutEntry(file, name, area, string.Empty, ShortcutStatus.Unverified,
                    "resolves to no path (advertised or wrapped shortcut)");
            }

            bool exists = File.Exists(target) || Directory.Exists(target);
            return new ShortcutEntry(
                file,
                name,
                area,
                target,
                exists ? ShortcutStatus.Valid : ShortcutStatus.Broken,
                exists ? string.Empty : "target is missing");
        }
        catch (Exception ex)
        {
            return new ShortcutEntry(file, name, area, string.Empty, ShortcutStatus.Unverified, ex.Message);
        }
        finally
        {
            if (link is not null)
            {
                try { Marshal.ReleaseComObject(link); } catch { /* COM already released */ }
            }
        }
    }

    private static List<(string Area, List<string> Folders)> Roots() => new()
    {
        ("DESKTOP", new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        }),
        ("START MENU", new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        }),
        ("STARTUP", new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
        }),
    };

    /// <summary>Walks a folder tree defensively: one locked directory must not abort the audit.</summary>
    private static IEnumerable<string> Enumerate(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) yield break;

        var pending = new Stack<string>();
        pending.Push(folder);

        while (pending.Count > 0)
        {
            string current = pending.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(current, "*.lnk");
            }
            catch
            {
                continue;
            }

            foreach (string file in files) yield return file;

            string[] children;
            try
            {
                children = Directory.GetDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (string child in children) pending.Push(child);
        }
    }

    private static bool SendToRecycleBin(string path)
    {
        try
        {
            var operation = new ShFileOpStruct
            {
                Function = DeleteOperation,
                // SHFileOperation wants a double-null-terminated list; the marshaler appends the last null.
                From = path + "\0",
                Flags = AllowUndo | NoConfirmation | Silent | NoErrorUi,
            };

            return SHFileOperationW(ref operation) == 0 && !operation.Aborted;
        }
        catch
        {
            return false;
        }
    }
}
