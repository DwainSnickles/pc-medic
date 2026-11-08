namespace PC_Medic
{
    internal static class Program
    {
        /// <summary>The main entry point for the application.</summary>
        [STAThread]
        private static void Main()
        {
            ApplicationConfiguration.Initialize();

            try
            {
                Application.SetColorMode(SystemColorMode.Dark);
            }
            catch
            {
                // Window manager has no dark title bar; the app itself is dark regardless.
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
            Application.Run(shell);
        }
    }
}
