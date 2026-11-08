using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace PC_Medic;

/// <summary>Thin, dependency-free kernel32 wrappers for live telemetry.</summary>
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    public static (ulong Total, ulong Available) PhysicalMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status)) return (0, 0);
        return (status.TotalPhys, status.AvailPhys);
    }

    public static bool CpuTimes(out long idle, out long kernel, out long user)
        => GetSystemTimes(out idle, out kernel, out user);

    public static TimeSpan Uptime => TimeSpan.FromMilliseconds(Environment.TickCount64);

    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Relaunches PC Medic through UAC so machine-wide tweaks can be applied.</summary>
    public static bool RestartElevated()
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
            };
            Process.Start(start);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool OpenShell(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
