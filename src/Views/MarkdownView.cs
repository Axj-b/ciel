using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.Threading;

namespace SourceGit.Views
{
    /// <summary>
    ///     Scrollable markdown preview. Parsing is debounced and runs in the background; the syntax
    ///     tree is then turned into Avalonia controls on the UI thread.
    /// </summary>
    public class MarkdownView : UserControl
    {
        public static readonly StyledProperty<string> SourceProperty =
            AvaloniaProperty.Register<MarkdownView, string>(nameof(Source), string.Empty);

        public static readonly StyledProperty<string> FileNameProperty =
            AvaloniaProperty.Register<MarkdownView, string>(nameof(FileName), string.Empty);

        public static readonly StyledProperty<string> RepositoryPathProperty =
            AvaloniaProperty.Register<MarkdownView, string>(nameof(RepositoryPath), string.Empty);

        public static readonly StyledProperty<bool> IsPreviewActiveProperty =
            AvaloniaProperty.Register<MarkdownView, bool>(nameof(IsPreviewActive), true);

        public string Source
        {
            get => GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        /// <summary>Repository relative path of the markdown file. Used to resolve relative links.</summary>
        public string FileName
        {
            get => GetValue(FileNameProperty);
            set => SetValue(FileNameProperty, value);
        }

        public string RepositoryPath
        {
            get => GetValue(RepositoryPathProperty);
            set => SetValue(RepositoryPathProperty, value);
        }

        /// <summary>Inactive previews keep receiving source text but do not spend time rendering it.</summary>
        public bool IsPreviewActive
        {
            get => GetValue(IsPreviewActiveProperty);
            set => SetValue(IsPreviewActiveProperty, value);
        }

        public event Action<string> OpenFileRequested;

        public MarkdownView()
        {
            _host = new StackPanel();
            _scroll = new ScrollViewer()
            {
                Content = _host,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(18, 12, 18, 28),
            };

            Content = _scroll;
            Classes.Add("markdown_view");

            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(280), DispatcherPriority.Background, OnRenderTimer);
            _timer.Stop();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == SourceProperty ||
                change.Property == FileNameProperty ||
                change.Property == RepositoryPathProperty)
            {
                QueueRender();
            }
            else if (change.Property == IsPreviewActiveProperty)
            {
                if (IsPreviewActive)
                    QueueRender();
            }
            else if (change.Property.Name == nameof(ActualThemeVariant))
            {
                QueueRender();
            }
        }

        private void OnRenderTimer(object sender, EventArgs e)
        {
            _timer.Stop();
            _ = RenderAsync();
        }

        private void QueueRender()
        {
            _renderPending = true;
            if (!IsPreviewActive)
                return;

            _timer.Stop();
            _timer.Start();
        }

        private async Task RenderAsync()
        {
            if (!_renderPending || !IsPreviewActive)
                return;

            _renderPending = false;

            var source = Source ?? string.Empty;
            if (source.Length > MaxSourceLength)
            {
                _host.Children.Clear();
                _anchors.Clear();
                var notice = new SelectableTextBlock()
                {
                    Text = App.Text("Markdown.TooLarge"),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                };
                notice.Classes.Add("md_notice");
                _host.Children.Add(notice);
                return;
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                _host.Children.Clear();
                _anchors.Clear();
                return;
            }

            var generation = ++_renderGeneration;
            var isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
            try
            {
                var document = await Task.Run(() => Models.MarkdownRenderer.Parse(source)).ConfigureAwait(true);
                if (generation != _renderGeneration || !IsPreviewActive)
                    return;

                Build(document, isDark);
            }
            catch (Exception e)
            {
                if (generation != _renderGeneration)
                    return;

                Native.OS.LogException(e);
                _host.Children.Clear();
                var notice = new SelectableTextBlock()
                {
                    Text = App.Text("Markdown.RenderFailed"),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                };
                notice.Classes.Add("md_notice");
                _host.Children.Add(notice);
            }
        }

        private void Build(Markdig.Syntax.MarkdownDocument document, bool isDark)
        {
            var offset = _scroll.Offset;
            _host.Children.Clear();
            _anchors.Clear();

            var options = new Models.MarkdownRenderOptions()
            {
                RepositoryPath = RepositoryPath ?? string.Empty,
                FileName = FileName ?? string.Empty,
                IsDark = isDark,
                RegisterAnchor = (id, control) => _anchors[id] = control,
                OpenLink = OnLinkActivated,
            };

            try
            {
                new Models.MarkdownRenderer(options).Render(document, _host);
            }
            catch (Exception e)
            {
                Native.OS.LogException(e);
                _host.Children.Clear();
                var notice = new SelectableTextBlock()
                {
                    Text = App.Text("Markdown.RenderFailed"),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                };
                notice.Classes.Add("md_notice");
                _host.Children.Add(notice);
                return;
            }

            if (offset.Y > 0)
                Dispatcher.UIThread.Post(() => _scroll.Offset = offset, DispatcherPriority.Background);
        }

        private void OnLinkActivated(Models.MarkdownLink link)
        {
            if (link == null)
                return;

            switch (link.Kind)
            {
                case Models.MarkdownLinkKind.External:
                    Native.OS.OpenBrowser(link.ExternalUrl);
                    break;
                case Models.MarkdownLinkKind.Anchor:
                    ScrollToAnchor(link.Anchor);
                    break;
                case Models.MarkdownLinkKind.File:
                    if (OpenFileRequested != null)
                        OpenFileRequested.Invoke(link.RelativePath);
                    else if (!string.IsNullOrEmpty(link.FullPath))
                        Native.OS.OpenWithDefaultEditor(link.FullPath);
                    break;
            }
        }

        private void ScrollToAnchor(string anchor)
        {
            if (string.IsNullOrEmpty(anchor) || !_anchors.TryGetValue(anchor, out var target))
                return;

            var point = target.TranslatePoint(new Point(0, 0), _host);
            if (point.HasValue)
                _scroll.Offset = new Vector(_scroll.Offset.X, Math.Max(0, point.Value.Y - 12));
        }

        private readonly ScrollViewer _scroll;
        private readonly StackPanel _host;
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<string, Control> _anchors = new(StringComparer.Ordinal);
        private bool _renderPending = false;
        private int _renderGeneration = 0;
        private const int MaxSourceLength = 2 * 1024 * 1024;
    }
}
