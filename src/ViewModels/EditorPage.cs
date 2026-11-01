using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Collections;

using CommunityToolkit.Mvvm.ComponentModel;

using SourceGit.Models;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     Repository wide file editor. Lists the complete working tree, keeps one tab per open file
    ///     and saves files back to disk. Staging and committing stay in the regular Git views.
    /// </summary>
    public class EditorPage : ObservableObject
    {
        public EditorPage(Repository repo)
        {
            _repo = repo;
        }

        public string RepositoryPath => _repo.FullPath;

        public AvaloniaList<EditorFileTreeNode> Rows { get; } = [];

        public AvaloniaList<FileEditorTab> Tabs { get; } = [];

        public FileEditorTab SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (ReferenceEquals(_selectedTab, value))
                    return;

                if (_selectedTab != null)
                    _selectedTab.PropertyChanged -= OnSelectedTabPropertyChanged;

                if (SetProperty(ref _selectedTab, value))
                {
                    if (_selectedTab != null)
                        _selectedTab.PropertyChanged += OnSelectedTabPropertyChanged;

                    OnPropertyChanged(nameof(HasSelectedTab));
                    OnPropertyChanged(nameof(IsSelectedTabReadOnly));
                }
            }
        }

        public bool HasSelectedTab => _selectedTab != null;

        public bool IsSelectedTabReadOnly => _selectedTab?.IsReadOnly == true;

        public EditorFileTreeNode SelectedRow
        {
            get => _selectedRow;
            set => SetProperty(ref _selectedRow, value);
        }

        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value))
                {
                    RefreshVisibleFiles();
                    RebuildTree();
                }
            }
        }

        public bool IsRefreshingFiles
        {
            get => _isRefreshingFiles;
            private set => SetProperty(ref _isRefreshingFiles, value);
        }

        public bool IsWordWrapEnabled
        {
            get => _isWordWrapEnabled;
            set => SetProperty(ref _isWordWrapEnabled, value);
        }

        public event Action<string> BinaryFileRequested;

        public async Task EnsureFilesLoadedAsync()
        {
            if (_loaded)
                return;

            await (_loadingTask ??= LoadFilesAsync()).ConfigureAwait(true);
        }

        public async Task RefreshFilesAsync()
        {
            if (_loadingTask == null)
                _loadingTask = LoadFilesAsync();

            await _loadingTask.ConfigureAwait(true);
        }

        public async Task OpenFileAsync(string path)
        {
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path))
                return;

            var existing = Tabs.FirstOrDefault(t => t.Path.Equals(path, StringComparison.Ordinal));
            if (existing != null)
            {
                SelectedTab = existing;
                return;
            }

            var file = await EditableTextFile.LoadAsync(_repo.FullPath, path).ConfigureAwait(true);
            switch (file.Status)
            {
                case Models.TextFileLoadStatus.Binary:
                    BinaryFileRequested?.Invoke(path);
                    return;
                case Models.TextFileLoadStatus.TooLarge:
                    _repo.SendNotification(App.Text("Editor.FileTooLarge", path), true);
                    return;
                case Models.TextFileLoadStatus.NotFound:
                    _repo.SendNotification(App.Text("Editor.FileNotFound", path), true);
                    await RefreshFilesAsync();
                    return;
                case Models.TextFileLoadStatus.Failed:
                    _repo.SendNotification(App.Text("Editor.LoadFailed", path, file.Error), true);
                    return;
            }

            var tab = new FileEditorTab(file);
            Tabs.Add(tab);
            SelectedTab = tab;
        }

        public async Task OpenFileAndRevealAsync(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            await EnsureFilesLoadedAsync().ConfigureAwait(true);

            if (!string.IsNullOrEmpty(_filter))
            {
                _filter = string.Empty;
                OnPropertyChanged(nameof(Filter));
            }

            ExpandAncestors(path);
            RefreshVisibleFiles();
            RebuildTree();

            var row = Rows.FirstOrDefault(x => !x.IsFolder && x.Path.Equals(path, StringComparison.Ordinal));
            if (row != null)
                SelectedRow = row;

            await OpenFileAsync(path).ConfigureAwait(true);
        }

        public void ToggleNode(EditorFileTreeNode node)
        {
            if (node is not { IsFolder: true })
                return;

            node.IsExpanded = !node.IsExpanded;
            if (node.IsExpanded)
                _expandedFolders.Add(node.Path);
            else
                _expandedFolders.Remove(node.Path);

            RebuildRows();
        }

        public async Task SaveAsync(FileEditorTab tab)
        {
            if (tab == null)
                return;

            if (tab.IsReadOnly)
            {
                _repo.SendNotification(App.Text("Editor.ReadOnlyFile", tab.Path), true);
                return;
            }

            if (tab.IsDirty && tab.File.HasChangedOnDisk() &&
                !await App.AskConfirmAsync(App.Text("Editor.OverwriteExternalChanges", tab.Name)))
                return;

            try
            {
                await tab.SaveAsync().ConfigureAwait(true);
                _repo.MarkWorkingCopyDirtyManually();
            }
            catch (Exception e)
            {
                _repo.SendNotification(App.Text("Editor.SaveFailed", tab.Name, e.Message), true);
            }
        }

        public async Task SaveAllAsync()
        {
            foreach (var tab in Tabs.Where(x => x.IsDirty).ToList())
                await SaveAsync(tab).ConfigureAwait(true);
        }

        public async Task ReloadAsync(FileEditorTab tab)
        {
            if (tab == null)
                return;

            if (tab.IsDirty && !await App.AskConfirmAsync(App.Text("Editor.DiscardChanges", tab.Name)))
                return;

            var file = await tab.ReloadAsync(_repo.FullPath).ConfigureAwait(true);
            if (file.Status == Models.TextFileLoadStatus.Success)
                return;

            if (file.Status == Models.TextFileLoadStatus.Binary)
                BinaryFileRequested?.Invoke(tab.Path);
            else
                _repo.SendNotification(App.Text("Editor.LoadFailed", tab.Path, file.Error), true);
        }

        public async Task CloseTabAsync(FileEditorTab tab)
        {
            if (tab == null)
                return;

            if (tab.IsDirty && !await App.AskConfirmAsync(App.Text("Editor.UnsavedChanges", tab.Name)))
                return;

            RemoveTab(tab);
        }

        /// <summary>Called by the watcher whenever the working copy changed. Debounced.</summary>
        public async void OnWorkingCopyChanged()
        {
            if (!_loaded || _closed || _refreshPending)
                return;

            _refreshPending = true;
            try
            {
                await Task.Delay(700).ConfigureAwait(true);
                if (!_closed)
                    await RefreshFilesAsync().ConfigureAwait(true);
            }
            finally
            {
                _refreshPending = false;
            }
        }

        public void Cleanup()
        {
            _closed = true;
            foreach (var tab in Tabs)
                tab.Cleanup();
        }

        private async Task LoadFilesAsync()
        {
            IsRefreshingFiles = true;
            try
            {
                var files = await new Commands.QueryWorkingTreeFiles(_repo.FullPath).GetResultAsync().ConfigureAwait(false);
                if (_closed)
                    return;

                // File.Exists on every entry can be slow for large repositories, so keep it off
                // the UI thread together with sorting.
                var existing = await Task.Run(() =>
                {
                    var result = new List<string>(files.Count);
                    foreach (var path in files)
                    {
                        var full = Path.Combine(_repo.FullPath, path.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(full))
                            result.Add(path);
                    }

                    result.Sort(Models.NumericSort.Compare);
                    return result;
                }).ConfigureAwait(true);

                if (_closed)
                    return;

                _allFiles = existing;
                _loaded = true;

                RefreshVisibleFiles();
                RebuildTree();
            }
            finally
            {
                IsRefreshingFiles = false;
                _loadingTask = null;
            }
        }

        private void OnSelectedTabPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FileEditorTab.IsReadOnly))
                OnPropertyChanged(nameof(IsSelectedTabReadOnly));
        }

        private void RefreshVisibleFiles()
        {
            if (string.IsNullOrEmpty(_filter))
            {
                _visibleFiles = _allFiles;
                return;
            }

            _visibleFiles = [];
            foreach (var file in _allFiles)
            {
                if (file.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                    _visibleFiles.Add(file);
            }
        }

        private void RebuildTree()
        {
            _tree = new EditorFileTreeNode { IsFolder = true, IsExpanded = true };
            var expandAll = !string.IsNullOrEmpty(_filter);
            var folders = new Dictionary<string, EditorFileTreeNode>(StringComparer.Ordinal);

            foreach (var file in _visibleFiles)
            {
                var parts = file.Split('/');
                var parent = _tree;
                var prefix = string.Empty;

                for (var i = 0; i < parts.Length; i++)
                {
                    var name = parts[i];
                    var path = prefix.Length == 0 ? name : prefix + "/" + name;
                    if (i == parts.Length - 1)
                    {
                        parent.Children.Add(new EditorFileTreeNode
                        {
                            Name = name,
                            Path = file,
                            IsFolder = false,
                        });
                    }
                    else
                    {
                        if (!folders.TryGetValue(path, out var folder))
                        {
                            folder = new EditorFileTreeNode
                            {
                                Name = name,
                                Path = path,
                                IsFolder = true,
                                IsExpanded = expandAll || _expandedFolders.Contains(path),
                            };
                            folders.Add(path, folder);
                            parent.Children.Add(folder);
                        }

                        parent = folder;
                    }

                    prefix = path;
                }
            }

            SortChildren(_tree);
            RebuildRows();
        }

        private void RebuildRows()
        {
            var selectedPath = _selectedRow?.Path;
            var rows = new List<EditorFileTreeNode>();
            AddRows(rows, _tree, 0);

            Rows.Clear();
            Rows.AddRange(rows);

            if (string.IsNullOrEmpty(selectedPath))
                return;

            var selected = rows.FirstOrDefault(x => !x.IsFolder && x.Path.Equals(selectedPath, StringComparison.Ordinal)) ??
                           rows.FirstOrDefault(x => x.IsFolder && x.Path.Equals(selectedPath, StringComparison.Ordinal));
            if (selected != null)
                SelectedRow = selected;
        }

        private static void AddRows(List<EditorFileTreeNode> rows, EditorFileTreeNode parent, int depth)
        {
            foreach (var node in parent.Children)
            {
                node.Depth = depth;
                rows.Add(node);

                if (node.IsFolder && node.IsExpanded)
                    AddRows(rows, node, depth + 1);
            }
        }

        private static void SortChildren(EditorFileTreeNode parent)
        {
            parent.Children.Sort((l, r) =>
            {
                if (l.IsFolder == r.IsFolder)
                    return Models.NumericSort.Compare(l.Name, r.Name);

                return l.IsFolder ? -1 : 1;
            });

            foreach (var child in parent.Children)
            {
                if (child.IsFolder)
                    SortChildren(child);
            }
        }

        private void ExpandAncestors(string path)
        {
            var parts = path.Split('/');
            var prefix = string.Empty;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                prefix = prefix.Length == 0 ? parts[i] : prefix + "/" + parts[i];
                _expandedFolders.Add(prefix);
            }
        }

        private void RemoveTab(FileEditorTab tab)
        {
            var index = Tabs.IndexOf(tab);
            if (index < 0)
                return;

            tab.Cleanup();
            Tabs.RemoveAt(index);

            if (Tabs.Count == 0)
                SelectedTab = null;
            else
                SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
        }

        private readonly Repository _repo;
        private EditorFileTreeNode _tree = new() { IsFolder = true, IsExpanded = true };
        private readonly HashSet<string> _expandedFolders = new(StringComparer.Ordinal);
        private List<string> _allFiles = [];
        private List<string> _visibleFiles = [];
        private FileEditorTab _selectedTab = null;
        private EditorFileTreeNode _selectedRow = null;
        private string _filter = string.Empty;
        private bool _isRefreshingFiles = false;
        private bool _isWordWrapEnabled = false;
        private bool _loaded = false;
        private bool _closed = false;
        private bool _refreshPending = false;
        private Task _loadingTask = null;
    }
}
