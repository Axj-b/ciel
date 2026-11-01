using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace SourceGit.Views
{
    /// <summary>
    /// Hosts the TerminalView of the active session and owns re-parenting between
    /// hosts (repository terminal page ↔ session moves). The view itself is owned
    /// by <see cref="ViewModels.TerminalService"/>; <see cref="ViewModels.TerminalSession.View"/>
    /// may live under only one host at a time.
    /// </summary>
    public class TerminalHost : ContentControl
    {
        public static readonly Avalonia.StyledProperty<ViewModels.TerminalSession> SessionProperty =
            Avalonia.StyledProperty<ViewModels.TerminalSession>.Register<TerminalHost, ViewModels.TerminalSession>(
                nameof(Session));

        public ViewModels.TerminalSession Session
        {
            get => GetValue(SessionProperty);
            set => SetValue(SessionProperty, value);
        }

        protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == SessionProperty)
                Adopt();
        }

        protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
        {
            base.OnAttachedToLogicalTree(e);
            Adopt();
        }

        protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
        {
            // Keep the child associated (server-side it stays in the service registry);
            // Detaching the host moves the terminal with the whole page — the PTY session
            // survives because TerminalService armed BeginReparent on the view.
            base.OnDetachedFromLogicalTree(e);
        }

        private void Adopt()
        {
            var session = Session;
            if (session is null || session.View is null)
            {
                Content = null;
                return;
            }

            Adopt(session.View);
        }

        private void Adopt(Iciclecreek.Terminal.TerminalView view)
        {
            if (ReferenceEquals(Content, view))
                return;

            // Free the view from another host first (that detach is safe: the PTY is armed).
            if (view.Parent is ContentControl oldContent && !ReferenceEquals(oldContent, this))
                oldContent.Content = null;
            else if (view.Parent is Decorator oldDecorator && !ReferenceEquals(oldDecorator, this))
                oldDecorator.Child = null;

            Content = view;
        }
    }
}
