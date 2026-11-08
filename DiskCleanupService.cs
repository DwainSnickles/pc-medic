namespace PC_Medic;

internal readonly record struct CleanReport(ulong Freed, int Removed, int Failed);

/// <summary>
/// Clears the temp folders that live on the system drive. Files touched in the last ten minutes are
/// left alone, so an installer or a running tool keeps the files it is using right now.
/// </summary>
internal static class DiskCleanupService
{
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);

    public static CleanReport Clean()
    {
        string localTemp = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
        string windowsTemp = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");

        var roots = new List<string> { Path.GetTempPath(), localTemp, windowsTemp };
        ulong freed = 0;
        int removed = 0;
        int failed = 0;

        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            Walk(root, ref freed, ref removed, ref failed);
        }

        return new CleanReport(freed, removed, failed);
    }

    private static void Walk(string folder, ref ulong freed, ref int removed, ref int failed)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(folder);
        }
        catch
        {
            failed++;
            files = Array.Empty<string>();
        }

        foreach (string file in files)
        {
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists) continue;
                if (DateTime.UtcNow - info.LastWriteTimeUtc < Grace) continue;

                ulong size = (ulong)info.Length;
                if ((info.Attributes & FileAttributes.ReadOnly) != 0) info.Attributes = FileAttributes.Normal;
                info.Delete();
                freed += size;
                removed++;
            }
            catch
            {
                // Locked by a running process; it will be collectable next time.
                failed++;
            }
        }

        string[] children;
        try
        {
            children = Directory.GetDirectories(folder);
        }
        catch
        {
            failed++;
            return;
        }

        foreach (string child in children)
        {
            Walk(child, ref freed, ref removed, ref failed);

            try
            {
                if (!Directory.EnumerateFileSystemEntries(child).Any()) Directory.Delete(child);
            }
            catch
            {
                failed++;
            }
        }
    }
}
