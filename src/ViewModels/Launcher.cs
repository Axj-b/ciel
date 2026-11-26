using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class Launcher : ObservableObject
    {
        /// <summary>
        /// Number of workspaces kept in memory besides the active one.
        /// </summary>
        private const int MaxParkedWorkspaces = 2;

        public string Title
        {
            get => _title;
            private set => SetProperty(ref _title, value);
        }

        public AvaloniaList<LauncherPage> Pages
        {
            get;
            private set;
        }

        public Workspace ActiveWorkspace
        {
            get => _activeWorkspace;
            private set => SetProperty(ref _activeWorkspace, value);
        }

        public LauncherPage ActivePage
        {
            get => _activePage;
            set
            {
                if (SetProperty(ref _activePage, value))
                {
                    if (value != null && IsUnopenedPage(value))
                        _ = OpenUnopenedPageAsync(value);

                    if (value?.Data is Repository repo)
                        repo.RefreshIfStale();

                    PostActivePageChanged();
                }
            }
        }

        public ICommandPalette CommandPalette
        {
            get => _commandPalette;
            set => SetProperty(ref _commandPalette, value);
        }

        public Models.Version NewVersion
        {
            get => _newVersion;
            set => SetProperty(ref _newVersion, value);
        }

        public Launcher(string startupRepo)
        {
            Models.Notification.Raised += DispatchNotification;
            _ignoreIndexChange = true;

            ActiveWorkspace = Preferences.Instance.GetActiveWorkspace();
            Pages = new AvaloniaList<LauncherPage>();
            RestoreWorkspaceTabs();

            var activeIdx = ActiveWorkspace.ActiveIdx;
            if (activeIdx > 0 && activeIdx < Pages.Count)
                ActivePage = Pages[activeIdx];
            else
                ActivePage = Pages[0];

            _ignoreIndexChange = false;

            if (!TryOpenRepositoryFromPath(startupRepo))
                PostActivePageChanged();

            StartBackgroundLoading();
        }

        public bool TryOpenRepositoryFromPath(string repo)
        {
            if (!string.IsNullOrEmpty(repo) && Directory.Exists(repo))
            {
                var isBare = new Commands.IsBareRepository(repo).GetResult();
                if (isBare)
                {
                    var node = Preferences.Instance.FindOrAddNodeByRepositoryPath(repo, null, false);
                    Welcome.Instance.Refresh();
                    OpenRepositoryInTab(node, null);
                    return true;
                }

                var test = new Commands.QueryRepositoryRootPath(repo).GetResult();
                if (test.IsSuccess && !string.IsNullOrEmpty(test.StdOut))
                {
                    var node = Preferences.Instance.FindOrAddNodeByRepositoryPath(test.StdOut.Trim(), null, false);
                    Welcome.Instance.Refresh();
                    OpenRepositoryInTab(node, null);
                    return true;
                }
                else
                {
                    if (ActivePage is not { Data: Welcome { }, Popup: null })
                        AddNewTab();

                    if (Models.SafeDirectories.IsUntrustedRepository(test.StdErr) &&
                        Models.SafeDirectories.TryGetSafeDirectoryValue(repo, test.StdErr, out var safeDirectory))
                        ActivePage.Popup = new TrustRepository(ActivePage.Node.Id, repo, test.StdErr, safeDirectory, null, false, true, 0);
                    else
                        ActivePage.Popup = new Init(ActivePage.Node.Id, repo, null, 0, test.StdErr ?? "Unknown error occurred while opening the repository.");

                    return true;
                }
            }

            return false;
        }

        public void CloseAll()
        {
            _loadCts?.Cancel();
            _ignoreIndexChange = true;

            foreach (var one in Pages)
                CloseRepositoryInTab(one, false);

            foreach (var parked in _parked)
                CloseWorkspacePages(parked.Pages);
            _parked.Clear();

            _ignoreIndexChange = false;
        }

        public void SwitchWorkspace(Workspace to)
        {
            if (to == null || to.IsActive)
                return;

            _ignoreIndexChange = true;

            var pref = Preferences.Instance;
            foreach (var w in pref.Workspaces)
                w.IsActive = false;

            var target = TakeParkedWorkspace(to);
            ParkCurrentWorkspace();

            to.IsActive = true;

            if (target != null)
            {
                Pages = target.Pages;
                OnPropertyChanged(nameof(Pages));

                foreach (var page in Pages)
                {
                    if (page.Data is Repository repo)
                        repo.Resume();
                }

                ActivePage = target.ActivePage ?? Pages[0];
            }
            else
            {
                Pages = new AvaloniaList<LauncherPage>();
                OnPropertyChanged(nameof(Pages));
                RestoreWorkspaceTabs();

                var activeIdx = to.ActiveIdx;
                if (activeIdx >= 0 && activeIdx < Pages.Count)
                    ActivePage = Pages[activeIdx];
                else
                    ActivePage = Pages[0];
            }

            _ignoreIndexChange = false;
            PostActivePageChanged();
            Preferences.Instance.Save();
            StartBackgroundLoading();
        }

        public void AddNewTab()
        {
            var page = new LauncherPage();
            Pages.Add(page);
            ActivePage = page;
        }

        public void MoveTab(LauncherPage from, LauncherPage to)
        {
            _ignoreIndexChange = true;

            var fromIdx = Pages.IndexOf(from);
            var toIdx = Pages.IndexOf(to);
            Pages.Move(fromIdx, toIdx);

            RebuildWorkspaceRepositories();

            _ignoreIndexChange = false;
            ActivePage = from;
        }

        public void GotoNextTab()
        {
            if (Pages.Count == 1)
                return;

            var activeIdx = Pages.IndexOf(_activePage);
            var nextIdx = (activeIdx + 1) % Pages.Count;
            ActivePage = Pages[nextIdx];
        }

        public void GotoPrevTab()
        {
            if (Pages.Count == 1)
                return;

            var activeIdx = Pages.IndexOf(_activePage);
            var prevIdx = activeIdx == 0 ? Pages.Count - 1 : activeIdx - 1;
            ActivePage = Pages[prevIdx];
        }

        /// <summary>
        /// Activates the tab owning the given terminal session and shows its terminal page.
        /// Used by the global sessions dashboard 'Focus' action.
        /// </summary>
        public void FocusTerminalSession(TerminalSession session)
        {
            if (session is null)
                return;

            var owner = FindWorkspaceOwningRepository(session.RepositoryPath);
            if (owner == null)
                return;

            if (owner != _activeWorkspace)
                SwitchWorkspace(owner);

            foreach (var page in Pages)
            {
                if (page.Data is Repository repo && repo.FullPath.Equals(session.RepositoryPath, StringComparison.Ordinal))
                {
                    if (!ReferenceEquals(_activePage, page))
                        ActivePage = page;

                    repo.ShowTerminalPage(session);
                    return;
                }
            }
        }

        public void CloseTab(LauncherPage page)
        {
            if (Pages.Count == 1)
            {
                var last = Pages[0];
                if (last.Data is Repository || IsUnopenedPage(last))
                {
                    _activeWorkspace.Repositories.Clear();
                    _activeWorkspace.ActiveIdx = 0;

                    if (last.Data is Repository repo)
                    {
                        if (last.Node.IsUnmanaged)
                            last.Node.SaveMinimalInfo(repo.GitDir);
                        repo.Close();
                    }

                    CancelPendingOpen(last);
                    Welcome.Instance.ClearSearchFilter();
                    last.Node = new RepositoryNode() { Id = Guid.NewGuid().ToString() };
                    last.Data = Welcome.Instance;
                    last.Popup?.Cleanup();
                    last.Popup = null;

                    PostActivePageChanged();
                    GC.Collect();
                }
                else
                {
                    App.Quit(0);
                }

                return;
            }

            page ??= _activePage;

            var removeIdx = Pages.IndexOf(page);
            var activeIdx = Pages.IndexOf(_activePage);
            if (removeIdx == activeIdx)
                ActivePage = Pages[removeIdx > 0 ? removeIdx - 1 : removeIdx + 1];

            CloseRepositoryInTab(page);
            Pages.RemoveAt(removeIdx);
            GC.Collect();
        }

        public void CloseOtherTabs()
        {
            if (Pages.Count == 1)
                return;

            _ignoreIndexChange = true;

            var id = ActivePage.Node.Id;
            foreach (var one in Pages)
            {
                if (one.Node.Id != id)
                    CloseRepositoryInTab(one);
            }

            Pages = new AvaloniaList<LauncherPage> { ActivePage };
            OnPropertyChanged(nameof(Pages));

            _activeWorkspace.ActiveIdx = 0;
            _ignoreIndexChange = false;
            GC.Collect();
        }

        public void CloseRightTabs()
        {
            _ignoreIndexChange = true;

            var endIdx = Pages.IndexOf(ActivePage);
            for (var i = Pages.Count - 1; i > endIdx; i--)
            {
                CloseRepositoryInTab(Pages[i]);
                Pages.Remove(Pages[i]);
            }

            _ignoreIndexChange = false;
            GC.Collect();
        }

        public void OpenRepositoryInTab(string repo, LauncherPage page)
        {
            var normalizedPath = repo.Replace('\\', '/').TrimEnd('/');
            var node = Preferences.Instance.FindNode(normalizedPath) ?? new RepositoryNode
            {
                Id = normalizedPath,
                Name = Path.GetFileName(normalizedPath),
                Bookmark = 0,
                IsRepository = true,
                IsUnmanaged = true
            };

            OpenRepositoryInTab(node, null);
        }

        public void OpenRepositoryInTab(RepositoryNode node, LauncherPage page)
        {
            foreach (var one in Pages)
            {
                if (one.Node.Id == node.Id)
                {
                    ActivePage = one;
                    return;
                }
            }

            var (error, isBare, gitDir) = ResolveRepositoryLocation(node.Id);
            if (error != null)
            {
                ActivePage.Notifications.Add(new Models.Notification
                {
                    Group = node.Id,
                    Message = error,
                    IsError = true,
                });
                return;
            }

            var repo = CreateRepository(node, isBare, gitDir);

            if (page == null)
            {
                if (_activePage == null || _activePage.Node.IsRepository)
                {
                    page = new LauncherPage(node, repo);
                    Pages.Add(page);
                }
                else
                {
                    page = _activePage;
                    page.Node = node;
                    page.Data = repo;
                }
            }
            else
            {
                page.Node = node;
                page.Data = repo;
            }

            RebuildWorkspaceRepositories();

            if (_activePage == page)
                PostActivePageChanged();
            else
                ActivePage = page;
        }

        public void OpenSubRepository(LauncherPage ownerPage, string fullpath)
        {
            var normalizedPath = fullpath.Replace('\\', '/').TrimEnd('/');

            // Check if the sub-repository is already open in any of the tabs
            foreach (var one in Pages)
            {
                if (one.Node.Id.Equals(normalizedPath, StringComparison.Ordinal))
                {
                    ActivePage = one;
                    return;
                }
            }

            // Make sure the target directory exists
            if (!Directory.Exists(normalizedPath))
            {
                ownerPage.Notifications.Add(new Models.Notification
                {
                    Group = ownerPage.Node.Id,
                    Message = "Repository path does NOT exist. Please check the path.",
                    IsError = true,
                });

                return;
            }

            // Check if the target directory is a valid git repository
            var gitDir = GetRepositoryGitDir(normalizedPath);
            if (string.IsNullOrEmpty(gitDir))
            {
                ownerPage.Notifications.Add(new Models.Notification
                {
                    Group = ownerPage.Node.Id,
                    Message = "Given path is not a valid git repository!",
                    IsError = true,
                });
                return;
            }

            // Get the owner repository's name and extract the pure owner name (without any prefix)
            var ownerName = ownerPage.Node.Name;
            var colonIdx = ownerName.LastIndexOf(':');
            var pureOwnerName = (colonIdx >= 0 && colonIdx < ownerName.Length - 1) ? ownerName.Substring(colonIdx + 1).Trim() : ownerName;

            // Find the sub-repository node in the preferences or create a new one if it doesn't exist
            var node = Preferences.Instance.FindNode(normalizedPath) ?? new RepositoryNode
            {
                Id = normalizedPath,
                Name = $"{pureOwnerName}: {Path.GetFileName(normalizedPath)}",
                Bookmark = 0,
                IsRepository = true,
                IsUnmanaged = true
            };
            node.LoadMinimalInfo(gitDir);

            var repo = new Repository(false, node.Id, gitDir);
            repo.Open();

            var page = new LauncherPage(node, repo);
            var idxOfOwner = Pages.IndexOf(ownerPage);
            Pages.Insert(idxOfOwner + 1, page);
            _activeWorkspace.Repositories.Insert(idxOfOwner + 1, normalizedPath);
            ActivePage = page;
        }

        private void DispatchNotification(Models.Notification notification)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Invoke(() => DispatchNotification(notification));
                return;
            }

            if (string.IsNullOrEmpty(notification.Group))
            {
                _activePage?.Notifications.Add(notification);
                return;
            }

            foreach (var page in EnumerateAllPages())
            {
                var id = page.Node.Id.Replace('\\', '/').TrimEnd('/');
                if (id.Equals(notification.Group, StringComparison.OrdinalIgnoreCase))
                {
                    page.Notifications.Add(notification);
                    return;
                }
            }

            _activePage?.Notifications.Add(notification);
        }

        /// <summary>
        /// Pages of the active workspace followed by the pages of parked workspaces.
        /// </summary>
        private IEnumerable<LauncherPage> EnumerateAllPages()
        {
            foreach (var page in Pages)
                yield return page;

            foreach (var parked in _parked)
            {
                foreach (var page in parked.Pages)
                    yield return page;
            }
        }

        private Workspace FindWorkspaceOwningRepository(string path)
        {
            foreach (var page in Pages)
            {
                if (page.Data is Repository repo && repo.FullPath.Equals(path, StringComparison.Ordinal))
                    return _activeWorkspace;
            }

            foreach (var parked in _parked)
            {
                foreach (var page in parked.Pages)
                {
                    if (page.Data is Repository repo && repo.FullPath.Equals(path, StringComparison.Ordinal))
                        return parked.Workspace;
                }
            }

            return null;
        }

        private ParkedWorkspace TakeParkedWorkspace(Workspace ws)
        {
            var idx = _parked.FindIndex(p => ReferenceEquals(p.Workspace, ws));
            if (idx < 0)
                return null;

            var parked = _parked[idx];
            _parked.RemoveAt(idx);
            return parked;
        }

        /// <summary>
        /// Keeps the current workspace's tabs and repositories in memory (suspended) and
        /// closes the least recently used parked workspace when the limit is exceeded.
        /// </summary>
        private void ParkCurrentWorkspace()
        {
            _loadCts?.Cancel();
            _loadCts = null;

            foreach (var cts in _pendingOpens.Values)
                cts.Cancel();
            _pendingOpens.Clear();

            foreach (var page in Pages)
            {
                if (page.Data is Repository repo)
                    repo.Suspend();
            }

            _parked.Add(new ParkedWorkspace(_activeWorkspace, Pages, _activePage));
            Pages = new AvaloniaList<LauncherPage>();
            OnPropertyChanged(nameof(Pages));

            while (_parked.Count > MaxParkedWorkspaces)
            {
                var evicted = _parked[0];
                _parked.RemoveAt(0);
                CloseWorkspacePages(evicted.Pages);
            }
        }

        private void CloseWorkspacePages(AvaloniaList<LauncherPage> pages)
        {
            foreach (var page in pages)
                CloseRepositoryInTab(page, false);
        }

        /// <summary>
        /// Closes the parked tabs of a workspace that is being deleted.
        /// </summary>
        public void DropParkedWorkspace(Workspace ws)
        {
            var parked = TakeParkedWorkspace(ws);
            if (parked != null)
                CloseWorkspacePages(parked.Pages);
        }

        /// <summary>
        /// Adds a repository to a workspace (possibly parked) without changing the active workspace.
        /// </summary>
        public void AddRepositoryToWorkspace(Workspace ws, string path)
        {
            var parked = _parked.Find(p => ReferenceEquals(p.Workspace, ws));
            if (parked != null)
                parked.Pages.Add(CreateUnopenedPage(path));

            ws.Repositories.Add(path);
        }


        private static string GetRepositoryGitDir(string repo)
        {
            var fullpath = Path.Combine(repo, ".git");
            if (Directory.Exists(fullpath))
            {
                if (Directory.Exists(Path.Combine(fullpath, "refs")) &&
                    Directory.Exists(Path.Combine(fullpath, "objects")) &&
                    File.Exists(Path.Combine(fullpath, "HEAD")))
                    return fullpath;

                return null;
            }

            if (File.Exists(fullpath))
            {
                var redirect = File.ReadAllText(fullpath).Trim();
                if (redirect.StartsWith("gitdir: ", StringComparison.Ordinal))
                    redirect = redirect.Substring(8);

                if (!Path.IsPathRooted(redirect))
                    redirect = Path.GetFullPath(Path.Combine(repo, redirect));

                if (Directory.Exists(redirect))
                    return redirect;

                return null;
            }

            return new Commands.QueryGitDir(repo).GetResult();
        }

        private void CloseRepositoryInTab(LauncherPage page, bool removeFromWorkspace = true)
        {
            CancelPendingOpen(page);

            if (page.Data is Repository repo)
            {
                if (removeFromWorkspace)
                    _activeWorkspace.Repositories.Remove(repo.FullPath);

                if (page.Node.IsUnmanaged)
                    page.Node.SaveMinimalInfo(repo.GitDir);

                repo.Close();
            }
            else if (removeFromWorkspace && IsUnopenedPage(page))
            {
                _activeWorkspace.Repositories.Remove(page.Node.Id);
            }

            page.Popup?.Cleanup();
            page.Popup = null;
            page.Data = null;
        }

        private void PostActivePageChanged()
        {
            if (_ignoreIndexChange)
                return;

            var activePath = GetPageRepositoryPath(_activePage);
            if (activePath != null)
                _activeWorkspace.ActiveIdx = _activeWorkspace.Repositories.IndexOf(activePath);

            var builder = new StringBuilder(512);
            builder.Append(string.IsNullOrEmpty(_activePage.Node.Name) ? "Repositories" : _activePage.Node.Name);

            var workspaces = Preferences.Instance.Workspaces;
            if (workspaces.Count == 0 || workspaces.Count > 1 || workspaces[0] != _activeWorkspace)
                builder.Append(" - ").Append(_activeWorkspace.Name);

            Title = builder.ToString();
            CommandPalette = null;
        }

        /// <summary>
        /// Fills the tab bar from the active workspace. Only the active tab is opened right away,
        /// the others are created as unopened pages and loaded in the background.
        /// </summary>
        private void RestoreWorkspaceTabs()
        {
            var repos = ActiveWorkspace.Repositories.ToArray();
            if (repos.Length == 0)
            {
                AddNewTab();
                return;
            }

            foreach (var repo in repos)
                Pages.Add(CreateUnopenedPage(repo));
        }

        private static LauncherPage CreateUnopenedPage(string repo)
        {
            var normalizedPath = repo.Replace('\\', '/').TrimEnd('/');
            var node = FindOrCreateNode(normalizedPath);

            // Unopened unmanaged nodes can only show their saved name if the minimal info is
            // read from the .git folder directly, without spawning git.
            if (node.IsUnmanaged)
            {
                var dotGit = Path.Combine(normalizedPath, ".git");
                if (Directory.Exists(dotGit))
                    node.LoadMinimalInfo(dotGit);
            }

            return new LauncherPage(node, null);
        }

        private static RepositoryNode FindOrCreateNode(string normalizedPath)
        {
            return Preferences.Instance.FindNode(normalizedPath) ?? new RepositoryNode
            {
                Id = normalizedPath,
                Name = Path.GetFileName(normalizedPath),
                Bookmark = 0,
                IsRepository = true,
                IsUnmanaged = true
            };
        }

        private static bool IsUnopenedPage(LauncherPage page)
        {
            return page.Data == null && page.Node is { IsRepository: true };
        }

        private static string GetPageRepositoryPath(LauncherPage page)
        {
            if (page == null)
                return null;

            if (page.Data is Repository repo)
                return repo.FullPath;

            return IsUnopenedPage(page) ? page.Node.Id : null;
        }

        private void RebuildWorkspaceRepositories()
        {
            _activeWorkspace.Repositories.Clear();
            foreach (var p in Pages)
            {
                var path = GetPageRepositoryPath(p);
                if (path != null)
                    _activeWorkspace.Repositories.Add(path);
            }
        }

        private static (string error, bool isBare, string gitDir) ResolveRepositoryLocation(string path)
        {
            if (!Directory.Exists(path))
                return ("Repository does NOT exist any more. Please remove it.", false, null);

            var isBare = new Commands.IsBareRepository(path).GetResult();
            var gitDir = isBare ? path : GetRepositoryGitDir(path);
            if (string.IsNullOrEmpty(gitDir))
                return ("Given path is not a valid git repository!", false, null);

            return (null, isBare, gitDir);
        }

        private static Repository CreateRepository(RepositoryNode node, bool isBare, string gitDir)
        {
            if (node.IsUnmanaged)
                node.LoadMinimalInfo(gitDir);

            var repo = new Repository(isBare, node.Id, gitDir);
            repo.Open();
            return repo;
        }

        /// <summary>
        /// Opens an unopened page. The git checks run on a worker thread; the repository itself
        /// is created on the UI thread. Results are dropped if the page was closed or replaced meanwhile.
        /// </summary>
        private async Task OpenUnopenedPageAsync(LauncherPage page)
        {
            if (_pendingOpens.ContainsKey(page))
                return;

            var cts = new CancellationTokenSource();
            _pendingOpens[page] = cts;

            var node = page.Node;
            try
            {
                var (error, isBare, gitDir) = await Task.Run(() => ResolveRepositoryLocation(node.Id));
                if (cts.IsCancellationRequested || page.Data != null || page.Node != node || !Pages.Contains(page))
                    return;

                if (error != null)
                {
                    page.Notifications.Add(new Models.Notification
                    {
                        Group = node.Id,
                        Message = error,
                        IsError = true,
                    });
                    return;
                }

                page.Data = CreateRepository(node, isBare, gitDir);
                RebuildWorkspaceRepositories();

                if (_activePage == page)
                    PostActivePageChanged();
            }
            catch (Exception e)
            {
                Native.OS.LogException(e);
            }
            finally
            {
                if (_pendingOpens.TryGetValue(page, out var current) && current == cts)
                    _pendingOpens.Remove(page);
            }
        }

        private void CancelPendingOpen(LauncherPage page)
        {
            if (_pendingOpens.Remove(page, out var cts))
                cts.Cancel();
        }

        /// <summary>
        /// Loads the remaining unopened tabs one after another. Cancelled when another workspace is activated.
        /// </summary>
        private void StartBackgroundLoading()
        {
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            _ = LoadRemainingPagesAsync(new List<LauncherPage>(Pages), _loadCts.Token);
        }

        private async Task LoadRemainingPagesAsync(List<LauncherPage> pages, CancellationToken token)
        {
            foreach (var page in pages)
            {
                if (token.IsCancellationRequested)
                    return;

                if (page == _activePage || !Pages.Contains(page) || !IsUnopenedPage(page) || _pendingOpens.ContainsKey(page))
                    continue;

                await OpenUnopenedPageAsync(page);
                if (token.IsCancellationRequested)
                    return;

                // Yield between tabs so the UI stays responsive while Repository.Open() runs on the UI thread.
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
        }

        private Workspace _activeWorkspace;
        private LauncherPage _activePage;
        private bool _ignoreIndexChange;
        private string _title = string.Empty;
        private ICommandPalette _commandPalette;
        private Models.Version _newVersion = null;
        private readonly Dictionary<LauncherPage, CancellationTokenSource> _pendingOpens = new Dictionary<LauncherPage, CancellationTokenSource>();
        private CancellationTokenSource _loadCts;
        private readonly List<ParkedWorkspace> _parked = new List<ParkedWorkspace>();

        private sealed class ParkedWorkspace
        {
            public ParkedWorkspace(Workspace workspace, AvaloniaList<LauncherPage> pages, LauncherPage activePage)
            {
                Workspace = workspace;
                Pages = pages;
                ActivePage = activePage;
            }

            public Workspace Workspace { get; }
            public AvaloniaList<LauncherPage> Pages { get; }
            public LauncherPage ActivePage { get; }
        }
    }
}
