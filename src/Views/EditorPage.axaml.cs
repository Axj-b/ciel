using System;
using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class EditorPage : UserControl
    {
        public EditorPage()
        {
            InitializeComponent();
            AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
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
                     nameof(ViewModels.EditorPage.IsRenderMode))
            {
                ApplyViewMode();
            }
        }

        private void UpdateDocument(ViewModels.FileEditorTab oldTab, ViewModels.FileEditorTab newTab)
        {
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

            Editor.IsVisible = _vm.HasSelectedTab && mode != ViewModels.MarkdownViewMode.Render;
            EditorSplitter.IsVisible = mode == ViewModels.MarkdownViewMode.Split;
            Preview.IsVisible = _vm.HasSelectedTab && mode != ViewModels.MarkdownViewMode.Source;
            Preview.IsPreviewActive = Preview.IsVisible;
        }

        private async void OnPreviewOpenFileRequested(string path)
        {
            if (_vm == null || string.IsNullOrEmpty(path))
                return;

            await _vm.OpenFileAndRevealAsync(path);
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
            if (e.KeyModifiers == (cmd | KeyModifiers.Shift) && e.Key == Key.S)
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
    }
}
