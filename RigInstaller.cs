namespace PC_Medic;

/// <summary>Outcome of one rig install: whether it landed, what to say, and where the executable is.</summary>
internal sealed record RigInstallResult(bool Ok, string Message, string? Path);

/// <summary>
/// Installs a benchmark rig through winget and then proves where it landed. winget is the only
/// download engine the suite uses: it resolves the current build, verifies the hash and runs the
/// vendor's own installer, so nothing here hard-codes a download URL that will rot in a month.
/// The bar is fed from winget's own streamed progress; when winget reports nothing the caller
/// sweeps instead of inventing a number.
/// </summary>
internal static class RigInstaller
{
    public static async Task<RigInstallResult> InstallAsync(
        PortableApp app,
        IProgress<int>? percent = null,
        CancellationToken token = default)
    {
        if (UpdatesService.WingetPath is null)
        {
            return new RigInstallResult(false, "winget is not installed \u2014 get App Installer first", null);
        }

        if (app.WingetId.Length == 0)
        {
            return new RigInstallResult(false, $"{app.Title} has no winget package \u2014 open its install page", null);
        }

        WingetResult run = await Winget.RunAsync(
            new[]
            {
                "install", "--id", app.WingetId, "--exact", "--silent",
                "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
            },
            percent,
            token);

        if (token.IsCancellationRequested) return new RigInstallResult(false, "cancelled", null);

        string? path = RigLocator.Locate(app);
        bool ok = run.Ok || run.Says("Successfully installed");

        if (ok || run.Says("already installed"))
        {
            if (path is null)
            {
                return new RigInstallResult(
                    false,
                    ok ? "installed, but no executable was found" : "winget says it is already installed, but no executable was found",
                    null);
            }

            Remember(app, path);
            percent?.Report(100);

            return new RigInstallResult(
                true,
                (ok ? "installed \u2014 " : "already installed \u2014 ") + Theme.Truncate(path, 64),
                path);
        }

        return new RigInstallResult(false, run.Reason, path);
    }

    /// <summary>The rig is now remembered by path, so the grid reads it as present on the next refresh.</summary>
    private static void Remember(PortableApp app, string path)
    {
        (bool ok, string message) = Tools.SetCustomPath(app, path);
        if (!ok) ActivityLog.Warn($"{app.Title}: found at {path}, but the path could not be remembered ({message}).");
    }
}

/// <summary>
/// Finds a rig that winget installed outside the portable folder. winget packages extract under
/// its own Packages directory, installers land in Program Files, and the uninstall registry records
/// the folder: each is checked before giving up, and archive packages fall back to a bounded walk.
/// </summary>
internal static class RigLocator
{
    private const int PackageDepth = 6;
    private const int InstallDepth = 4;

    public static string? Locate(PortableApp app)
    {
        string? portable = Tools.ResolvePortable(app);
        if (portable is not null) return portable;

        string packages = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WinGet",
            "Packages");

        string? hit = Search(packages, app.ExeName, PackageDepth);
        if (hit is not null) return hit;

        foreach (ProgramEntry entry in Registered(app))
        {
            if (entry.InstallLocation.Length == 0) continue;

            hit = Search(entry.InstallLocation, app.ExeName, InstallDepth);
            if (hit is not null) return hit;
        }

        foreach (string root in InstallRoots())
        {
            hit = Search(root, app.ExeName, InstallDepth);
            if (hit is not null) return hit;
        }

        return null;
    }

    private static string[] InstallRoots()
    {
        var roots = new List<string>(4);

        foreach (Environment.SpecialFolder folder in new[]
                 {
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86,
                 })
        {
            string path = Environment.GetFolderPath(folder);
            if (path.Length > 0) roots.Add(path);
        }

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (local.Length > 0) roots.Add(Path.Combine(local, "Programs"));

        return roots.ToArray();
    }

    private static List<ProgramEntry> Registered(PortableApp app)
    {
        try
        {
            return ProgramsService.Load()
                .Where(entry => !entry.Dead &&
                                entry.DisplayName.Contains(app.InstallName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex)
        {
            ActivityLog.Warn($"Installed rig lookup failed: {ex.Message}");
            return new List<ProgramEntry>();
        }
    }

    /// <summary>Depth-limited, access-tolerant walk: a locked folder never stops the search.</summary>
    private static string? Search(string root, string fileName, int maxDepth)
    {
        if (string.IsNullOrWhiteSpace(root) || fileName.Length == 0) return null;

        try
        {
            if (!Directory.Exists(root)) return null;

            var pending = new Stack<(string Folder, int Depth)>();
            pending.Push((root, 0));

            int visited = 0;

            while (pending.Count > 0 && visited++ < 3000)
            {
                (string folder, int depth) = pending.Pop();

                try
                {
                    string direct = Path.Combine(folder, fileName);
                    if (File.Exists(direct)) return direct;
                }
                catch
                {
                    // Malformed recorded path; keep walking.
                }

                if (depth >= maxDepth) continue;

                try
                {
                    foreach (string child in Directory.EnumerateDirectories(folder)) pending.Push((child, depth + 1));
                }
                catch
                {
                    // Access denied on this branch only.
                }
            }
        }
        catch
        {
            // Root unreadable; no hit.
        }

        return null;
    }
}
