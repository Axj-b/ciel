using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class RepositoryConfigure : ObservableObject
    {
        public string UserName
        {
            get;
            set;
        }

        public string UserEmail
        {
            get;
            set;
        }

        public List<Models.GitAccount> Accounts
        {
            get;
        }

        public Models.GitAccount SelectedAccount
        {
            get => _selectedAccount;
            set
            {
                var previous = _selectedAccount;
                if (!SetProperty(ref _selectedAccount, value) || _isLoading || value == null)
                    return;

                if (string.IsNullOrEmpty(value.Id))
                {
                    // `Custom / Global` means this repository should not pin any
                    // account values. Restore the values the user had before an
                    // account was selected, or clear them when the repository was
                    // already pinned to an account.
                    if (_hasStashedIdentity)
                    {
                        UserName = _stashedUserName;
                        UserEmail = _stashedUserEmail;
                        SSHKey = _stashedSSHKey;
                        HTTPSUsername = _stashedHTTPSUsername;
                        _httpsHost = _stashedHTTPSHost;
                        _hasStashedIdentity = false;
                    }
                    else
                    {
                        UserName = string.Empty;
                        UserEmail = string.Empty;
                        SSHKey = string.Empty;
                        HTTPSUsername = string.Empty;
                        _httpsHost = string.Empty;
                    }
                }
                else
                {
                    if (previous != null && string.IsNullOrEmpty(previous.Id))
                    {
                        _stashedUserName = UserName;
                        _stashedUserEmail = UserEmail;
                        _stashedSSHKey = SSHKey;
                        _stashedHTTPSUsername = HTTPSUsername;
                        _stashedHTTPSHost = _httpsHost;
                        _hasStashedIdentity = true;
                    }

                    UserName = value.UserName;
                    UserEmail = value.UserEmail;
                    SSHKey = value.PrivateSSHKey;
                    HTTPSUsername = value.HTTPSUsername;
                    _httpsHost = value.HTTPSHost;
                }
            }
        }

        public string SSHKey
        {
            get => _sshKey;
            set => SetProperty(ref _sshKey, value);
        }

        public string HTTPSUsername
        {
            get => _httpsUsername;
            set => SetProperty(ref _httpsUsername, value);
        }

        public List<string> Remotes
        {
            get;
        }

        public string DefaultRemote
        {
            get => _repo.Settings.DefaultRemote;
            set
            {
                if (_repo.Settings.DefaultRemote != value)
                {
                    _repo.Settings.DefaultRemote = value;
                    OnPropertyChanged();
                }
            }
        }

        public int PreferredMergeMode
        {
            get => _repo.Settings.PreferredMergeMode;
            set
            {
                if (_repo.Settings.PreferredMergeMode != value)
                {
                    _repo.Settings.PreferredMergeMode = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool GPGCommitSigningEnabled
        {
            get;
            set;
        }

        public bool GPGTagSigningEnabled
        {
            get;
            set;
        }

        public string GPGUserSigningKey
        {
            get;
            set;
        }

        public string HttpProxy
        {
            get => _httpProxy;
            set => SetProperty(ref _httpProxy, value);
        }

        public string ConventionalTypesOverride
        {
            get => _repo.Settings.ConventionalTypesOverride;
            set
            {
                if (_repo.Settings.ConventionalTypesOverride != value)
                {
                    _repo.Settings.ConventionalTypesOverride = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool EnablePruneOnFetch
        {
            get;
            set;
        }

        public bool AskBeforeAutoUpdatingSubmodules
        {
            get => _repo.Settings.AskBeforeAutoUpdatingSubmodules;
            set => _repo.Settings.AskBeforeAutoUpdatingSubmodules = value;
        }

        public bool EnableRecursiveWhenAutoUpdatingSubmodules
        {
            get => _repo.Settings.EnableRecursiveWhenAutoUpdatingSubmodules;
            set => _repo.Settings.EnableRecursiveWhenAutoUpdatingSubmodules = value;
        }

        public AvaloniaList<Models.CommitTemplate> CommitTemplates
        {
            get => _repo.Settings.CommitTemplates;
        }

        public Models.CommitTemplate SelectedCommitTemplate
        {
            get => _selectedCommitTemplate;
            set => SetProperty(ref _selectedCommitTemplate, value);
        }

        public AvaloniaList<Models.IssueTracker> IssueTrackers
        {
            get;
        } = [];

        public Models.IssueTracker SelectedIssueTracker
        {
            get => _selectedIssueTracker;
            set => SetProperty(ref _selectedIssueTracker, value);
        }

        public List<string> AvailableOpenAIServices
        {
            get;
            private set;
        }

        public string PreferredOpenAIService
        {
            get => _repo.Settings.PreferredOpenAIService;
            set => _repo.Settings.PreferredOpenAIService = value;
        }

        public AvaloniaList<Models.CustomAction> CustomActions
        {
            get => _repo.Settings.CustomActions;
        }

        public Models.CustomAction SelectedCustomAction
        {
            get => _selectedCustomAction;
            set => SetProperty(ref _selectedCustomAction, value);
        }

        public RepositoryConfigure(Repository repo)
        {
            _repo = repo;

            Remotes = new List<string>();
            foreach (var remote in _repo.Remotes)
                Remotes.Add(remote.Name);

            Accounts = new List<Models.GitAccount>();
            Accounts.Add(new Models.GitAccount() { Id = string.Empty, Name = App.Text("Configure.Git.Account.Custom") });
            foreach (var account in Preferences.Instance.GitAccounts)
                Accounts.Add(account);

            AvailableOpenAIServices = new List<string>() { "---" };
            foreach (var service in Preferences.Instance.OpenAIServices)
                AvailableOpenAIServices.Add(service.Name);

            if (!AvailableOpenAIServices.Contains(PreferredOpenAIService))
                PreferredOpenAIService = "---";

            _cached = new Commands.Config(repo.FullPath).ReadAll();
            if (_cached.TryGetValue("user.name", out var name))
                UserName = name;
            if (_cached.TryGetValue("user.email", out var email))
                UserEmail = email;
            if (_cached.TryGetValue("commit.gpgsign", out var gpgCommitSign))
                GPGCommitSigningEnabled = gpgCommitSign == "true";
            if (_cached.TryGetValue("tag.gpgsign", out var gpgTagSign))
                GPGTagSigningEnabled = gpgTagSign == "true";
            if (_cached.TryGetValue("user.signingkey", out var signingKey))
                GPGUserSigningKey = signingKey;
            if (_cached.TryGetValue("http.proxy", out var proxy))
                HttpProxy = proxy;
            if (_cached.TryGetValue("fetch.prune", out var prune))
                EnablePruneOnFetch = (prune == "true");

            _isLoading = true;
            var identity = new Commands.Config(repo.FullPath).ReadAllLocal();
            var accountId = identity.GetValueOrDefault(Commands.GitIdentityConfig.ACCOUNT, string.Empty);
            SelectedAccount = Accounts.Find(x => x.Id == accountId) ?? Accounts[0];
            SSHKey = identity.GetValueOrDefault(Commands.GitIdentityConfig.SSH_KEY, string.Empty);
            if (string.IsNullOrEmpty(SSHKey))
            {
                var sshCommand = identity.GetValueOrDefault(Commands.GitIdentityConfig.CORE_SSH_COMMAND, string.Empty);
                if (Models.GitAccount.IsGeneratedSSHCommand(sshCommand))
                    SSHKey = Models.GitAccount.ExtractKeyFromSSHCommand(sshCommand);
            }
            HTTPSUsername = identity.GetValueOrDefault(Commands.GitIdentityConfig.HTTPS_USERNAME, string.Empty);
            _httpsHost = identity.GetValueOrDefault(Commands.GitIdentityConfig.HTTPS_HOST, string.Empty);
            if (string.IsNullOrEmpty(_httpsHost))
                _httpsHost = SelectedAccount.HTTPSHost;
            _isLoading = false;

            foreach (var rule in _repo.IssueTrackers)
            {
                IssueTrackers.Add(new()
                {
                    IsShared = rule.IsShared,
                    Name = rule.Name,
                    RegexString = rule.RegexString,
                    URLTemplate = rule.URLTemplate,
                });
            }
        }

        public void ClearHttpProxy()
        {
            HttpProxy = string.Empty;
        }

        public void AddCommitTemplate()
        {
            var template = new Models.CommitTemplate() { Name = "New Template" };
            _repo.Settings.CommitTemplates.Add(template);
            SelectedCommitTemplate = template;
        }

        public void RemoveSelectedCommitTemplate()
        {
            if (_selectedCommitTemplate != null)
                _repo.Settings.CommitTemplates.Remove(_selectedCommitTemplate);
            SelectedCommitTemplate = null;
        }

        public List<string> GetRemoteVisitUrls()
        {
            var outs = new List<string>();
            foreach (var remote in _repo.Remotes)
            {
                if (remote.TryGetVisitURL(out var url))
                    outs.Add(url);
            }
            return outs;
        }

        public void AddIssueTracker(string name, string regex, string url)
        {
            var rule = new Models.IssueTracker()
            {
                IsShared = false,
                Name = name,
                RegexString = regex,
                URLTemplate = url,
            };

            IssueTrackers.Add(rule);
            SelectedIssueTracker = rule;
        }

        public void RemoveIssueTracker()
        {
            if (_selectedIssueTracker is { } rule)
                IssueTrackers.Remove(rule);

            SelectedIssueTracker = null;
        }

        public void AddNewCustomAction()
        {
            SelectedCustomAction = _repo.Settings.AddNewCustomAction();
        }

        public void RemoveSelectedCustomAction()
        {
            _repo.Settings.RemoveCustomAction(_selectedCustomAction);
            SelectedCustomAction = null;
        }

        public void MoveSelectedCustomActionUp()
        {
            if (_selectedCustomAction != null)
                _repo.Settings.MoveCustomActionUp(_selectedCustomAction);
        }

        public void MoveSelectedCustomActionDown()
        {
            if (_selectedCustomAction != null)
                _repo.Settings.MoveCustomActionDown(_selectedCustomAction);
        }

        public async Task SaveAsync()
        {
            _repo.Settings.Save();

            var account = SelectedAccount is { } selected && !string.IsNullOrEmpty(selected.Id) ? selected : null;
            if (account != null)
            {
                // An explicitly selected account must be pinned to this repository,
                // even when its values match the current global git config.
                await new Commands.Config(_repo.FullPath).SetAsync("user.name", UserName);
                await new Commands.Config(_repo.FullPath).SetAsync("user.email", UserEmail);
            }
            else
            {
                await SetIfChangedAsync("user.name", UserName, "");
                await SetIfChangedAsync("user.email", UserEmail, "");
            }

            await SetIfChangedAsync("commit.gpgsign", GPGCommitSigningEnabled ? "true" : "false", "false");
            await SetIfChangedAsync("tag.gpgsign", GPGTagSigningEnabled ? "true" : "false", "false");
            await SetIfChangedAsync("user.signingkey", GPGUserSigningKey, "");
            await SetIfChangedAsync("http.proxy", HttpProxy, "");
            await SetIfChangedAsync("fetch.prune", EnablePruneOnFetch ? "true" : "false", "false");

            await Commands.GitIdentityConfig.ApplyAsync(_repo.FullPath, account, SSHKey, HTTPSUsername, _httpsHost);

            await ApplyIssueTrackerChangesAsync();
        }

        private async Task SetIfChangedAsync(string key, string value, string defValue)
        {
            if (value != _cached.GetValueOrDefault(key, defValue))
                await new Commands.Config(_repo.FullPath).SetAsync(key, value);
        }

        private async Task ApplyIssueTrackerChangesAsync()
        {
            var changed = false;
            var oldRules = new Dictionary<string, Models.IssueTracker>();
            foreach (var rule in _repo.IssueTrackers)
                oldRules.Add(rule.Name, rule);

            foreach (var rule in IssueTrackers)
            {
                if (oldRules.TryGetValue(rule.Name, out var old))
                {
                    if (old.IsShared != rule.IsShared)
                    {
                        changed = true;
                        await new Commands.IssueTracker(_repo.FullPath, old.IsShared).RemoveAsync(old.Name);
                        await new Commands.IssueTracker(_repo.FullPath, rule.IsShared).AddAsync(rule);
                    }
                    else
                    {
                        if (!old.RegexString.Equals(rule.RegexString, StringComparison.Ordinal))
                        {
                            changed = true;
                            await new Commands.IssueTracker(_repo.FullPath, old.IsShared).UpdateRegexAsync(rule);
                        }

                        if (!old.URLTemplate.Equals(rule.URLTemplate, StringComparison.Ordinal))
                        {
                            changed = true;
                            await new Commands.IssueTracker(_repo.FullPath, old.IsShared).UpdateURLTemplateAsync(rule);
                        }
                    }

                    oldRules.Remove(rule.Name);
                }
                else
                {
                    changed = true;
                    await new Commands.IssueTracker(_repo.FullPath, rule.IsShared).AddAsync(rule);
                }
            }

            if (oldRules.Count > 0)
            {
                changed = true;

                foreach (var kv in oldRules)
                    await new Commands.IssueTracker(_repo.FullPath, kv.Value.IsShared).RemoveAsync(kv.Key);
            }

            if (changed)
            {
                _repo.IssueTrackers.Clear();
                _repo.IssueTrackers.AddRange(IssueTrackers);
            }
        }

        private readonly Repository _repo;
        private readonly Dictionary<string, string> _cached;
        private string _httpProxy;
        private Models.CommitTemplate _selectedCommitTemplate = null;
        private Models.IssueTracker _selectedIssueTracker = null;
        private Models.CustomAction _selectedCustomAction = null;
        private Models.GitAccount _selectedAccount = null;
        private string _sshKey = string.Empty;
        private string _httpsUsername = string.Empty;
        private string _httpsHost = string.Empty;
        private bool _isLoading = false;
        private bool _hasStashedIdentity = false;
        private string _stashedUserName = string.Empty;
        private string _stashedUserEmail = string.Empty;
        private string _stashedSSHKey = string.Empty;
        private string _stashedHTTPSUsername = string.Empty;
        private string _stashedHTTPSHost = string.Empty;
    }
}
