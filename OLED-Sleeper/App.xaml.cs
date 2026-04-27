using System.Windows;
using System.Windows.Threading;
using OLED_Sleeper.Infrastructure;
using Serilog;

namespace OLED_Sleeper
{
    /// <summary>
    /// Entry point for the WPF application.
    /// Handles WPF lifecycle events and delegates application initialization
    /// and shutdown to <see cref="ApplicationBootstrapper"/>.
    /// </summary>
    /// <remarks>
    /// This class intentionally contains minimal logic. All application
    /// initialization, dependency setup, and runtime orchestration are handled
    /// by <see cref="ApplicationBootstrapper"/>.
    /// </remarks>
    public partial class App : Application
    {
        private ApplicationBootstrapper? _bootstrapper;

        /// <summary>
        /// Invoked when the application starts. Creates and initializes the
        /// <see cref="ApplicationBootstrapper"/> using the provided startup arguments.
        /// </summary>
        /// <param name="e">Startup event arguments containing command-line parameters.</param>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Log-only handlers are safe to install pre-init: they let bootstrap failures
            // produce a useful trace before the runtime tears the process down.
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            SessionEnding += App_SessionEnding;
            StartBootstrapper(e);

            // The dispatcher swallow is only safe AFTER bootstrapping has wired up the tray
            // icon and shutdown path. If we hooked it earlier, a startup failure would leave
            // an invisible zombie process (no window, no tray, ShutdownMode=OnExplicitShutdown)
            // that the user cannot exit. With the hook installed here, a pre-init failure
            // crashes normally and the user can simply relaunch.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        /// <summary>
        /// Invoked when the application is exiting. Ensures the bootstrapper
        /// performs its shutdown logic and releases resources.
        /// </summary>
        /// <param name="e">Exit event arguments.</param>
        protected override void OnExit(ExitEventArgs e)
        {
            _bootstrapper?.ShutdownApp();
            _bootstrapper?.Dispose();
            base.OnExit(e);
        }

        /// <summary>
        /// Handles Windows session ending events (e.g., user logoff or system shutdown)
        /// and forwards shutdown handling to the bootstrapper.
        /// </summary>
        private void App_SessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            _bootstrapper?.ShutdownApp();
        }

        /// <summary>
        /// Creates and initializes the <see cref="ApplicationBootstrapper"/>
        /// using the provided startup arguments.
        /// </summary>
        private void StartBootstrapper(StartupEventArgs e)
        {
            _bootstrapper = new ApplicationBootstrapper(e.Args);
            _bootstrapper.Initialize();
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error(e.Exception, "Unhandled exception on UI dispatcher; swallowing to keep tray app alive.");
            e.Handled = true;
        }

        private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // IsTerminating is true for fatal exceptions. We can't recover, but we can log
            // before the runtime tears the process down so the user has something to share.
            if (e.ExceptionObject is Exception ex)
            {
                Log.Fatal(ex, "Unhandled AppDomain exception (IsTerminating={IsTerminating}).", e.IsTerminating);
            }
            else
            {
                Log.Fatal("Unhandled AppDomain exception (non-Exception object) IsTerminating={IsTerminating}.", e.IsTerminating);
            }
            Log.CloseAndFlush();
        }

        private static void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
        {
            Log.Error(e.Exception, "Unobserved task exception.");
            e.SetObserved();
        }
    }
}
