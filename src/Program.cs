using System;
using System.Reflection;
using System.Windows;
using vView.Core;
using vView.Ui;

namespace vView
{
    static class Program
    {
        static System.Threading.Mutex _running;

        [STAThread]
        static int Main(string[] args)
        {
            // the shared library: which app this is (settings folder, log, test variables, releases, a folder install), and its icon
            vSuite.App.Init("vView", "VVIEW", "https://vsuite.vrsion.com", InstallStyle.Folder, "libvlc");
            AppIcon.Drawer = Brand.Draw;
            if (args.Length == 2 && args[0] == "--make-icon")   // used once to make vview.ico
            {
                AppIcon.Save(args[1]);
                return 0;
            }
            if (args.Length == 1 && (args[0] == "--register-types" || args[0] == "--unregister-types"))   // the installer and uninstaller
            {
                if (args[0] == "--register-types") DefaultApp.Register(); else DefaultApp.Unregister();
                return 0;
            }
            string file = args.Length > 0 ? args[0] : "";
            // for an installer: "vView is running" (AppMutex)
            _running = new System.Threading.Mutex(false, "vView.Running");
            // already running (same settings folder): it shows this file, and this one stops here
            try { if (Instance.HandOff(file)) return 0; }
            catch (Exception ex) { Log.Write("Hand-off: " + ex.Message); }
            Log.Write("vView " + Assembly.GetExecutingAssembly().GetName().Version + " starting" + (file.Length > 0 ? " with " + file : ""));

            var app = new Application { ShutdownMode = ShutdownMode.OnLastWindowClose };   // Options can make one window per file
            app.DispatcherUnhandledException += (s, e) =>
            {
                Log.Write("Unhandled: " + e.Exception);
                Log.Flush();
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { Log.Write("Unhandled (" + (e.IsTerminating ? "ending" : "carrying on") + "): " + e.ExceptionObject); Log.Flush(); };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => { Log.Write("Unobserved task error: " + e.Exception); e.SetObserved(); };

            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/vSuite.Core;component/Ui/Controls.xaml", UriKind.Absolute)
            });
            // the music styles shared by the main window and the classic player
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/vView;component/src/Ui/Music.xaml", UriKind.Absolute) });
            var settings = AppSettings.Load();
            var theme = new VShellThemeSource();
            if (UpdateCenter.HandleCommand(file)) file = "";   // vView.exe --update (vShell's Settings): open as usual, update now
            var window = new MainWindow(settings, theme, file);
            int code;
            if (window.StartsInClassic)
            {
                // a song double-clicked while songs play in the classic player: only that shows (no flash of the full window)
                app.MainWindow = window;
                app.Dispatcher.BeginInvoke(new Action(() => { window.StartHidden(); UpdateCenter.Start(settings); }));
                code = app.Run();
            }
            else
            {
                // update checks (and telling a waiting update watchdog this version started fine) once the first window is up
                window.ContentRendered += (s, e) => UpdateCenter.Start(settings);
                code = app.Run(window);
            }
            theme.Dispose();
            return code;
        }
    }
}
