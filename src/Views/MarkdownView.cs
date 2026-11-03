using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

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

        public static readonly StyledProperty<bool> IsInlineEditingProperty =
            AvaloniaProperty.Register<MarkdownView, bool>(nameof(IsInlineEditing), false);

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

        /// <summary>
        ///     When enabled, clicking a rendered block opens a small source editor right below it and
        ///     committing the text replaces exactly the markdown range that produced the block.
        /// </summary>
        public bool IsInlineEditing
        {
            get => GetValue(IsInlineEditingProperty);
            set => SetValue(IsInlineEditingProperty, value);
        }

        public event Action<string> OpenFileRequested;

        /// <summary>Raised when an inline source edit is applied: start offset, replaced length, new text.</summary>
        public event Action<int, int, string> InlineEditCommitted;

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
            Focusable = true;

            _host.AddHandler(PointerPressedEvent, OnHostPointerPressed, RoutingStrategies.Bubble, true);

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
            else if (change.Property == IsInlineEditingProperty)
            {
                if (!IsInlineEditing)
                    CloseInlineEditor();

                UpdateInlineTargets();
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
                _inlineEditor = null;
                _inlineEditorBox = null;
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
                _inlineEditor = null;
                _inlineEditorBox = null;
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
            _inlineEditor = null;
            _inlineEditorBox = null;

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

            UpdateInlineTargets();

            if (offset.Y > 0)
                Dispatcher.UIThread.Post(() => _scroll.Offset = offset, DispatcherPriority.Background);
        }

        private void UpdateInlineTargets()
        {
            foreach (var child in _host.Children)
            {
                if (child.Tag is not Models.MarkdownBlockSource)
                    continue;

                if (IsInlineEditing)
                {
                    if (!child.Classes.Contains("md_inline_target"))
                        child.Classes.Add("md_inline_target");
                }
                else
                {
                    child.Classes.Remove("md_inline_target");
                }
            }
        }

        private void OnHostPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (!IsInlineEditing || _inlineEditor != null)
                return;

            if (!e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed)
                return;

            if (e.Source is not Visual source)
                return;

            // Buttons (copy code) and scroll bars keep their own behaviour.
            if (source.FindAncestorOfType<Button>(true) != null ||
                source.FindAncestorOfType<ScrollBar>(true) != null)
                return;

            // Links open normally; Alt+Click edits the whole block instead.
            if ((e.KeyModifiers & KeyModifiers.Alt) == 0)
            {
                var text = source.FindAncestorOfType<MarkdownTextBlock>(true);
                if (text != null && text.IsLinkAt(e.GetPosition(text)))
                    return;
            }

            var current = source;
            while (current != null && current.GetVisualParent() != _host)
                current = current.GetVisualParent();

            if (current is Control block && block.Tag is Models.MarkdownBlockSource info)
                BeginInlineEdit(block, info);
        }

        private void BeginInlineEdit(Control block, Models.MarkdownBlockSource info)
        {
            var source = Source ?? string.Empty;
            var start = Math.Clamp(info.Start, 0, source.Length);
            var end = Math.Clamp(info.Start + info.Length, start, source.Length);

            // Keep trailing blank lines outside the editable range so the document layout is not
            // disturbed when the replacement is shorter than the original block.
            while (end > start && (source[end - 1] == '\n' || source[end - 1] == '\r'))
                end--;

            if (end <= start)
                return;

            var text = source.Substring(start, end - start);
            var panel = BuildInlineEditor(text);
            var index = _host.Children.IndexOf(block);
            if (index < 0)
                return;

            _inlineEditStart = start;
            _inlineEditLength = end - start;
            _inlineEditor = panel;
            _host.Children.Insert(index + 1, panel);

            Dispatcher.UIThread.Post(() =>
            {
                if (_inlineEditor == null)
                    return;

                _inlineEditorBox?.Focus();
                if (_inlineEditorBox != null)
                    _inlineEditorBox.CaretIndex = _inlineEditorBox.Text?.Length ?? 0;

                EnsureInlineEditorVisible(panel);
            }, DispatcherPriority.Loaded);
        }

        private Border BuildInlineEditor(string text)
        {
            var box = new TextBox()
            {
                Text = text,
                AcceptsReturn = true,
                TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = Math.Clamp(text.Count(x => x == '\n') * 19 + 18, 60, 300),
                MaxHeight = 320,
                Margin = new Thickness(1),
            };
            box.Classes.Add("md_inline_source");
            ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
            box.AddHandler(KeyDownEvent, OnInlineEditorKeyDown, RoutingStrategies.Tunnel);

            var title = new TextBlock() { Text = App.Text("Markdown.Edit.Title") };
            title.Classes.Add("md_inline_title");
            title.VerticalAlignment = VerticalAlignment.Center;

            var apply = new Button()
            {
                Content = App.Text("Markdown.Edit.Apply"),
                Classes = { "flat", "primary" },
                Height = 24,
                MinWidth = 64,
                Padding = new Thickness(10, 2),
                FontSize = 11,
                Margin = new Thickness(6, 0, 0, 0),
            };
            apply.Click += OnInlineEditorApply;

            var cancel = new Button()
            {
                Content = App.Text("Cancel"),
                Classes = { "flat" },
                Height = 24,
                MinWidth = 64,
                Padding = new Thickness(10, 2),
                FontSize = 11,
                Margin = new Thickness(6, 0, 0, 0),
            };
            cancel.Click += OnInlineEditorCancel;

            var header = new Grid()
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                Margin = new Thickness(10, 5, 6, 4),
            };
            header.Children.Add(title);
            Grid.SetColumn(apply, 1);
            header.Children.Add(apply);
            Grid.SetColumn(cancel, 2);
            header.Children.Add(cancel);

            var hint = new TextBlock() { Text = App.Text("Markdown.Edit.Hint") };
            hint.Classes.Add("md_inline_hint");
            hint.Margin = new Thickness(10, 0, 10, 7);

            var root = new Grid() { RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
            root.Children.Add(header);
            Grid.SetRow(box, 1);
            root.Children.Add(box);
            Grid.SetRow(hint, 2);
            root.Children.Add(hint);

            var border = new Border() { Child = root };
            border.Classes.Add("md_inline_editor");
            _inlineEditorBox = box;
            return border;
        }

        private void OnInlineEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseInlineEditor();
                return;
            }

            var cmd = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            if ((e.Key == Key.Enter || e.Key == Key.Return) && (e.KeyModifiers & cmd) == cmd)
            {
                e.Handled = true;
                CommitInlineEdit();
            }
        }

        private void OnInlineEditorApply(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            CommitInlineEdit();
        }

        private void OnInlineEditorCancel(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            CloseInlineEditor();
        }

        private void CommitInlineEdit()
        {
            if (_inlineEditor == null || _inlineEditorBox == null)
                return;

            var text = _inlineEditorBox.Text ?? string.Empty;
            var start = _inlineEditStart;
            var length = _inlineEditLength;

            CloseInlineEditor();
            InlineEditCommitted?.Invoke(start, length, text);
        }

        private void CloseInlineEditor()
        {
            if (_inlineEditor == null)
                return;

            _host.Children.Remove(_inlineEditor);
            _inlineEditor = null;
            _inlineEditorBox = null;

            // Keep keyboard shortcuts (undo in the rendered view, mode cycling) working after the
            // editor closes, because the removed text box was the focused control until now.
            Dispatcher.UIThread.Post(() =>
            {
                if (IsVisible)
                    Focus();
            }, DispatcherPriority.Loaded);
        }

        /// <summary>Dismisses an open inline editor without applying its text.</summary>
        public void CancelInlineEdit()
        {
            CloseInlineEditor();
        }

        private void EnsureInlineEditorVisible(Control panel)
        {
            var point = panel.TranslatePoint(new Point(0, 0), _host);
            if (!point.HasValue)
                return;

            var top = point.Value.Y;
            var bottom = top + panel.Bounds.Height;
            var viewTop = _scroll.Offset.Y;
            var viewBottom = viewTop + _scroll.Viewport.Height;

            if (bottom > viewBottom - 12)
                _scroll.Offset = new Vector(_scroll.Offset.X, Math.Max(0, bottom - _scroll.Viewport.Height + 24));
            else if (top < viewTop)
                _scroll.Offset = new Vector(_scroll.Offset.X, Math.Max(0, top - 16));
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
        private Border _inlineEditor = null;
        private TextBox _inlineEditorBox = null;
        private int _inlineEditStart = 0;
        private int _inlineEditLength = 0;
        private const int MaxSourceLength = 2 * 1024 * 1024;
    }
}
