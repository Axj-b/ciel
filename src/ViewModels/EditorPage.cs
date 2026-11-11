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
                    OnPropertyChanged(nameof(IsSelectedTabMarkdown));
                    RaiseViewModeProperties();
                }
            }
        }

        public bool HasSelectedTab => _selectedTab != null;

        public bool IsSelectedTabReadOnly => _selectedTab?.IsReadOnly == true;

        public bool IsSelectedTabMarkdown => _selectedTab?.IsMarkdown == true;

        /// <summary>
        ///     Markdown files default to the rendered view. The last mode selected by the user is
        ///     remembered for the lifetime of the process, not persisted across restarts.
        /// </summary>
        public MarkdownViewMode ViewMode
        {
            get => IsSelectedTabMarkdown ? s_sessionMarkdownMode : MarkdownViewMode.Source;
            set
            {
                if (!IsSelectedTabMarkdown || s_sessionMarkdownMode == value)
                    return;

                s_sessionMarkdownMode = value;
                RaiseViewModeProperties();
            }
        }

        public bool IsSourceMode => ViewMode == MarkdownViewMode.Source;

        public bool IsSplitMode => ViewMode == MarkdownViewMode.Split;

        public bool IsRenderMode => ViewMode == MarkdownViewMode.Render;

        public bool IsInlineMode => ViewMode == MarkdownViewMode.Inline;

        public bool IsEditorVisible => HasSelectedTab &&
                                       ViewMode != MarkdownViewMode.Render &&
                                       ViewMode != MarkdownViewMode.Inline;

        public bool IsPreviewVisible => HasSelectedTab && ViewMode != MarkdownViewMode.Source;

        public bool IsSplitVisible => HasSelectedTab && ViewMode == MarkdownViewMode.Split;

        public string ViewModeToolTip => ViewMode switch
        {
            MarkdownViewMode.Source => App.Text("Editor.View.Source"),
            MarkdownViewMode.Split => App.Text("Editor.View.Split"),
            MarkdownViewMode.Inline => App.Text("Editor.View.Inline"),
            _ => App.Text("Editor.View.Rendered"),
        };

        /// <summary>Cycles markdown view modes: rendered -> edit in place -> split -> source -> rendered.</summary>
        public void CycleViewMode()
        {
            if (!IsSelectedTabMarkdown)
                return;

            ViewMode = ViewMode switch
            {
                MarkdownViewMode.Render => MarkdownViewMode.Inline,
                MarkdownViewMode.Inline => MarkdownViewMode.Split,
                MarkdownViewMode.Split => MarkdownViewMode.Source,
                _ => MarkdownViewMode.Render,
            };
        }

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

        /// <summary>
        ///     Whether files matched by <c>.gitignore</c> are listed as well. Remembered per repository;
        ///     ignored trees can be large, so this is off by default.
        /// </summary>
        public bool ShowIgnoredFiles
        {
            get => _repo.UIStates.ShowIgnoredFilesInEditor;
            set
            {
                if (value == _repo.UIStates.ShowIgnoredFilesInEditor)
                    return;

                _repo.UIStates.ShowIgnoredFilesInEditor = value;
                OnPropertyChanged();
                _ = RefreshFilesAsync();
            }
        }

        public event Action<string> BinaryFileRequested;

        public void Notify(string message, bool isError = false)
        {
            _repo.SendNotification(message, isError);
        }

        public async Task EnsureFilesLoadedAsync()
        {
            if (_loaded)
                return;

            await (_loadingTask ??= LoadFilesAsync()).ConfigureAwait(true);
        }

        public async Task RefreshFilesAsync()
        {
            var running = _loadingTask;
            if (running != null)
            {
                try
                {
                    await running.ConfigureAwait(true);
                }
                catch
                {
                    // A failed load must not block the next one.
                }
            }

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

        /// <summary>Opens the name popup to create a file or folder below the given row (or at the repo root).</summary>
        public void BeginCreateItem(EditorFileTreeNode node, bool isFolder)
        {
            if (!_repo.CanCreatePopup())
                return;

            _repo.ShowPopup(new EditorItemName(this, node,
                isFolder ? EditorItemNameMode.NewFolder : EditorItemNameMode.NewFile));
        }

        public void BeginRenameItem(EditorFileTreeNode node)
        {
            if (node == null || !_repo.CanCreatePopup())
                return;

            _repo.ShowPopup(new EditorItemName(this, node, EditorItemNameMode.Rename));
        }

        /// <summary>
        ///     Creates a file or folder on disk. Returns an error message on failure, or null on success.
        ///     Called by the name popup once the user confirmed.
        /// </summary>
        public async Task<string> CreateItemAsync(string folder, string name, bool isFolder)
        {
            var relative = string.IsNullOrEmpty(folder) ? name : folder + "/" + name;
            var full = Native.OS.GetAbsPath(_repo.FullPath, relative);

            if (File.Exists(full) || Directory.Exists(full))
                return App.Text("Editor.NameExists", relative);

            try
            {
                if (isFolder)
                {
                    Directory.CreateDirectory(full);
                    _expandedFolders.Add(relative);

                    // Git does not track empty folders, so remember it for this page. Otherwise the
                    // folder would be created on disk but never show up in the list.
                    _emptyFolders.Add(relative);
                }
                else
                {
                    var parent = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);

                    File.WriteAllText(full, string.Empty);
                }
            }
            catch (Exception e)
            {
                return App.Text("Editor.CreateFailed", relative, e.Message);
            }

            await RefreshFilesAsync().ConfigureAwait(true);
            if (isFolder)
            {
                ExpandAncestors(relative);
                RebuildTree();
                SelectRow(relative, true);
            }
            else
            {
                await OpenFileAndRevealAsync(relative).ConfigureAwait(true);
            }

            return null;
        }

        /// <summary>Renames a file or folder on disk. Returns an error message on failure, or null on success.</summary>
        public async Task<string> RenameItemAsync(EditorFileTreeNode node, string newName)
        {
            if (node == null)
                return null;

            var oldRelative = node.Path;
            var parent = GetParentFolder(oldRelative);
            var newRelative = string.IsNullOrEmpty(parent) ? newName : parent + "/" + newName;
            if (oldRelative.Equals(newRelative, StringComparison.Ordinal))
                return null;

            var affected = Tabs.Where(t => IsPathUnder(t.Path, oldRelative)).ToList();
            if (affected.Any(t => t.IsDirty))
                return App.Text("Editor.DirtyTabs", oldRelative);

            var oldFull = Native.OS.GetAbsPath(_repo.FullPath, oldRelative);
            var newFull = Native.OS.GetAbsPath(_repo.FullPath, newRelative);

            try
            {
                MovePath(oldFull, newFull, node.IsFolder);
            }
            catch (Exception e)
            {
                return App.Text("Editor.RenameFailed", oldRelative, e.Message);
            }

            foreach (var tab in affected)
                tab.UpdatePath(_repo.FullPath, newRelative + tab.Path[oldRelative.Length..]);

            if (node.IsFolder)
            {
                var expanded = _expandedFolders.Where(x => IsPathUnder(x, oldRelative)).ToList();
                foreach (var path in expanded)
                {
                    _expandedFolders.Remove(path);
                    _expandedFolders.Add(newRelative + path[oldRelative.Length..]);
                }

                foreach (var path in _emptyFolders.Where(x => IsPathUnder(x, oldRelative)).ToList())
                {
                    _emptyFolders.Remove(path);
                    _emptyFolders.Add(newRelative + path[oldRelative.Length..]);
                }
            }

            await RefreshFilesAsync().ConfigureAwait(true);
            SelectRow(newRelative, node.IsFolder);
            return null;
        }

        /// <summary>Deletes a file or folder after confirmation. Open tabs below the item are closed.</summary>
        public async Task DeleteItemAsync(EditorFileTreeNode node)
        {
            if (node == null)
                return;

            var affected = Tabs.Where(t => IsPathUnder(t.Path, node.Path)).ToList();
            if (affected.Any(t => t.IsDirty))
            {
                _repo.SendNotification(App.Text("Editor.DirtyTabs", node.Path), true);
                return;
            }

            if (!await App.AskConfirmAsync(App.Text("Editor.DeleteConfirm", node.Path)).ConfigureAwait(true))
                return;

            var full = Native.OS.GetAbsPath(_repo.FullPath, node.Path);
            try
            {
                if (node.IsFolder)
                    Directory.Delete(full, true);
                else
                    File.Delete(full);
            }
            catch (Exception e)
            {
                _repo.SendNotification(App.Text("Editor.DeleteFailed", node.Path, e.Message), true);
                return;
            }

            foreach (var tab in affected)
                RemoveTab(tab);

            _emptyFolders.RemoveWhere(x => IsPathUnder(x, node.Path));
            await RefreshFilesAsync().ConfigureAwait(true);
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
                var includeIgnored = _repo.UIStates.ShowIgnoredFilesInEditor;
                var (files, ignored) = await new Commands.QueryWorkingTreeFiles(_repo.FullPath, includeIgnored)
                    .GetResultAsync()
                    .ConfigureAwait(false);
                if (_closed)
                    return;

                // File.Exists on every entry can be slow for large repositories, so keep it off
                // the UI thread together with sorting. Ignored entries come straight from Git's
                // untracked listing, so they do not need the extra probe (ignored trees can be huge).
                var existing = await Task.Run(() =>
                {
                    var result = new List<string>(files.Count);
                    foreach (var path in files)
                    {
                        if (ignored.Contains(path))
                        {
                            result.Add(path);
                            continue;
                        }

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
                _ignoredFiles = ignored;
                _emptyFolders.RemoveWhere(x => !Directory.Exists(Native.OS.GetAbsPath(_repo.FullPath, x)));
                _loaded = true;

                RefreshVisibleFiles();
                RebuildTree();
            }
            finally
            {
                IsRefreshingFiles = false;
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
                            IsIgnored = _ignoredFiles.Contains(file),
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

            if (string.IsNullOrEmpty(_filter))
                AddEmptyFolders(folders);

            SortChildren(_tree);
            MarkIgnoredChildren(_tree);
            RebuildRows();
        }

        /// <summary>Adds folders the user created during this session that do not contain any file yet.</summary>
        private void AddEmptyFolders(Dictionary<string, EditorFileTreeNode> folders)
        {
            foreach (var path in _emptyFolders)
            {
                var parts = path.Split('/');
                var parent = _tree;
                var prefix = string.Empty;

                foreach (var name in parts)
                {
                    var full = prefix.Length == 0 ? name : prefix + "/" + name;
                    if (!folders.TryGetValue(full, out var folder))
                    {
                        folder = new EditorFileTreeNode
                        {
                            Name = name,
                            Path = full,
                            IsFolder = true,
                            IsExpanded = _expandedFolders.Contains(full),
                        };
                        folders.Add(full, folder);
                        parent.Children.Add(folder);
                    }

                    parent = folder;
                    prefix = full;
                }
            }
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

        /// <summary>A folder is treated as ignored when every file below it is ignored.</summary>
        private static bool MarkIgnoredChildren(EditorFileTreeNode node)
        {
            if (!node.IsFolder)
                return node.IsIgnored;

            var all = node.Children.Count > 0;
            foreach (var child in node.Children)
                all &= MarkIgnoredChildren(child);

            node.IsIgnored = all;
            return all;
        }

        private void SelectRow(string path, bool isFolder)
        {
            SelectedRow = Rows.FirstOrDefault(x =>
                x.IsFolder == isFolder && x.Path.Equals(path, StringComparison.Ordinal));
        }

        private static string GetParentFolder(string path)
        {
            var index = path.LastIndexOf('/');
            return index < 0 ? string.Empty : path[..index];
        }

        private static bool IsPathUnder(string path, string root)
        {
            return path.Equals(root, StringComparison.Ordinal) ||
                   path.StartsWith(root + "/", StringComparison.Ordinal);
        }

        /// <summary>
        ///     Moves a file or folder. A pure case change goes through a temporary name because
        ///     case-insensitive file systems treat the source and target as the same entry.
        /// </summary>
        private static void MovePath(string oldFullPath, string newFullPath, bool isFolder)
        {
            if (oldFullPath.Equals(newFullPath, StringComparison.OrdinalIgnoreCase) &&
                !oldFullPath.Equals(newFullPath, StringComparison.Ordinal))
            {
                var temp = oldFullPath + ".sourcegit-rename-" + Guid.NewGuid().ToString("N");
                if (isFolder)
                {
                    Directory.Move(oldFullPath, temp);
                    Directory.Move(temp, newFullPath);
                }
                else
                {
                    File.Move(oldFullPath, temp);
                    File.Move(temp, newFullPath);
                }

                return;
            }

            if (isFolder)
                Directory.Move(oldFullPath, newFullPath);
            else
                File.Move(oldFullPath, newFullPath);
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

        private void RaiseViewModeProperties()
        {
            OnPropertyChanged(nameof(ViewMode));
            OnPropertyChanged(nameof(IsSourceMode));
            OnPropertyChanged(nameof(IsSplitMode));
            OnPropertyChanged(nameof(IsRenderMode));
            OnPropertyChanged(nameof(IsInlineMode));
            OnPropertyChanged(nameof(IsEditorVisible));
            OnPropertyChanged(nameof(IsPreviewVisible));
            OnPropertyChanged(nameof(IsSplitVisible));
            OnPropertyChanged(nameof(ViewModeToolTip));
        }

        private readonly Repository _repo;
        private EditorFileTreeNode _tree = new() { IsFolder = true, IsExpanded = true };
        private readonly HashSet<string> _expandedFolders = new(StringComparer.Ordinal);
        private readonly HashSet<string> _emptyFolders = new(StringComparer.Ordinal);
        private List<string> _allFiles = [];
        private List<string> _visibleFiles = [];
        private HashSet<string> _ignoredFiles = new(StringComparer.Ordinal);
        private FileEditorTab _selectedTab = null;
        private EditorFileTreeNode _selectedRow = null;
        private string _filter = string.Empty;
        private bool _isRefreshingFiles = false;
        private bool _isWordWrapEnabled = false;
        private bool _loaded = false;
        private bool _closed = false;
        private bool _refreshPending = false;
        private Task _loadingTask = null;
        private static MarkdownViewMode s_sessionMarkdownMode = MarkdownViewMode.Render;
    }
}
