using AutoUpdaterDotNET;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PC_Medic;

/// <summary>What the release feed said about the running build.</summary>
internal sealed record SelfUpdateInfo(
    bool Checked,
    bool Available,
    string CurrentVersion,
    string LatestVersion,
    string? AssetUrl,
    string? AssetName,
    string Notes,
    string Message);

/// <summary>
/// Self-update against GitHub releases: read the latest tag, compare it with the running
/// build, download the attached package and hand the swap to a script that waits for exit.
/// </summary>
internal static class UpdateService
{
    public const string Repository = "DwainSnickles/PC-Medic";

    private static readonly HttpClient Client = CreateClient();

    public static string ReleasesPage => $"https://github.com/{Repository}/releases";

    public static string CurrentVersion => Normalise(Application.ProductVersion);

    /// <summary>
    /// Unlisted AutoUpdater.NET manifest on the owner's site. Set this to the update.xml URL and the
    /// check switches from the GitHub API to that file, which the owner edits to publish a version.
    /// </summary>
    public const string DefaultManifestUrl = "";

    /// <summary>The manifest URL, or PCMEDIC_UPDATE_FEED when that is set.</summary>
    public static string ManifestUrl =>
        Environment.GetEnvironmentVariable("PCMEDIC_UPDATE_FEED") is { Length: > 0 } custom
            ? custom
            : DefaultManifestUrl;

    /// <summary>An .xml feed is read by AutoUpdater.NET; anything else is treated as a GitHub-style JSON feed.</summary>
    public static bool UsesManifest =>
        ManifestUrl.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    /// <summary>GitHub by default; override with PCMEDIC_UPDATE_FEED for a custom feed.</summary>
    public static string FeedUrl =>
        UsesManifest
            ? ManifestUrl
            : Environment.GetEnvironmentVariable("PCMEDIC_UPDATE_FEED") is { Length: > 0 } custom
                ? custom
                : $"https://api.github.com/repos/{Repository}/releases/latest";

    public static string FeedHost
    {
        get
        {
            try
            {
                return new Uri(FeedUrl).Host;
            }
            catch
            {
                return "the update feed";
            }
        }
    }

    public static string DownloadFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCMedic",
        "updates");

    public static async Task<SelfUpdateInfo> CheckAsync()
    {
        string current = CurrentVersion;

        return UsesManifest
            ? await CheckManifestAsync(current).ConfigureAwait(false)
            : await CheckGitHubAsync(current).ConfigureAwait(false);
    }

    /// <summary>
    /// Lets AutoUpdater.NET fetch and parse the manifest, but keeps PC Medic's own dialog: subscribing to
    /// CheckForUpdateEvent suppresses its stock update form, and our installer still performs the swap.
    /// </summary>
    private static async Task<SelfUpdateInfo> CheckManifestAsync(string current)
    {
        var completion = new TaskCompletionSource<SelfUpdateInfo>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnChecked(UpdateInfoEventArgs args) => completion.TrySetResult(MapManifest(args, current));

        AutoUpdater.CheckForUpdateEvent += OnChecked;

        try
        {
            AutoUpdater.ReportErrors = false;
            AutoUpdater.InstalledVersion = ParseVersion(current);
            AutoUpdater.Start(ManifestUrl);
        }
        catch (Exception ex)
        {
            AutoUpdater.CheckForUpdateEvent -= OnChecked;
            return new SelfUpdateInfo(false, false, current, current, null, null, string.Empty,
                $"Update check failed: {ex.Message}");
        }

        try
        {
            Task finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);

            return finished == completion.Task
                ? await completion.Task.ConfigureAwait(false)
                : new SelfUpdateInfo(false, false, current, current, null, null, string.Empty,
                    "Update check timed out \u2014 the manifest did not answer.");
        }
        finally
        {
            AutoUpdater.CheckForUpdateEvent -= OnChecked;
        }
    }

    private static SelfUpdateInfo MapManifest(UpdateInfoEventArgs args, string current)
    {
        if (args.Error is not null)
        {
            return new SelfUpdateInfo(false, false, current, current, null, null, string.Empty,
                $"Update check failed: {args.Error.Message}");
        }

        string latest = args.CurrentVersion?.ToString() ?? current;
        string? url = string.IsNullOrWhiteSpace(args.DownloadURL) ? null : args.DownloadURL.Trim();
        string notes = string.IsNullOrWhiteSpace(args.ChangelogURL) ? string.Empty : $"Changelog: {args.ChangelogURL}";
        bool available = args.IsUpdateAvailable && url is not null;

        string message = url is null
            ? "The update manifest lists no download URL."
            : available
                ? $"Version {Normalise(latest)} is available."
                : $"PC Medic is up to date ({current}).";

        return new SelfUpdateInfo(
            true,
            available,
            current,
            Normalise(latest),
            url,
            url is null ? null : SafeName(url),
            notes,
            message);
    }

    private static Version ParseVersion(string text)
        => Version.TryParse(Normalise(text), out Version? parsed) ? parsed : new Version(0, 0, 0, 0);

    private static async Task<SelfUpdateInfo> CheckGitHubAsync(string current)
    {
        try
        {
            using HttpResponseMessage response = await Client.GetAsync(FeedUrl).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new SelfUpdateInfo(
                    true,
                    false,
                    current,
                    current,
                    null,
                    null,
                    string.Empty,
                    "No published release was found \u2014 tag a build on GitHub first.");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden &&
                response.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string>? remaining) &&
                remaining.FirstOrDefault() == "0")
            {
                return new SelfUpdateInfo(true, false, current, current, null, null, string.Empty,
                    "GitHub is rate limiting the check \u2014 try again later.");
            }

            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return Parse(json, current);
        }
        catch (Exception ex)
        {
            return new SelfUpdateInfo(false, false, current, current, null, null, string.Empty,
                $"Update check failed: {ex.Message}");
        }
    }

    /// <summary>Accepts either a GitHub release payload or a plain manifest.</summary>
    internal static SelfUpdateInfo Parse(string json, string current)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        if (root.TryGetProperty("version", out JsonElement version))
        {
            string latest = Normalise(version.GetString());
            string? url = root.TryGetProperty("url", out JsonElement urlElement) ? urlElement.GetString() : null;
            string notes = root.TryGetProperty("notes", out JsonElement notesElement) ? notesElement.GetString() ?? string.Empty : string.Empty;
            return Describe(current, latest, url, url is null ? null : SafeName(url), notes);
        }

        string tag = root.TryGetProperty("tag_name", out JsonElement tagElement) ? tagElement.GetString() ?? string.Empty : string.Empty;
        string body = root.TryGetProperty("body", out JsonElement bodyElement) ? bodyElement.GetString() ?? string.Empty : string.Empty;
        string? assetUrl = null;
        string? assetName = null;
        int bestRank = int.MaxValue;

        if (root.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
        {
            // Assets come back alphabetically, so "first zip" is the companion tool. Rank them instead:
            // the app package wins over the suite zip, which wins over anything else attached.
            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
                string url = asset.TryGetProperty("browser_download_url", out JsonElement urlElement) ? urlElement.GetString() ?? string.Empty : string.Empty;

                if (name.Length == 0 || url.Length == 0) continue;
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                    !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                int rank = AssetRank(name);
                if (rank >= bestRank) continue;

                bestRank = rank;
                assetName = name;
                assetUrl = url;
            }
        }

        return Describe(current, Normalise(tag), assetUrl, assetName, body);
    }

    /// <summary>Lower wins: the single-app zip, then a bare exe, then the suite zip, then any other package.</summary>
    private static int AssetRank(string name)
    {
        string key = name.ToLowerInvariant().Replace("-", string.Empty).Replace(" ", string.Empty);
        bool zip = key.EndsWith(".zip", StringComparison.Ordinal);
        bool ours = key.Contains("pcmedic");

        if (zip && ours && !key.Contains("suite")) return 0;
        if (!zip && ours) return 1;
        if (zip && ours) return 2;
        if (zip) return 3;
        return 4;
    }

    private static SelfUpdateInfo Describe(string current, string latest, string? url, string? name, string notes)
    {
        bool available = url is not null && IsNewer(current, latest);

        string message = url is null
            ? "The latest release has no installable package attached."
            : available
                ? $"Version {latest} is available."
                : $"PC Medic is up to date ({current}).";

        return new SelfUpdateInfo(true, available, current, latest, url, name, notes, message);
    }

    public static async Task<(bool Ok, string Path, string Message)> DownloadAsync(
        SelfUpdateInfo info,
        IProgress<string>? progress = null)
    {
        if (info.AssetUrl is null || info.AssetName is null) return (false, string.Empty, "No package to download.");

        try
        {
            Directory.CreateDirectory(DownloadFolder);
            string target = Path.Combine(DownloadFolder, Path.GetFileName(info.AssetName));
            progress?.Report("connecting\u2026");

            using HttpResponseMessage response = await Client
                .GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;
            await using Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            await using FileStream destination = File.Create(target);

            byte[] buffer = new byte[81920];
            long copied = 0;
            int read;

            while ((read = await source.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                copied += read;

                progress?.Report(total is > 0
                    ? $"{copied * 100 / total.Value}% of {total.Value / 1048576.0:0.0} MB"
                    : $"{copied / 1048576.0:0.0} MB");
            }

            return (true, target, Path.GetFileName(target));
        }
        catch (Exception ex)
        {
            return (false, string.Empty, ex.Message);
        }
    }

    /// <summary>
    /// Starts the swap script and returns; the caller must exit so the executable unlocks.
    /// </summary>
    public static (bool Ok, string Message) Install(string archivePath, bool restart = true)
    {
        try
        {
            string script = Path.Combine(Path.GetTempPath(), $"pcmedic-update-{Guid.NewGuid():N}.ps1");
            File.WriteAllText(script, BuildInstallScript(archivePath, AppContext.BaseDirectory, Environment.ProcessId, restart));

            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            Process.Start(start);
            return (true, "Update unpacked by the installer \u2014 PC Medic will reopen.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>The script waits for this process to exit, unpacks, copies and optionally restarts.</summary>
    internal static string BuildInstallScript(string archivePath, string appDirectory, int pid, bool restart)
    {
        string archive = archivePath.Replace("'", "''");
        string target = appDirectory.TrimEnd(Path.DirectorySeparatorChar).Replace("'", "''");

        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.AppendLine("$processId = " + pid);
        script.AppendLine("while (Get-Process -Id $processId -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 400 }");
        script.AppendLine("Start-Sleep -Milliseconds 700");
        script.AppendLine("$work = Join-Path $env:TEMP ('pcmedic-unpack-' + [guid]::NewGuid().ToString('N'))");
        script.AppendLine("New-Item -ItemType Directory -Path $work -Force | Out-Null");
        script.AppendLine("$source = '" + archive + "'");
        script.AppendLine("if ($source.ToLower().EndsWith('.zip')) { Expand-Archive -LiteralPath $source -DestinationPath $work -Force }");
        script.AppendLine("else { Copy-Item -LiteralPath $source -Destination $work -Force }");
        script.AppendLine("$exe = Get-ChildItem -LiteralPath $work -Recurse -Filter 'PC Medic.exe' | Select-Object -First 1");
        script.AppendLine("if ($null -eq $exe) { throw 'The package does not contain PC Medic.exe.' }");
        script.AppendLine("Copy-Item -Path (Join-Path $exe.Directory.FullName '*') -Destination '" + target + "' -Recurse -Force");
        script.AppendLine("Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue");

        if (restart)
        {
            script.AppendLine("Start-Process -FilePath (Join-Path '" + target + "' 'PC Medic.exe') -WorkingDirectory '" + target + "'");
        }

        script.AppendLine("Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue");
        return script.ToString();
    }

    internal static bool IsNewer(string current, string latest)
        => Version.TryParse(Normalise(current), out Version? running) &&
           Version.TryParse(Normalise(latest), out Version? offered) &&
           offered > running;

    internal static string Normalise(string? value)
    {
        string text = (value ?? string.Empty).Trim();

        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];

        int cut = text.IndexOfAny(new[] { '-', '+' });
        if (cut > 0) text = text[..cut];

        return text.Length == 0 ? "0.0.0" : text;
    }

    private static string? SafeName(string url)
    {
        try
        {
            return Path.GetFileName(new Uri(url).LocalPath);
        }
        catch
        {
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PCMedic-SelfUpdate");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
