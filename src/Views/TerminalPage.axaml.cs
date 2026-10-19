using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class TerminalPage : UserControl
    {
        public TerminalPage()
        {
            InitializeComponent();
        }

        private void OnNewSessionMenu(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || DataContext is not ViewModels.Repository repo)
                return;

            var shell = new MenuItem();
            shell.Header = App.Text("Terminal.Profile.Shell");
            shell.Click += (_, ev) =>
            {
                repo.NewTerminalSession(ViewModels.TerminalSession.ShellProfile);
                ev.Handled = true;
            };

            var opencode = new MenuItem();
            opencode.Header = App.Text("Terminal.Profile.OpenCode");
            opencode.Click += (_, ev) =>
            {
                repo.NewTerminalSession(ViewModels.TerminalSession.OpenCodeProfile);
                ev.Handled = true;
            };

            var menu = new ContextMenu();
            menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
            menu.Items.Add(shell);
            menu.Items.Add(opencode);
            menu.Open(button);

            e.Handled = true;
        }

        private void OnSessionTabTapped(object sender, TappedEventArgs e)
        {
            // Ignore taps on the inner close button.
            if (e.Source is Button)
                return;

            if (e.Source is Control { DataContext: ViewModels.TerminalSession session } &&
                DataContext is ViewModels.Repository repo)
            {
                repo.ShowTerminalPage(session);
                e.Handled = true;
            }
        }

        private void OnSessionTabClosed(object sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is ViewModels.TerminalSession session)
            {
                ViewModels.TerminalService.Close(session);
                e.Handled = true;
            }
        }

        private void OnRestartSelected(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo && repo.SelectedSession is { } session)
                ViewModels.TerminalService.Start(session);

            e.Handled = true;
        }

        private void OnKillSelected(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo && repo.SelectedSession is { } session)
                ViewModels.TerminalService.Kill(session);

            e.Handled = true;
        }

        private void OnCloseSelected(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Repository repo && repo.SelectedSession is { } session)
                ViewModels.TerminalService.Close(session);

            e.Handled = true;
        }
    }
}
