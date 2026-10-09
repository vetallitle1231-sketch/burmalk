using System;
using System.Linq;
using System.Threading;
using System.Windows;
using Burmalk.Services;

namespace Burmalk
{
    public partial class App : Application
    {
        // Singleton app-wide services
        public static SystemMonitor Monitor { get; private set; }
        public static Services.DataStore Store { get; private set; }
        public static OllamaMonitor Ai { get; private set; }
        public static string SelfTestMode { get; private set; } = "";

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Global safety net: never let a sampler crash the app.
            DispatcherUnhandledException += (s, ex) =>
            {
                if(Verification.DirectoryPath!=null) { Verification.Failures.Add(ex.Exception.ToString());ex.Handled=true;return; }
                try
                {
                    Services.ErrorLog.Write("Dispatcher EXC: " + ex.Exception.ToString()
                        + Environment.NewLine + "STACK: " + ex.Exception.StackTrace);
                }
                catch { }
                MessageBox.Show("Internal UI error (caught, app continues):\n" + ex.Exception.Message,
                    "Burmalk", MessageBoxButton.OK, MessageBoxImage.Warning);
                ex.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                try { Services.ErrorLog.Write("AppDomain: " + ex.ExceptionObject); } catch { }
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, ex) =>
            {
                try { Services.ErrorLog.Write("Task: " + ex.Exception); } catch { }
            };

            // Self-test arguments (used for automated verification, optional)
            var args = e.Args ?? new string[0];
            if (args.Contains("--selftest")) SelfTestMode = "navigate";
            if (args.Contains("--selftest-exit")) SelfTestMode = "navigate-exit";
            int verifyArg=Array.IndexOf(args,"--verify-dir");
            if(SelfTestMode!=""&&verifyArg>=0&&verifyArg+1<args.Length)
            {
                Verification.DirectoryPath=System.IO.Path.GetFullPath(args[verifyArg+1]);
                System.IO.Directory.CreateDirectory(Verification.DirectoryPath);
            }

            Store = new Services.DataStore(Verification.DirectoryPath==null?null:System.IO.Path.Combine(Verification.DirectoryPath,"app-data"));
            Store.Load();
            Appearance.Apply(Store.Settings);
            if(Verification.DirectoryPath!=null) { Store.Settings.GameDetectionEnabled=false;Store.Settings.OverlayEnabled=false; }
            Ai = new OllamaMonitor();
            Ai.SetEndpoint(Store.Settings.AiEndpoint);

            Monitor = new SystemMonitor(Store);
            Monitor.Start();

            var window = new MainWindow();

            if (SelfTestMode.StartsWith("navigate"))
            {
                window.SelfTestRequested = SelfTestMode;
            }
            window.Show();
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            Services.OverlayManager.Initialize(this, Store, Monitor);
            Services.GameService.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { Services.GameService.Stop(); } catch { }
            try { Services.OverlayManager.Shutdown(); } catch { }
            try { Monitor.Dispose(); } catch { }
            try { Ai.Dispose(); } catch { }
            try { Store.Save(); } catch { }
            base.OnExit(e);
        }
    }
}

