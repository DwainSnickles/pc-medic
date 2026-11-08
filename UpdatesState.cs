namespace PC_Medic;

/// <summary>
/// One shared view of what is outdated, so the shell can show counts without opening the page.
/// The scan runs once in the background; the page publishes its own results into the same slot.
/// </summary>
internal static class UpdatesState
{
    private static readonly object Gate = new();
    private static bool _started;

    /// <summary>Available application upgrades.</summary>
    public static int Apps { get; private set; }

    /// <summary>Offered driver updates.</summary>
    public static int Drivers { get; private set; }

    public static bool Scanning { get; private set; }

    /// <summary>True once a scan has produced counts.</summary>
    public static bool Scanned { get; private set; }

    public static int Total => Apps + Drivers;

    public static event Action? Changed;

    /// <summary>Starts a one-time background scan of winget and the update agent.</summary>
    public static void BeginScan()
    {
        lock (Gate)
        {
            if (_started) return;
            _started = true;
            Scanning = true;
        }

        _ = Task.Run(async () =>
        {
            int apps = 0;
            int drivers = 0;

            Task appsTask = Task.Run(() =>
            {
                try
                {
                    (List<AppUpgrade> found, _) = UpdatesService.ScanApps();
                    apps = found.Count;
                }
                catch (Exception ex)
                {
                    ActivityLog.Warn($"Update scan skipped: {ex.Message}");
                }

                // Show the winget result while the slower driver search is still running.
                Publish(apps, drivers, complete: false);
            });

            Task driversTask = Task.Run(() =>
            {
                try
                {
                    (List<DriverUpdate> found, _) = UpdatesService.ScanDrivers();
                    drivers = found.Count;
                }
                catch (Exception ex)
                {
                    ActivityLog.Warn($"Driver scan skipped: {ex.Message}");
                }
            });

            await Task.WhenAll(appsTask, driversTask).ConfigureAwait(false);
            Publish(apps, drivers);
        });
    }

    /// <summary>Records a fresh set of counts and tells every listener.</summary>
    public static void Publish(int apps, int drivers, bool complete = true)
    {
        lock (Gate)
        {
            Apps = apps;
            Drivers = drivers;
            Scanning = !complete;
            Scanned = true;
        }

        Changed?.Invoke();
    }
}
