using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PC_Medic;

internal readonly record struct MemoryReport(int Opened, int Trimmed, int Skipped, ulong Freed, bool Purged);

/// <summary>
/// The memory optimizer. Asks every process we are allowed to touch to hand back its working set,
/// then purges the standby list when the session is elevated. Nothing is terminated: trimmed pages
/// are paged back in on demand, so the change is safe and self-reversing.
/// </summary>
internal static class MemoryService
{
    private const int ProcessSetQuota = 0x0100;

    // EmptyWorkingSet requires the full query right; PROCESS_QUERY_LIMITED_INFORMATION is refused.
    private const int ProcessQueryInformation = 0x0400;

    private const int SystemMemoryListInformation = 0x50;
    private const int MemoryPurgeStandbyList = 4;

    private const int TokenAdjustPrivileges = 0x20;
    private const int TokenQuery = 0x8;
    private const int SePrivilegeEnabled = 0x2;

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int infoClass, IntPtr info, int length);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr token,
        [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref TokenPrivileges state,
        int length,
        IntPtr previous,
        IntPtr returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public int Low;
        public int High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public int Count;
        public Luid Privilege;
        public int Attributes;
    }

    /// <summary>Runs the whole pass; call it from a worker thread, it walks every process on the machine.</summary>
    public static MemoryReport Trim()
    {
        (ulong _, ulong before) = Native.PhysicalMemory();
        int opened = 0;
        int trimmed = 0;
        int skipped = 0;

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                using (process)
                {
                    if (process.Id <= 4 || process.Id == Environment.ProcessId)
                    {
                        skipped++;
                        continue;
                    }

                    IntPtr handle = OpenProcess(ProcessSetQuota | ProcessQueryInformation, false, process.Id);
                    if (handle == IntPtr.Zero)
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        opened++;
                        if (EmptyWorkingSet(handle)) trimmed++;
                        else skipped++;
                    }
                    finally
                    {
                        CloseHandle(handle);
                    }
                }
            }
            catch
            {
                skipped++;
            }
        }

        bool purged = PurgeStandby();
        (ulong _, ulong after) = Native.PhysicalMemory();

        return new MemoryReport(opened, trimmed, skipped, after > before ? after - before : 0, purged);
    }

    private static bool PurgeStandby()
    {
        if (!Native.IsElevated) return false;

        try
        {
            if (!EnableProfilePrivilege()) return false;

            IntPtr block = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(block, MemoryPurgeStandbyList);
                return NtSetSystemInformation(SystemMemoryListInformation, block, sizeof(int)) == 0;
            }
            finally
            {
                Marshal.FreeHGlobal(block);
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>NtSetSystemInformation refuses the standby purge without SeProfileSingleProcessPrivilege.</summary>
    private static bool EnableProfilePrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out IntPtr token)) return false;

        try
        {
            if (!LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out Luid luid)) return false;

            var state = new TokenPrivileges
            {
                Count = 1,
                Privilege = luid,
                Attributes = SePrivilegeEnabled,
            };

            bool ok = AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero);
            return ok && Marshal.GetLastWin32Error() == 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }
}
