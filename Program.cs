namespace PC_Medic
{
    internal static class Program
    {
        private const string InstanceMutex = @"Local\PCMedic.Console.1";
        private const string ShowEventName = @"Local\PCMedic.ShowConsole.1";

        /// <summary>Exit code when a copy already owns the console, so the debugger and scripts can tell why nothing opened.</summary>
        private const int AlreadyRunningExitCode = 2;

        /// <summary>The main entry point for the application.</summary>
        [STAThread]
        private static void Main()
        {
            // One console per session: a second launch surfaces the running window and exits.
            using var instance = new Mutex(true, InstanceMutex, out bool isFirst);
            _instance = instance;

            if (!isFirst)
            {
                // Runs even under the debugger, before the splash: ask the parked copy to surface, then leave.
                System.Diagnostics.Debug.WriteLine(
                    "PC MEDIC is already running \u2014 asking the existing console to come back from the tray.");
                ActivityLog.Info("PC MEDIC is already running \u2014 brought the console back from the tray.");
                SignalRunningConsole();
                Environment.ExitCode = AlreadyRunningExitCode;
                return;
            }

            ApplicationConfiguration.Initialize();

            try
            {
                Application.SetColorMode(SystemColorMode.Dark);
            }
            catch
            {
                // Window manager has no dark title bar; the app itself is dark regardless.
            }

            if (!Native.IsElevated)
            {
                using var gate = new ElevationGateForm();

                if (gate.ShowDialog() == DialogResult.OK && gate.RestartRequested)
                {
                    try
                    {
                        instance.ReleaseMutex();
                    }
                    catch (ApplicationException)
                    {
                        // Already released; the elevated copy still needs the name free.
                    }

                    if (Native.RestartElevated()) return;

                    ActivityLog.Warn("Administrator access was declined \u2014 continuing in limited mode.");
                }
                else
                {
                    ActivityLog.Warn("Running limited: repairs, machine-wide tweaks and Disk Defrag are read-only.");
                }
            }
            else
            {
                ActivityLog.Info("Running elevated \u2014 every maintenance task is available.");
            }

            ActivityLog.Info("PC Medic online \u2014 portable maintenance suite.");

            Application.ThreadException += (_, e) => ActivityLog.Error(e.Exception.Message);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex) ActivityLog.Error(ex.Message);
            };

            using var splash = new SplashForm();
            splash.Show();
            Application.DoEvents();

            using var shell = new MainForm();

            while (!splash.Finished)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }

            splash.Close();
            ListenForSecondLaunch(shell);
            Application.Run(shell);
        }

        /// <summary>Asks the copy that owns the console to come back out of the tray.</summary>
        private static Mutex? _instance;

    /// <summary>Gives up the console name before relaunching, so the elevated copy is not turned away.</summary>
    internal static void ReleaseInstance()
    {
        try
        {
            _instance?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Already released; the name is free either way.
        }
    }

    /// <summary>Takes the console name back when the relaunch was cancelled.</summary>
    internal static void ReclaimInstance()
    {
        try
        {
            _instance?.WaitOne(0);
        }
        catch (Exception)
        {
            // Nothing to reclaim; the next launch simply starts a fresh console.
        }
    }

    private static void SignalRunningConsole()
        {
            try
            {
                using var show = EventWaitHandle.OpenExisting(ShowEventName);
                show.Set();
            }
            catch (Exception)
            {
                // The other copy has not finished starting yet; it will be on screen already.
            }
        }

        /// <summary>Wakes the shell whenever a second copy asks it to surface.</summary>
        private static void ListenForSecondLaunch(MainForm shell)
        {
            var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

            // Create the handle up front so a request that arrives during the splash still lands.
            _ = shell.Handle;

            var listener = new Thread(() =>
            {
                while (true)
                {
                    show.WaitOne();

                    try
                    {
                        shell.BeginInvoke(() =>
                        {
                            ActivityLog.Info("PC MEDIC was launched again \u2014 window restored from the tray.");
                            shell.ShowFromTray();
                            shell.NotifySecondLaunch();
                        });
                    }
                    catch (Exception)
                    {
                        // The console is closing; nothing left to surface.
                    }
                }
            })
            {
                IsBackground = true,
                Name = "PC Medic single instance",
            };

            listener.Start();
        }
    }
}
