using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PC_Medic;

/// <summary>What one winget invocation printed, trimmed to the tail that carries the outcome.</summary>
internal readonly record struct WingetResult(bool Ok, int ExitCode, string Output)
{
    /// <summary>First usable diagnostic line, for a compact UI message.</summary>
    public string Reason => Winget.LastLine(Output, ExitCode);

    public bool Says(string text) => Output.Contains(text, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Runs winget with its output streamed, so callers can drive a real progress bar from the transfer
/// figures winget prints rather than parking a spinner. Used by the upgrade pass and the benchmark
/// rig installer; both need the same cancellation and the same tail-for-diagnostics handling.
/// </summary>
internal static class Winget
{
    private static readonly Regex PercentMark = new(@"\d{1,3}\s*%", RegexOptions.Compiled);

    private static readonly Regex Transfer = new(
        @"(?<done>\d+(?:[.,]\d+)?)\s*(?<unit>KB|MB|GB)\s*/\s*(?<total>\d+(?:[.,]\d+)?)\s*\k<unit>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>winget prints 100% before the install phase, so the bar stops short until exit.</summary>
    private const int RunningCeiling = 97;

    public static async Task<WingetResult> RunAsync(
        IReadOnlyList<string> arguments,
        IProgress<int>? percent = null,
        CancellationToken token = default)
    {
        string? winget = UpdatesService.WingetPath;

        if (winget is null) return new WingetResult(false, -1, "winget is not installed.");

        var start = new ProcessStartInfo(winget)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        foreach (string argument in arguments) start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };

        try
        {
            if (!process.Start()) return new WingetResult(false, -1, "winget did not start.");
        }
        catch (Exception ex)
        {
            return new WingetResult(false, -1, ex.Message);
        }

        var tail = new StringBuilder();
        Task output = PumpAsync(process.StandardOutput, tail, percent);
        Task error = PumpAsync(process.StandardError, tail, percent);

        using (token.Register(() => Stop(process)))
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
        }

        await Task.WhenAll(output, error).ConfigureAwait(false);

        string text;
        lock (tail) text = tail.ToString();

        return new WingetResult(process.ExitCode == 0, process.ExitCode, text);
    }

    /// <summary>Drains one stream, keeping a short tail for the message and reading progress out of it.</summary>
    private static async Task PumpAsync(StreamReader reader, StringBuilder tail, IProgress<int>? percent)
    {
        char[] buffer = new char[512];

        while (true)
        {
            int read;

            try
            {
                read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            if (read <= 0) return;

            string window;

            lock (tail)
            {
                tail.Append(buffer, 0, read);
                if (tail.Length > 4000) tail.Remove(0, tail.Length - 4000);
                window = tail.ToString();
            }

            int value = ReadPercent(window);
            if (value > 0) percent?.Report(value);
        }
    }

    /// <summary>The last transfer ratio winget printed, else its last percentage mark, else nothing.</summary>
    internal static int ReadPercent(string text)
    {
        Match? transfer = Transfer.Matches(text).Cast<Match>().LastOrDefault();

        if (transfer is not null &&
            TryNumber(transfer.Groups["done"].Value, out double done) &&
            TryNumber(transfer.Groups["total"].Value, out double total) &&
            total > 0)
        {
            return Math.Clamp((int)Math.Round(done / total * 100), 0, RunningCeiling);
        }

        Match? mark = PercentMark.Matches(text).Cast<Match>().LastOrDefault();
        if (mark is null) return 0;

        return int.TryParse(mark.Value.TrimEnd('%', ' '), out int value)
            ? Math.Clamp(value, 0, RunningCeiling)
            : 0;
    }

    private static bool TryNumber(string text, out double value)
        => double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>First usable diagnostic line, read from the end of the output.</summary>
    internal static string LastLine(string text, int exitCode)
    {
        foreach (string raw in text.Replace("\r", "\n").Split('\n').Reverse())
        {
            string line = raw.Trim();

            if (line.Length < 4) continue;
            if (line.All(character => character is '-' or ' ' or '\u2500' or '\u2588' or '\u2591' or '\u2592' or '\u2593')) continue;

            return Theme.Truncate(line, 90);
        }

        return $"winget exited with code {exitCode}";
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone, or not ours to kill.
        }
    }
}
