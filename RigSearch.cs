namespace PC_Medic;

/// <summary>One rig the search proved is on disk, with the executable it matched.</summary>
internal sealed record RigSearchHit(PortableApp App, string Path);

/// <summary>
/// Finds rigs that live somewhere other than the system drive. The uninstall registry and winget's
/// own package folder are read first, wherever they point, then every ready volume is walked once
/// for all the missing executables at the same time. Nothing is installed and nothing is written
/// except the remembered paths, so a rig already sitting on D: needs no download at all.
/// </summary>
internal static class RigSearch
{
    /// <summary>Deep enough for a library install, shallow enough that a walk stays quick.</summary>
    private const int MaxDepth = 6;

    /// <summary>Per-volume cap: a walk that reaches it stops instead of grinding for minutes.</summary>
    private const int FolderBudget = 150_000;

    // ACL-locked or guaranteed rig-free trees, all of them expensive to walk.
    private static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "$recycle.bin",
        "system volume information",
        "windows",
        "winsxs",
        "windowsapps",
        "recovery",
        "perflogs",
        "config.msi",
        "node_modules",
        ".git",
    };

    /// <summary>
    /// Searches for every rig in the list and remembers each hit by path, so the grid reads it as
    /// present. Progress is reported per target folder, per folder count, and per hit as it lands.
    /// </summary>
    public static async Task<IReadOnlyList<RigSearchHit>> RunAsync(
        IReadOnlyList<PortableApp> apps,
        IProgress<string>? stage = null,
        IProgress<int>? folders = null,
        IProgress<RigSearchHit>? hit = null,
        CancellationToken token = default)
    {
        var found = new List<RigSearchHit>();
        var missing = new List<PortableApp>();

        foreach (PortableApp app in apps)
        {
            if (Tools.ResolvePortable(app) is { } known) found.Add(new RigSearchHit(app, known));
            else missing.Add(app);
        }

        if (missing.Count == 0) return found;

        IReadOnlyList<RigSearchHit> hits = await Task.Run(() => Scan(missing, stage, folders, hit, token));

        foreach (RigSearchHit result in hits) Remember(result);

        return hits;
    }

    private static List<RigSearchHit> Scan(
        List<PortableApp> missing,
        IProgress<string>? stage,
        IProgress<int>? folders,
        IProgress<RigSearchHit>? hit,
        CancellationToken token)
    {
        var hits = new List<RigSearchHit>();

        // Cheap answers first: the registry and winget already know where their packages went.
        stage?.Report("REGISTRY AND WINGET");

        foreach (PortableApp app in missing.ToList())
        {
            if (token.IsCancellationRequested) return hits;
            if (RigLocator.Locate(app) is not { } located) continue;

            var result = new RigSearchHit(app, located);
            hits.Add(result);
            missing.Remove(app);
            hit?.Report(result);
        }

        if (missing.Count == 0) return hits;

        List<string> names = missing.Select(app => app.ExeName).ToList();

        foreach (string root in ReadyVolumes())
        {
            if (names.Count == 0 || token.IsCancellationRequested) break;

            stage?.Report(root);

            // Breadth-first: every shallow folder is checked before any deep one, so a rig installed
            // a couple of levels down is found in seconds even on a slow, large volume.
            var pending = new Queue<(string Folder, int Depth)>();
            pending.Enqueue((root, 0));

            int scanned = 0;

            while (pending.Count > 0 && scanned < FolderBudget && !token.IsCancellationRequested)
            {
                (string folder, int depth) = pending.Dequeue();

                if (++scanned % 128 == 0) folders?.Report(scanned);

                // Every rig still missing is checked in this folder, so one walk answers all of them.
                for (int i = names.Count - 1; i >= 0; i--)
                {
                    string candidate = Path.Combine(folder, names[i]);
                    if (!File.Exists(candidate)) continue;

                    var result = new RigSearchHit(missing[i], candidate);
                    hits.Add(result);
                    hit?.Report(result);
                    missing.RemoveAt(i);
                    names.RemoveAt(i);
                }

                if (names.Count == 0) break;
                if (depth >= MaxDepth) continue;

                try
                {
                    foreach (string child in Directory.EnumerateDirectories(folder))
                    {
                        if (!SkipFolders.Contains(Path.GetFileName(child))) pending.Enqueue((child, depth + 1));
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
                {
                    // One unreadable folder never ends the walk.
                }
            }

            folders?.Report(scanned);
        }

        return hits;
    }

    /// <summary>
    /// Every ready local volume, the ones that are not the system drive first: rigs are usually
    /// installed off C:, and the system volume is the slowest and least likely to hold them.
    /// </summary>
    private static List<string> ReadyVolumes()
    {
        var volumes = new List<string>(4);
        string system = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

        try
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady) continue;
                    if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                    volumes.Add(drive.RootDirectory.FullName);
                }
                catch
                {
                    // A volume that vanished mid-enumeration is simply not searched.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ActivityLog.Warn($"Drive list unavailable: {ex.Message}");
        }

        return volumes
            .OrderBy(volume => string.Equals(volume, system, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(volume => volume, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The hit is stored by path, so the grid reads it as present on the next refresh.</summary>
    private static void Remember(RigSearchHit hit)
    {
        (bool ok, string message) = Tools.SetCustomPath(hit.App, hit.Path);

        if (!ok)
        {
            ActivityLog.Warn(
                $"{hit.App.Title}: found at {hit.Path}, but the path could not be remembered ({message}).");
        }
    }
}
