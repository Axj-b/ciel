using System;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace SourceGit.Views
{
    public partial class SessionsDashboard : UserControl
    {
        public SessionsDashboard()
        {
            InitializeComponent();
            ViewModels.TerminalService.SessionsChanged += OnSessionsChanged;
            Refresh();
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);
            ViewModels.TerminalService.SessionsChanged -= OnSessionsChanged;
        }

        private void OnSessionsChanged()
        {
            Dispatcher.UIThread.Post(Refresh);
        }

        private void Refresh()
        {
            var filter = FilterBox.Text?.Trim();
            var sessions = ViewModels.TerminalService.Sessions;

            if (string.IsNullOrEmpty(filter))
            {
                Rows.ItemsSource = sessions;
                EmptyHint.IsVisible = sessions.Count == 0;
            }
            else
            {
                var filtered = sessions
                    .Where(s =>
                        s.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        s.RepositoryName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        s.Profile.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Rows.ItemsSource = filtered;
                EmptyHint.IsVisible = filtered.Count == 0;
            }
        }

        private void OnFilterChanged(object sender, TextChangedEventArgs e)
        {
            Refresh();
        }

        private void OnFocusRow(object sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is ViewModels.TerminalSession session)
            {
                if (App.GetLauncher() is ViewModels.Launcher launcher)
                    launcher.FocusTerminalSession(session);

                // Leave dashboard mode after jumping to the session.
                (DataContext as ViewModels.Repository)?.CloseSessionsDashboard();
            }

            e.Handled = true;
        }

        private void OnKillRow(object sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is ViewModels.TerminalSession session)
                ViewModels.TerminalService.Kill(session);

            e.Handled = true;
        }

        private void OnCloseRow(object sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is ViewModels.TerminalSession session)
                ViewModels.TerminalService.Close(session);

            e.Handled = true;
        }

        private void OnKillAll(object sender, RoutedEventArgs e)
        {
            ViewModels.TerminalService.KillAll();
            e.Handled = true;
        }

        private void OnCloseAll(object sender, RoutedEventArgs e)
        {
            ViewModels.TerminalService.CloseAll();
            e.Handled = true;
        }

        private void OnCloseDashboard(object sender, RoutedEventArgs e)
        {
            (DataContext as ViewModels.Repository)?.CloseSessionsDashboard();
            e.Handled = true;
        }
    }
}
