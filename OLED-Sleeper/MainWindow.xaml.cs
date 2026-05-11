using OLED_Sleeper.UI.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace OLED_Sleeper
{
    /// <summary>
    /// The main application window for OLED Sleeper.
    /// Handles window chrome, custom title bar, and delegates closing logic to the ViewModel.
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _skipCloseConfirmation;

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
        }

        #endregion Constructor

        /// <summary>
        /// Closes the settings window without asking about unsaved changes. Used only for
        /// emergency recovery paths where WPF itself is failing to render/update the window
        /// and showing another dialog would make the failure loop worse.
        /// </summary>
        public void CloseWithoutConfirmation()
        {
            _skipCloseConfirmation = true;
            Close();
        }

        #region Window Event Overrides

        /// <summary>
        /// Handles the window closing event, delegating logic to the ViewModel.
        /// Cancels closing if the ViewModel returns false.
        /// </summary>
        /// <param name="e">CancelEventArgs for the closing event.</param>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (_skipCloseConfirmation)
            {
                base.OnClosing(e);
                return;
            }

            if (DataContext is MainViewModel viewModel && !viewModel.OnWindowClosing())
            {
                e.Cancel = true;
                return;
            }
            base.OnClosing(e);
        }

        #endregion Window Event Overrides

        #region Title Bar & Window Controls

        /// <summary>
        /// Handles dragging the window when the custom title bar is clicked and dragged.
        /// </summary>
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        /// <summary>
        /// Sends the window to the tray. The window is fully closed (not hidden) so WPF can
        /// release its DirectX swap chain; a new instance is created by <c>MainWindowService</c>
        /// the next time the user clicks the tray icon.
        /// </summary>
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Toggles between maximized and normal window state when the maximize button is clicked.
        /// </summary>
        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }

        /// <summary>
        /// Closes the window when the close button is clicked.
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion Title Bar & Window Controls
    }
}
