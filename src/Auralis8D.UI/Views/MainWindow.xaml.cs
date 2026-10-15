using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Auralis8D.UI.ViewModels;

namespace Auralis8D.UI.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml.
    /// Manages window lifecycle, application selection callbacks, and clean engine shutdown.
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Event handler fired when a user toggles an application selection checkbox.
        /// </summary>
        private void OnAppCheckboxClicked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is AppProcessViewModel appVm)
            {
                if (DataContext is MainViewModel mainVm)
                {
                    // Checkbox value is already bound to IsSelected, inform ViewModel to sync with audio engine
                    mainVm.RefreshProcessList();
                }
            }
        }

        /// <summary>
        /// Ensures the audio engine is gracefully stopped and all sessions unmuted when the application closes.
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (DataContext is IDisposable disposableVm)
            {
                disposableVm.Dispose();
            }
            base.OnClosing(e);
        }
    }
}
