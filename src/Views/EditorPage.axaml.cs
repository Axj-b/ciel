using System;
using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class EditorPage : UserControl
    {
        public EditorPage()
        {
            InitializeComponent();
            AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

            _peekTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(180), DispatcherPriority.Background, OnPeekTimer);
            _peekTimer.Stop();

            ContentArea.SizeChanged += OnContentAreaSizeChanged;
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            Attach();
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);
            Detach();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            Attach();
        }

        protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == IsVisibleProperty && IsVisible && _vm != null)
            {
                _ = _vm.EnsureFilesLoadedAsync();
                if (_vm.HasSelectedTab)
                    FocusEditor();
            }
        }

        private void Attach()
        {
            var vm = DataContext as ViewModels.EditorPage;
            if (ReferenceEquals(vm, _vm) && _attached)
                return;

            Detach();

            _vm = vm;
            if (_vm == null)
                return;

            _vm.PropertyChanged += OnViewModelPropertyChanged;
            _vm.BinaryFileRequested += OnBinaryFileRequested;
            _attached = true;

            Preview.RepositoryPath = _vm.RepositoryPath;
            Preview.OpenFileRequested += OnPreviewOpenFileRequested;
            Preview.InlineEditCommitted += OnInlineEditCommitted;

            Editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
            Editor.TextArea.TextView.ScrollOffsetChanged += OnEditorScrollOffsetChanged;

            Editor.WordWrap = _vm.IsWordWrapEnabled;
            UpdateDocument(null, _vm.SelectedTab);
            ApplyViewMode();

            if (IsVisible)
                _ = _vm.EnsureFilesLoadedAsync();
        }

        private void Detach()
        {
            if (!_attached || _vm == null)
                return;

            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.BinaryFileRequested -= OnBinaryFileRequested;
            Preview.OpenFileRequested -= OnPreviewOpenFileRequested;
            Preview.InlineEditCommitted -= OnInlineEditCommitted;

            Editor.TextArea.Caret.PositionChanged -= OnCaretPositionChanged;
            Editor.TextArea.TextView.ScrollOffsetChanged -= OnEditorScrollOffsetChanged;

            ClosePeek();
            _attached = false;
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModels.EditorPage.SelectedTab))
            {
                UpdateDocument(_lastTab, _vm.SelectedTab);
            }
            else if (e.PropertyName == nameof(ViewModels.EditorPage.IsWordWrapEnabled) && _vm != null)
            {
                Editor.WordWrap = _vm.IsWordWrapEnabled;
            }
            else if (e.PropertyName is nameof(ViewModels.EditorPage.ViewMode) or
                     nameof(ViewModels.EditorPage.HasSelectedTab) or
                     nameof(ViewModels.EditorPage.IsSourceMode) or
                     nameof(ViewModels.EditorPage.IsSplitMode) or
                     nameof(ViewModels.EditorPage.IsRenderMode) or
                     nameof(ViewModels.EditorPage.IsInlineMode))
            {
                ApplyViewMode();
            }
        }

        private void UpdateDocument(ViewModels.FileEditorTab oldTab, ViewModels.FileEditorTab newTab)
        {
            ClosePeek();
            Preview.CancelInlineEdit();

            if (oldTab != null && ReferenceEquals(Editor.Document, oldTab.Document))
                oldTab.CaretOffset = Editor.CaretOffset;

            if (_lastTab != null)
                _lastTab.Document.TextChanged -= OnDocumentTextChanged;

            _lastTab = newTab;

            if (newTab == null)
            {
                Editor.FileName = string.Empty;
                Editor.IsReadOnly = false;
                Editor.Document = new AvaloniaEdit.Document.TextDocument();
                Preview.Source = string.Empty;
                Preview.FileName = string.Empty;
                ApplyViewMode();
                return;
            }

            Editor.FileName = newTab.Path;
            Editor.IsReadOnly = newTab.IsReadOnly;
            Editor.Document = newTab.Document;
            Editor.CaretOffset = Math.Min(newTab.CaretOffset, newTab.Document.TextLength);
            Editor.TextArea.Caret.BringCaretToView();

            newTab.Document.TextChanged += OnDocumentTextChanged;
            Preview.Source = newTab.Document.Text;
            Preview.FileName = newTab.Path;
            Preview.RepositoryPath = _vm?.RepositoryPath ?? string.Empty;
            ApplyViewMode();

            if (IsVisible)
                FocusEditor();
        }

        private void OnDocumentTextChanged(object sender, EventArgs e)
        {
            if (_vm?.SelectedTab is { IsMarkdown: true } tab)
                Preview.Source = tab.Document.Text;

            if (_peekVisible)
                QueuePeekUpdate();
        }

        private void ApplyViewMode()
        {
            if (_vm == null)
                return;

            var mode = _vm.HasSelectedTab ? _vm.ViewMode : ViewModels.MarkdownViewMode.Source;
            var columns = EditorPanel.ColumnDefinitions;

            switch (mode)
            {
                case ViewModels.MarkdownViewMode.Render:
                case ViewModels.MarkdownViewMode.Inline:
                    columns[0].Width = new GridLength(0);
                    columns[1].Width = new GridLength(0);
                    columns[2].Width = new GridLength(1, GridUnitType.Star);
                    break;
                case ViewModels.MarkdownViewMode.Split:
                    columns[0].Width = new GridLength(1, GridUnitType.Star);
                    columns[1].Width = new GridLength(4);
                    columns[2].Width = new GridLength(1, GridUnitType.Star);
                    break;
                default:
                    columns[0].Width = new GridLength(1, GridUnitType.Star);
                    columns[1].Width = new GridLength(0);
                    columns[2].Width = new GridLength(0);
                    break;
            }

            Editor.IsVisible = _vm.HasSelectedTab && mode != ViewModels.MarkdownViewMode.Render &&
                               mode != ViewModels.MarkdownViewMode.Inline;
            EditorSplitter.IsVisible = mode == ViewModels.MarkdownViewMode.Split;
            Preview.IsVisible = _vm.HasSelectedTab && mode != ViewModels.MarkdownViewMode.Source;
            Preview.IsPreviewActive = Preview.IsVisible;
            Preview.IsInlineEditing = Preview.IsVisible && mode == ViewModels.MarkdownViewMode.Inline &&
                                      !_vm.IsSelectedTabReadOnly;

            if (mode != ViewModels.MarkdownViewMode.Source && mode != ViewModels.MarkdownViewMode.Split)
                ClosePeek();
        }

        private async void OnPreviewOpenFileRequested(string path)
        {
            if (_vm == null || string.IsNullOrEmpty(path))
                return;

            await _vm.OpenFileAndRevealAsync(path);
        }

        private void OnInlineEditCommitted(int start, int length, string text)
        {
            var tab = _vm?.SelectedTab;
            if (tab == null || tab.IsReadOnly || !tab.IsMarkdown)
                return;

            var document = tab.Document;
            start = Math.Clamp(start, 0, document.TextLength);
            length = Math.Clamp(length, 0, document.TextLength - start);
            document.Replace(start, length, text ?? string.Empty);
        }

        private void TogglePeek()
        {
            if (_peekVisible)
            {
                ClosePeek();
                return;
            }

            ShowPeekAtCaret(true);
        }

        private void ShowPeekAtCaret(bool updateContent)
        {
            if (_vm == null || _lastTab == null || !_lastTab.IsMarkdown)
            {
                ClosePeek();
                return;
            }

            if (_vm.ViewMode != ViewModels.MarkdownViewMode.Source &&
                _vm.ViewMode != ViewModels.MarkdownViewMode.Split)
            {
                ClosePeek();
                return;
            }

            var text = _lastTab.Document.Text;
            if (string.IsNullOrWhiteSpace(text) || text.Length > 2 * 1024 * 1024)
            {
                ClosePeek();
                return;
            }

            try
            {
                var document = Models.MarkdownRenderer.Parse(text);
                var block = FindMarkdownBlock(document, Editor.CaretOffset);
                if (block == null)
                {
                    ClosePeek();
                    return;
                }

                if (updateContent)
                {
                    PeekContent.Children.Clear();
                    var options = new Models.MarkdownRenderOptions()
                    {
                        RepositoryPath = _vm.RepositoryPath,
                        FileName = _lastTab.Path,
                        IsDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark,
                        OpenLink = OnPeekLinkActivated,
                    };

                    var control = new Models.MarkdownRenderer(options).RenderBlock(block);
                    if (control != null)
                        PeekContent.Children.Add(control);
                }

                _peekVisible = true;
                PeekLayer.IsVisible = true;
                UpdatePeekPosition();
            }
            catch (Exception e)
            {
                Native.OS.LogException(e);
                ClosePeek();
            }
        }

        private void UpdatePeekPosition()
        {
            if (!_peekVisible)
                return;

            var view = Editor.TextArea.TextView;
            var caret = Editor.TextArea.Caret.CalculateCaretRectangle();
            var origin = view.TranslatePoint(new Point(0, 0), ContentArea);
            var editorOrigin = Editor.TranslatePoint(new Point(0, 0), ContentArea);
            if (!origin.HasValue || !editorOrigin.HasValue)
                return;

            var area = ContentArea.Bounds;
            if (area.Width <= 0 || area.Height <= 0)
                return;

            var caretTop = origin.Value.Y + caret.Top;
            var caretBottom = origin.Value.Y + caret.Bottom;

            var editorWidth = Math.Max(220, Editor.Bounds.Width);
            var width = Math.Max(200, Math.Min(560, editorWidth - 24));
            var left = Math.Max(8, Math.Min(editorOrigin.Value.X + 12, area.Width - width - 8));

            var below = caretBottom + 6;
            var above = caretTop - 306;
            var isAbove = below + 140 > area.Height && above >= 8;
            var top = isAbove ? above : below;
            top = Math.Max(8, Math.Min(top, area.Height - 80));

            var available = isAbove ? caretTop - top - 12 : area.Height - top - 12;
            PeekPanel.Width = width;
            PeekScroll.MaxHeight = Math.Clamp(available - 34, 60, 260);
            Canvas.SetLeft(PeekPanel, left);
            Canvas.SetTop(PeekPanel, top);
        }

        private void QueuePeekUpdate()
        {
            _peekTimer.Stop();
            _peekTimer.Start();
        }

        private void OnPeekTimer(object sender, EventArgs e)
        {
            _peekTimer.Stop();
            ShowPeekAtCaret(true);
        }

        private void OnCaretPositionChanged(object sender, EventArgs e)
        {
            if (!_peekVisible)
                return;

            UpdatePeekPosition();
            QueuePeekUpdate();
        }

        private void OnEditorScrollOffsetChanged(object sender, EventArgs e)
        {
            if (_peekVisible)
                UpdatePeekPosition();
        }

        private void OnContentAreaSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_peekVisible)
                UpdatePeekPosition();
        }

        private void ClosePeek()
        {
            _peekTimer?.Stop();
            if (!_peekVisible)
                return;

            _peekVisible = false;
            PeekLayer.IsVisible = false;
            PeekContent.Children.Clear();
        }

        private void OnClosePeek(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            ClosePeek();
        }

        private void OnPeekLinkActivated(Models.MarkdownLink link)
        {
            if (link == null)
                return;

            switch (link.Kind)
            {
                case Models.MarkdownLinkKind.External:
                    Native.OS.OpenBrowser(link.ExternalUrl);
                    break;
                case Models.MarkdownLinkKind.File:
                    if (!string.IsNullOrEmpty(link.RelativePath))
                        _ = _vm?.OpenFileAndRevealAsync(link.RelativePath);
                    break;
            }
        }

        private static Markdig.Syntax.Block FindMarkdownBlock(Markdig.Syntax.MarkdownDocument document, int offset)
        {
            Markdig.Syntax.Block result = null;
            foreach (var block in document)
            {
                if (block is Markdig.Extensions.Footnotes.FootnoteGroup ||
                    block is Markdig.Syntax.LinkReferenceDefinitionGroup)
                    continue;

                if (offset < block.Span.Start)
                    break;

                result = block;
                if (offset <= block.Span.End)
                    break;
            }

            return result;
        }

        private void FocusEditor()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (IsVisible)
                    Editor.TextArea.Focus();
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }

        private async void OnBinaryFileRequested(string path)
        {
            if (_vm == null)
                return;

            await this.ShowDialogAsync(new ViewModels.BinaryFileViewer(_vm.RepositoryPath, path, null));
        }

        private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_vm == null)
                return;

            var cmd = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            if (e.Key == Key.F12 && e.KeyModifiers == KeyModifiers.Alt)
            {
                if (_vm.IsSelectedTabMarkdown &&
                    (_vm.IsSourceMode || _vm.IsSplitMode))
                {
                    e.Handled = true;
                    TogglePeek();
                }
            }
            else if (e.Key == Key.Escape && _peekVisible)
            {
                e.Handled = true;
                ClosePeek();
            }
            else if (e.Key is Key.Z or Key.Y && TryHandleDocumentUndo(e))
            {
                e.Handled = true;
            }
            else if (e.KeyModifiers == (cmd | KeyModifiers.Shift) && e.Key == Key.S)
            {
                e.Handled = true;
                await _vm.SaveAllAsync();
            }
            else if (e.KeyModifiers == cmd && e.Key == Key.S)
            {
                e.Handled = true;
                await _vm.SaveAsync(_vm.SelectedTab);
            }
            else if (e.KeyModifiers == cmd && e.Key == Key.F4)
            {
                e.Handled = true;
                await _vm.CloseTabAsync(_vm.SelectedTab);
            }
            else if (e.KeyModifiers == (cmd | KeyModifiers.Shift) && e.Key == Key.V)
            {
                if (_vm.IsSelectedTabMarkdown)
                {
                    e.Handled = true;
                    _vm.CycleViewMode();
                }
            }
        }

        private void OnCycleViewMode(object sender, RoutedEventArgs e)
        {
            _vm?.CycleViewMode();
            e.Handled = true;
        }

        /// <summary>
        ///     In rendered modes the text editor does not own the focus, so undo/redo shortcuts have
        ///     to be forwarded to the tab document explicitly. Text boxes (inline source editor) keep
        ///     their own undo stack.
        /// </summary>
        private bool TryHandleDocumentUndo(KeyEventArgs e)
        {
            if (_vm == null || (!_vm.IsRenderMode && !_vm.IsInlineMode))
                return false;

            var source = e.Source as Visual;
            if (source != null &&
                (source.FindAncestorOfType<TextBox>(true) != null ||
                 source.FindAncestorOfType<AvaloniaEdit.TextEditor>(true) != null))
                return false;

            var stack = _vm.SelectedTab?.Document.UndoStack;
            if (stack == null)
                return false;

            var cmd = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            if (e.Key == Key.Z && e.KeyModifiers == (cmd | KeyModifiers.Shift))
            {
                if (stack.CanRedo)
                    stack.Redo();
                return true;
            }

            if (e.Key == Key.Z && e.KeyModifiers == cmd)
            {
                if (stack.CanUndo)
                    stack.Undo();
                return true;
            }

            if (e.Key == Key.Y && e.KeyModifiers == cmd)
            {
                if (stack.CanRedo)
                    stack.Redo();
                return true;
            }

            return false;
        }

        private async void OnSave(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedTab is { } tab)
                await _vm.SaveAsync(tab);

            e.Handled = true;
        }

        private async void OnReload(object sender, RoutedEventArgs e)
        {
            if (_vm?.SelectedTab is { } tab)
                await _vm.ReloadAsync(tab);

            e.Handled = true;
        }

        private async void OnRefreshFiles(object sender, RoutedEventArgs e)
        {
            if (_vm != null)
                await _vm.RefreshFilesAsync();

            e.Handled = true;
        }

        private async void OnCloseTab(object sender, RoutedEventArgs e)
        {
            if (_vm != null && (sender as Control)?.DataContext is ViewModels.FileEditorTab tab)
                await _vm.CloseTabAsync(tab);

            e.Handled = true;
        }

        private ViewModels.EditorPage _vm = null;
        private ViewModels.FileEditorTab _lastTab = null;
        private bool _attached = false;
        private bool _peekVisible = false;
        private readonly DispatcherTimer _peekTimer;
    }
}
