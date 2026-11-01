using System;
using System.ComponentModel;

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

            Editor.WordWrap = _vm.IsWordWrapEnabled;
            UpdateDocument(null, _vm.SelectedTab);

            if (IsVisible)
                _ = _vm.EnsureFilesLoadedAsync();
        }

        private void Detach()
        {
            if (!_attached || _vm == null)
                return;

            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.BinaryFileRequested -= OnBinaryFileRequested;
            _attached = false;
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModels.EditorPage.SelectedTab))
                UpdateDocument(_lastTab, _vm.SelectedTab);
            else if (e.PropertyName == nameof(ViewModels.EditorPage.IsWordWrapEnabled) && _vm != null)
                Editor.WordWrap = _vm.IsWordWrapEnabled;
        }

        private void UpdateDocument(ViewModels.FileEditorTab oldTab, ViewModels.FileEditorTab newTab)
        {
            if (oldTab != null && ReferenceEquals(Editor.Document, oldTab.Document))
                oldTab.CaretOffset = Editor.CaretOffset;

            _lastTab = newTab;

            if (newTab == null)
            {
                Editor.FileName = string.Empty;
                Editor.IsReadOnly = false;
                Editor.Document = new AvaloniaEdit.Document.TextDocument();
                return;
            }

            Editor.FileName = newTab.Path;
            Editor.IsReadOnly = newTab.IsReadOnly;
            Editor.Document = newTab.Document;
            Editor.CaretOffset = Math.Min(newTab.CaretOffset, newTab.Document.TextLength);
            Editor.TextArea.Caret.BringCaretToView();

            if (IsVisible)
                FocusEditor();
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
