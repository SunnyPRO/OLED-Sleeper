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
            // Hook every plausible failure surface BEFORE the bootstrapper runs so even DI
            // construction errors are logged and (where survivable) swallowed. We've seen the
            // process die from transient Win32Exception 1816 ("Not enough quota") inside
            // WPF's HwndTarget.UpdateWindowSettings -> PostMessage during sleep/resume and
            // boot-time window creation; killing the process leaves a stale tray ghost and
            // makes the user think the app is "frozen".
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            SessionEnding += App_SessionEnding;
            StartBootstrapper(e);
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
