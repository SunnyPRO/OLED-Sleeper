using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OLED_Sleeper.Infrastructure;
using OLED_Sleeper.UI.Services.Interfaces;
using OLED_Sleeper.UI.ViewModels;
using System.Windows;

namespace OLED_Sleeper.UI.Services
{
    /// <summary>
    /// Owns the lifetime of the <see cref="MainWindow"/>. The window is created on demand
    /// (via the DI container) and fully closed when the user sends it to the tray, which
    /// releases the DirectX swap chain and drops idle GPU usage to ~0. A fresh window is
    /// resolved from DI on the next tray-icon activation; the backing <see cref="MainViewModel"/>
    /// is a singleton so navigation / edit state survives open-close cycles.
    /// </summary>
    public class MainWindowService : IMainWindowService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly MainViewModel _mainViewModel;
        private readonly ApplicationOptions _options;
        private MainWindow? _currentWindow;

        public MainWindowService(
            IServiceProvider serviceProvider,
            MainViewModel mainViewModel,
            IOptions<ApplicationOptions> options)
        {
            _serviceProvider = serviceProvider;
            _mainViewModel = mainViewModel;
            _options = options.Value;
        }

        /// <summary>
        /// Called once during bootstrap. Opens the window unless <see cref="ApplicationOptions.StartHidden"/>
        /// is set, in which case no window is created at all — the app just starts in the tray.
        /// </summary>
        public void SetupMainWindow()
        {
            if (!_options.StartHidden)
            {
                ShowMainWindow();
            }
        }

        /// <summary>
        /// Brings the main window to the foreground, creating it first if the user previously
        /// closed it to the tray.
        /// </summary>
        public void ShowMainWindow()
        {
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(ShowMainWindow);
                return;
            }

            if (_currentWindow == null)
            {
                _currentWindow = _serviceProvider.GetRequiredService<MainWindow>();
                _currentWindow.DataContext = _mainViewModel;
                _currentWindow.Closed += OnWindowClosed;
                Application.Current.MainWindow = _currentWindow;
            }

            if (_currentWindow.WindowState == WindowState.Minimized)
            {
                _currentWindow.WindowState = WindowState.Normal;
            }
            _currentWindow.Show();
            _currentWindow.Activate();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (sender is MainWindow window)
            {
                window.Closed -= OnWindowClosed;
            }
            _currentWindow = null;
            // The window is gone; ensure Application.Current.MainWindow isn't left pointing at
            // a disposed instance. The app itself keeps running because ShutdownMode is
            // OnExplicitShutdown (see App.xaml) — explicit Shutdown only happens via tray Exit.
            if (ReferenceEquals(Application.Current.MainWindow, sender))
            {
                Application.Current.MainWindow = null;
            }
        }
    }
}
