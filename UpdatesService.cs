using System.Text.RegularExpressions;

namespace PC_Medic;

/// <summary>One package winget can move to a newer version.</summary>
internal sealed record AppUpgrade(string Name, string Id, string Current, string Available, string Source)
{
    public string VersionLine => $"{Current} \u2192 {Available}";
}

/// <summary>A driver update offered by the Windows Update agent.</summary>
internal sealed record DriverUpdate(string Title, string Manufacturer, DateTime? Released, long SizeBytes, object Payload)
{
    public string DetailLine
    {
        get
        {
            string date = Released is { } released ? released.ToString("yyyy-MM-dd") : "unknown date";
            ulong size = SizeBytes > 0 ? (ulong)SizeBytes : 0;
            return $"{Theme.Truncate(Manufacturer, 24)} \u2022 {date} \u2022 {Theme.Bytes(size)}";
        }
    }
}

/// <summary>
/// Two read/write surfaces for keeping a machine current, using what Windows already ships:
/// winget for applications and the Windows Update agent (COM) for drivers. Nothing is
/// downloaded from a third-party server and no package is added to the project.
/// </summary>
internal static class UpdatesService
{
    public const string WingetDownloadPage = "https://aka.ms/getwinget";

    private const int OperationSucceeded = 2;

    /// <summary>winget.exe from the WindowsApps alias, or the first match on PATH.</summary>
    public static string? WingetPath
    {
        get
        {
            string alias = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\WindowsApps\winget.exe");

            try
            {
                if (File.Exists(alias)) return alias;
            }
            catch
            {
                // Unreadable profile path; fall through to PATH.
            }

            string? path = Environment.GetEnvironmentVariable("PATH");
            if (path is null) return null;

            foreach (string folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string candidate = Path.Combine(folder.Trim(), "winget.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                    // Malformed PATH entry; keep looking.
                }
            }

            return null;
        }
    }

    public static (List<AppUpgrade> Apps, string? Error) ScanApps()
    {
        string? winget = WingetPath;
        if (winget is null) return (new List<AppUpgrade>(), "winget is not installed.");

        CmdResult result = Cmd.Run(
            winget,
            "upgrade --include-unknown --accept-source-agreements --disable-interactivity",
            180000);

        List<AppUpgrade> apps = ParseUpgradeTable(result.Output + "\n" + result.Error);
        if (apps.Count > 0) return (apps, null);

        string text = result.AllText;
        bool nothingToDo = text.Contains("No installed package found", StringComparison.OrdinalIgnoreCase)
                           || text.Contains("No applicable update found", StringComparison.OrdinalIgnoreCase)
                           || text.Contains("No updates available", StringComparison.OrdinalIgnoreCase);

        if (nothingToDo) return (apps, null);

        return (apps, result.Ok ? null : result.Reason);
    }

    /// <summary>
    /// Streams winget's upgrade so the page can fill that row's own bar from the transfer it is
    /// actually doing, instead of parking a spinner for the whole pass.
    /// </summary>
    public static async Task<(bool Ok, string Message)> UpgradeAsync(
        AppUpgrade app,
        IProgress<int>? percent = null,
        CancellationToken token = default)
    {
        if (WingetPath is null) return (false, "winget is not installed.");

        WingetResult run = await Winget.RunAsync(
            new[]
            {
                "upgrade", "--id", app.Id, "--exact", "--silent", "--include-unknown",
                "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
            },
            percent,
            token);

        return run.Ok
            ? (true, $"{app.Name} upgraded to {app.Available}.")
            : (false, $"{app.Name}: {run.Reason}");
    }

    /// <summary>Removes a package for good, for apps that stopped being wanted rather than out of date.</summary>
    public static (bool Ok, string Message) Uninstall(AppUpgrade app)
    {
        string? winget = WingetPath;
        if (winget is null) return (false, "winget is not installed.");

        CmdResult result = Cmd.Run(
            winget,
            $"uninstall --id \"{app.Id}\" --exact --silent " +
            "--accept-source-agreements --disable-interactivity",
            600000);

        return result.Ok
            ? (true, $"{app.Name} uninstalled.")
            : (false, $"{app.Name}: {result.Reason}");
    }

    /// <summary>Reads the driver updates the Windows Update agent is offering. Read-only.</summary>
    public static (List<DriverUpdate> Drivers, string? Error) ScanDrivers()
    {
        var drivers = new List<DriverUpdate>();

        try
        {
            Type? sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (sessionType is null) return (drivers, "The Windows Update agent is unavailable.");

            dynamic session = Activator.CreateInstance(sessionType)!;
            dynamic searcher = session.CreateUpdateSearcher();
            searcher.Online = true;

            dynamic result = searcher.Search("IsInstalled=0 and Type='Driver' and IsHidden=0");
            int count = (int)result.Updates.Count;

            for (int index = 0; index < count; index++)
            {
                try
                {
                    dynamic update = result.Updates.Item(index);

                    string title = (string)update.Title;
                    string manufacturer = "Unknown";
                    DateTime? released = null;
                    long size = 0;

                    try
                    {
                        string? name = (string?)update.DriverManufacturer;
                        if (!string.IsNullOrWhiteSpace(name)) manufacturer = name;
                    }
                    catch
                    {
                        // Some updates carry no manufacturer property.
                    }

                    try
                    {
                        released = (DateTime)update.DriverVerDate;
                    }
                    catch
                    {
                        // Optional metadata; leave the date blank.
                    }

                    try
                    {
                        size = Convert.ToInt64(update.MaxDownloadSize);
                    }
                    catch
                    {
                        // Size is cosmetic.
                    }

                    drivers.Add(new DriverUpdate(title, manufacturer, released, size, (object)update));
                }
                catch
                {
                    // Skip updates whose metadata cannot be read at all.
                }
            }

            return (drivers.OrderBy(driver => driver.Title, StringComparer.OrdinalIgnoreCase).ToList(), null);
        }
        catch (Exception ex)
        {
            return (drivers, ex.Message);
        }
    }

    /// <summary>
    /// Downloads and installs the given drivers one at a time rather than as one batch: a batch hands
    /// the whole transfer to Windows Update and cannot be stopped, while stepping through the list lets
    /// the page cancel between drivers and mark each row as it goes.
    /// </summary>
    public static (bool Ok, string Message) Install(
        IReadOnlyList<DriverUpdate> drivers,
        CancellationToken token = default,
        IProgress<(int Index, int Total)>? progress = null)
    {
        if (drivers.Count == 0) return (false, "Nothing to install.");
        if (!Native.IsElevated) return (false, "Driver installs need an elevated run \u2014 use RESTART AS ADMIN.");

        try
        {
            Type? sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
            Type? collectionType = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl");
            if (sessionType is null || collectionType is null) return (false, "The Windows Update agent is unavailable.");

            dynamic session = Activator.CreateInstance(sessionType)!;

            int installed = 0;
            bool reboot = false;
            string? failure = null;

            for (int index = 0; index < drivers.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report((index, drivers.Count));

                DriverUpdate driver = drivers[index];
                dynamic update = driver.Payload;
                dynamic collection = Activator.CreateInstance(collectionType)!;

                try
                {
                    if (!(bool)update.EulaAccepted) update.AcceptEula();
                }
                catch
                {
                    // Not every driver update exposes a EULA flag.
                }

                collection.Add(update);

                dynamic downloader = session.CreateUpdateDownloader();
                downloader.Updates = collection;
                dynamic download = downloader.Download();

                int downloadCode = (int)download.ResultCode;
                if (downloadCode != OperationSucceeded)
                {
                    failure ??= $"{driver.Title}: download failed with code {downloadCode}.";
                    continue;
                }

                token.ThrowIfCancellationRequested();

                dynamic installer = session.CreateUpdateInstaller();
                installer.Updates = collection;
                dynamic install = installer.Install();

                int installCode = (int)install.ResultCode;
                if (installCode != OperationSucceeded)
                {
                    failure ??= $"{driver.Title}: install finished with code {installCode}.";
                    continue;
                }

                installed++;

                try
                {
                    reboot |= (bool)install.RebootRequired;
                }
                catch
                {
                    // Cosmetic.
                }
            }

            if (installed == 0 && failure is not null) return (false, failure);

            string message = $"{installed} driver update(s) installed" + (reboot ? " \u2014 reboot required." : ".");
            if (failure is not null) message += " " + failure;

            return (failure is null, message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Reads the table winget prints, slicing columns from the header offsets.</summary>
    public static List<AppUpgrade> ParseUpgradeTable(string output)
    {
        var apps = new List<AppUpgrade>();
        string[] lines = StripAnsi(output).Replace("\r", string.Empty).Split('\n');

        int headerIndex = -1;

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            if (line.Contains("Name", StringComparison.Ordinal)
                && line.Contains("Id", StringComparison.Ordinal)
                && line.Contains("Available", StringComparison.Ordinal)
                && !IsSeparator(line))
            {
                headerIndex = index;
                break;
            }
        }

        if (headerIndex < 0) return apps;

        string header = lines[headerIndex];
        int nameColumn = header.IndexOf("Name", StringComparison.Ordinal);
        int idColumn = header.IndexOf("Id", StringComparison.Ordinal);
        int versionColumn = header.IndexOf("Version", StringComparison.Ordinal);
        int availableColumn = header.IndexOf("Available", StringComparison.Ordinal);
        int sourceColumn = header.IndexOf("Source", StringComparison.Ordinal);

        if (nameColumn < 0 || idColumn <= nameColumn || availableColumn <= idColumn) return apps;
        if (versionColumn <= idColumn) versionColumn = availableColumn;

        for (int index = headerIndex + 1; index < lines.Length; index++)
        {
            string line = lines[index];
            if (line.Trim().Length == 0 || IsSeparator(line) || line.Length <= idColumn) continue;

            string name = Slice(line, nameColumn, idColumn).Trim();
            string id = Slice(line, idColumn, versionColumn).Trim();
            string current = Slice(line, versionColumn, availableColumn).Trim();
            string available = sourceColumn > availableColumn
                ? Slice(line, availableColumn, sourceColumn).Trim()
                : Slice(line, availableColumn, int.MaxValue).Trim();
            string source = sourceColumn > availableColumn
                ? Slice(line, sourceColumn, int.MaxValue).Trim()
                : string.Empty;

            if (!LooksLikePackageId(id)) continue;

            apps.Add(new AppUpgrade(name, id, CleanVersion(current), CleanVersion(available), source));
        }

        return apps;
    }

    private static string Slice(string line, int start, int end)
    {
        if (start < 0 || start >= line.Length) return string.Empty;

        int length = end == int.MaxValue ? line.Length - start : Math.Min(end, line.Length) - start;
        return length <= 0 ? string.Empty : line.Substring(start, length);
    }

    /// <summary>Package IDs are dotted (Git.Git) or alphanumeric Store IDs (9WZDNCRFJ3PZ).</summary>
    private static bool LooksLikePackageId(string id)
    {
        if (id.Length < 3 || id.Any(char.IsWhiteSpace)) return false;

        bool dashesOnly = id.All(character => character is '-' or '\u2013' or '\u2014' or '\u2500' or '\u2501');
        return !dashesOnly;
    }

    private static string CleanVersion(string value)
    {
        string text = value.Trim();
        if (text.Length == 0) return "?";

        bool dashesOnly = text.All(character => character is '-' or '\u2013' or '\u2014' or '\u2500' or '\u2501');
        return dashesOnly ? "?" : text;
    }

    private static bool IsSeparator(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length < 4) return false;

        foreach (char character in trimmed)
        {
            if (character is not ('-' or '\u2013' or '\u2014' or '\u2500' or '\u2501')) return false;
        }

        return true;
    }

    private static string StripAnsi(string text)
    {
        if (!text.Contains('\u001b')) return text;
        return Regex.Replace(text, "\u001b\\[[0-9;]*[A-Za-z]", string.Empty);
    }
}
