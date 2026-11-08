using System.Diagnostics;
using System.Text;

namespace PC_Medic;

internal readonly record struct CmdResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;

    public string AllText => string.Join(
        Environment.NewLine,
        new[] { Error, Output }.Where(t => !string.IsNullOrWhiteSpace(t)));

    /// <summary>First meaningful line of diagnostics, for compact UI messages.</summary>
    public string Reason
    {
        get
        {
            foreach (string raw in AllText.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length > 3) return Theme.Truncate(line, 160);
            }

            return $"exit code {ExitCode}";
        }
    }
}

internal static class Cmd
{
    public static CmdResult Run(string fileName, string arguments, int timeoutMs = 15000)
        => Launch(new ProcessStartInfo(fileName, arguments), fileName, timeoutMs);

    /// <summary>
    /// Runs one PowerShell command line and captures its output. Arguments travel through
    /// ArgumentList, so quotes and pipes survive without being escaped by hand.
    /// </summary>
    public static CmdResult RunPowerShell(string command, int timeoutMs = 30000)
    {
        var info = new ProcessStartInfo("powershell.exe");
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add(command);

        return Launch(info, "powershell.exe", timeoutMs);
    }

    /// <summary>Runs a script body through an encoded command, for text that must not be re-quoted.</summary>
    public static CmdResult PowerShell(string script, int timeoutMs = 30000)
    {
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return Run(
            "powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            timeoutMs);
    }

    private static CmdResult Launch(ProcessStartInfo info, string label, int timeoutMs)
    {
        try
        {
            info.UseShellExecute = false;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;

            using var process = new Process { StartInfo = info };
            if (!process.Start()) return new CmdResult(-1, string.Empty, $"Could not start {label}.");

            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return new CmdResult(-124, string.Empty, $"{label} timed out.");
            }

            return new CmdResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            return new CmdResult(-1, string.Empty, ex.Message);
        }
    }
}
