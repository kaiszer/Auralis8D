using System;
using System.Collections.Specialized;
using System.Windows;
using Auralis8D.UI.ViewModels;

namespace Auralis8D.UI.Views
{
    /// <summary>
    /// Interaction logic for DebugWindow.xaml.
    /// Provides live monitoring of system events, process loopback capture diagnostics, and WASAPI states.
    /// </summary>
    public partial class DebugWindow : Window
    {
        public DebugWindow()
        {
            InitializeComponent();

            if (DataContext is DebugViewModel vm)
            {
                ((INotifyCollectionChanged)vm.Logs).CollectionChanged += OnLogsCollectionChanged;
            }
        }

        /// <summary>
        /// Automatically scrolls the list view to the newly added item if AutoScroll is checked.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Collection change event args.</param>
        private void OnLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && DataContext is DebugViewModel vm && vm.AutoScroll)
            {
                if (LogListBox.Items.Count > 0)
                {
                    LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
