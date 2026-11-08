using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PC_Medic;

/// <summary>Static machine facts read once from the registry and the filesystem.</summary>
internal static class MachineInfo
{
    static MachineInfo()
    {
        CpuName = ReadString(
            @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
            "ProcessorNameString",
            RegistryHive.LocalMachine) ?? "Unknown processor";

        (GpuName, GpuMemory) = ReadGraphics();

        MachineName = Environment.MachineName;

        using RegistryKey? version = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        string product = version?.GetValue("ProductName") as string ?? "Windows";
        string display = version?.GetValue("DisplayVersion") as string ?? string.Empty;
        string build = version?.GetValue("CurrentBuildNumber") as string ?? string.Empty;

        int buildNumber = int.TryParse(build, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : 0;

        if (buildNumber >= 22000) product = product.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
        OsName = string.IsNullOrWhiteSpace(build)
            ? product
            : $"{product} {display}  \u2022  build {build}".Trim();

        Cores = Environment.ProcessorCount;
        LogicalCpus = Environment.ProcessorCount;

        string drive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        SystemDrive = drive;
        try
        {
            var info = new DriveInfo(drive);
            DriveTotal = (ulong)info.TotalSize;
            DriveFree = (ulong)info.AvailableFreeSpace;
        }
        catch
        {
            DriveTotal = 0;
            DriveFree = 0;
        }

        HibernateFile = Path.Combine(drive, "hiberfil.sys");
    }

    public static string CpuName { get; }

    /// <summary>Marketing name of the adapter with the most video memory, so a discrete card wins over an iGPU.</summary>
    public static string GpuName { get; }

    /// <summary>Reported video memory in bytes; 0 when the driver publishes no size.</summary>
    public static ulong GpuMemory { get; }

    public static string GraphicsText =>
        string.IsNullOrWhiteSpace(GpuName)
            ? "no display adapter reported"
            : GpuMemory > 0
                ? $"{GpuName}  \u2022  {Theme.Bytes(GpuMemory)}"
                : GpuName;

    public static string MachineName { get; }

    public static string OsName { get; }

    public static int Cores { get; }

    public static int LogicalCpus { get; }

    public static string SystemDrive { get; }

    public static ulong DriveTotal { get; private set; }

    public static ulong DriveFree { get; private set; }

    /// <summary>Re-reads drive capacity after a cleanup without rebuilding the rest of the profile.</summary>
    public static void RefreshDrive()
    {
        try
        {
            var info = new DriveInfo(SystemDrive);
            DriveTotal = (ulong)info.TotalSize;
            DriveFree = (ulong)info.AvailableFreeSpace;
        }
        catch
        {
            // The system drive disappeared mid-session; keep the last known figures.
        }
    }

    public static string HibernateFile { get; }

    public static bool HibernateAvailable => File.Exists(HibernateFile);

    public static DateTime LastBoot => DateTime.Now - Native.Uptime;

    public static string Runtime => RuntimeInformation.FrameworkDescription;

    /// <summary>Primary IPv4 of an active adapter, ignoring loopback, tunnels and link-local leases.</summary>
    public static (string Address, string Adapter) LocalAddress()
    {
        try
        {
            List<NetworkInterface> candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                .Where(adapter => adapter.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .OrderByDescending(HasGateway)
                .ThenBy(IsVirtual)
                .ThenBy(Rank)
                .ToList();

            foreach (NetworkInterface adapter in candidates)
            {
                foreach (UnicastIPAddressInformation unicast in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(unicast.Address)) continue;

                    string text = unicast.Address.ToString();
                    if (text.StartsWith("169.254", StringComparison.Ordinal)) continue;

                    return (text, adapter.Name);
                }
            }
        }
        catch
        {
            // No usable network stack right now; the fallback text says so honestly.
        }

        return (string.Empty, string.Empty);
    }

    /// <summary>The adapter holding the default route is the one whose address actually works.</summary>
    private static bool HasGateway(NetworkInterface adapter)
    {
        try
        {
            return adapter.GetIPProperties().GatewayAddresses.Any(gateway =>
                gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                !gateway.Address.Equals(IPAddress.Any));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Hyper-V switches, VPN dialers and Bluetooth links rank after a real card.</summary>
    private static bool IsVirtual(NetworkInterface adapter)
    {
        string name = adapter.Name;

        return name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
            || name.Contains("TAP-", StringComparison.OrdinalIgnoreCase)
            || name.Contains("VPN", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);
    }

    private static int Rank(NetworkInterface adapter) => adapter.NetworkInterfaceType switch
    {
        NetworkInterfaceType.Wireless80211 => 0,
        NetworkInterfaceType.Ethernet => 1,
        _ => 2,
    };

    public static string LocalAddressText
    {
        get
        {
            (string address, string adapter) = LocalAddress();
            return string.IsNullOrEmpty(address) ? "no active adapter" : $"{address}  \u2022  {adapter}";
        }
    }

    private const string DisplayClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    /// <summary>
    /// Reads the display class keys instead of WMI: the same driver data, no extra dependency and no startup wait.
    /// Entries without a DriverDesc are software mappings rather than adapters and are skipped.
    /// </summary>
    private static (string Name, ulong Memory) ReadGraphics()
    {
        try
        {
            using RegistryKey? root = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            if (root is null) return (string.Empty, 0);

            (string Name, ulong Memory) best = (string.Empty, 0);

            foreach (string sub in root.GetSubKeyNames())
            {
                if (sub.Length != 4 || !sub.All(char.IsDigit)) continue;

                using RegistryKey? key = root.OpenSubKey(sub);
                string? name = (key?.GetValue("DriverDesc") as string)?.Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;

                ulong memory = ReadVideoMemory(key);
                if (memory >= best.Memory) best = (name, memory);
            }

            return best;
        }
        catch
        {
            return (string.Empty, 0);
        }
    }

    /// <summary>qwMemorySize is a byte count; the older MemorySize blob is a little-endian DWORD of bytes.</summary>
    private static ulong ReadVideoMemory(RegistryKey? key)
    {
        try
        {
            if (key?.GetValue("HardwareInformation.qwMemorySize") is long bytes && bytes > 0) return (ulong)bytes;

            if (key?.GetValue("HardwareInformation.MemorySize") is byte[] raw && raw.Length >= 4)
            {
                return BitConverter.ToUInt32(raw, 0);
            }
        }
        catch
        {
            // Some drivers guard these values; a missing size just hides the figure.
        }

        return 0;
    }

    private static string? ReadString(string path, string value, RegistryHive hive)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using RegistryKey? key = baseKey.OpenSubKey(path);
            return (key?.GetValue(value) as string)?.Trim();
        }
        catch
        {
            return null;
        }
    }
}
