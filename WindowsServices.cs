using Microsoft.Win32;

namespace PC_Medic;

/// <summary>Enumerates automatic Windows services so boot can be trimmed or delayed.</summary>
internal static class WindowsServices
{
    private const string ServicesRoot = @"SYSTEM\CurrentControlSet\Services";
    private const int StartAutomatic = 2;
    private const int TypeWin32Own = 0x10;
    private const int TypeWin32Share = 0x20;

    public static List<ServiceEntry> AutoStartServices()
    {
        var services = new List<ServiceEntry>();

        try
        {
            using RegistryKey root = Registry.LocalMachine.OpenSubKey(ServicesRoot)
                ?? throw new InvalidOperationException("Services registry is unavailable.");

            foreach (string name in root.GetSubKeyNames())
            {
                try
                {
                    using RegistryKey? key = root.OpenSubKey(name);
                    if (key is null) continue;

                    if (key.GetValue("Start") is not int start || start != StartAutomatic) continue;
                    if (key.GetValue("Type") is not int type) continue;
                    if ((type & (TypeWin32Own | TypeWin32Share)) == 0) continue; // skip kernel drivers

                    string image = key.GetValue("ImagePath") as string ?? string.Empty;
                    bool delayed = key.GetValue("DelayedAutostart") is int delayedFlag && delayedFlag == 1;

                    services.Add(new ServiceEntry
                    {
                        Name = name,
                        DisplayName = (key.GetValue("DisplayName") as string ?? name).Trim(),
                        ImagePath = image,
                        Delayed = delayed,
                        ThirdParty = !IsSystemImage(image),
                    });
                }
                catch
                {
                    // Individual keys can be locked down; skip them.
                }
            }
        }
        catch (Exception ex)
        {
            ActivityLog.Warn($"Could not enumerate services: {ex.Message}");
        }

        return services
            .OrderBy(s => s.Delayed)
            .ThenBy(s => s.ThirdParty ? 0 : 1)
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static (bool Ok, string Message) SetDelayed(ServiceEntry service, bool delayed)
    {
        try
        {
            using RegistryKey key = Registry.LocalMachine.OpenSubKey($@"{ServicesRoot}\{service.Name}", writable: true)
                ?? throw new InvalidOperationException("Service key is not writable.");

            key.SetValue("DelayedAutostart", delayed ? 1 : 0, RegistryValueKind.DWord);
            service.Delayed = delayed;
            return (true, delayed ? "Delayed" : "Immediate");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Administrator privileges are required to change service start type.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static bool IsSystemImage(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return true;
        string probe = Environment.ExpandEnvironmentVariables(imagePath).ToLowerInvariant();
        return probe.Contains(@"\windows\system32") || probe.Contains(@"\windows\syswow64") ||
               probe.Contains(@"\systemroot\") || !probe.Contains(":\\");
    }
}
