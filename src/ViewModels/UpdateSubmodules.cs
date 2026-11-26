using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class UpdateSubmodules : Popup
    {
        public bool HasPreSelectedSubmodule
        {
            get;
        }

        public List<Models.Submodule> Submodules
        {
            get => _repo.Submodules;
        }

        public Models.Submodule SelectedSubmodule
        {
            get;
            set;
        }

        public bool UpdateAll
        {
            get => _updateAll;
            set => SetProperty(ref _updateAll, value);
        }

        public bool IsEnableInitVisible
        {
            get;
            set;
        } = true;

        public bool EnableInit
        {
            get;
            set;
        } = true;

        public bool EnableRecursive
        {
            get;
            set;
        } = true;

        public bool EnableRemote
        {
            get;
            set;
        } = false;

        public UpdateSubmodules(Repository repo, Models.Submodule selected)
        {
            _repo = repo;

            if (selected != null)
            {
                _updateAll = false;
                SelectedSubmodule = selected;
                IsEnableInitVisible = selected.Status == Models.SubmoduleStatus.NotInited;
                EnableInit = selected.Status == Models.SubmoduleStatus.NotInited;
                HasPreSelectedSubmodule = true;
            }
            else if (repo.Submodules.Count > 0)
            {
                SelectedSubmodule = repo.Submodules[0];
                IsEnableInitVisible = true;
                HasPreSelectedSubmodule = false;
            }

            EnableRecursive = _repo.Settings.EnableRecursiveWhenAutoUpdatingSubmodules;
        }

        public override async Task<bool> Sure()
        {
            var targets = new List<Models.Submodule>();
            if (_updateAll)
            {
                targets.AddRange(Submodules);
            }
            else if (SelectedSubmodule != null)
            {
                targets.Add(SelectedSubmodule);
            }

            if (targets.Count == 0)
                return true;

            var log = _repo.CreateLog("Update Submodule");
            using var lockWatcher = _repo.LockWatcher();
            Use(log);

            // A single `submodule update` call can only use one SSH key, so
            // group the targets by their resolved key and update each group
            // separately. Modules without an explicit key fall back to the
            // repository-level key (or the default SSH configuration).
            var fallbackKey = await Commands.GitIdentityConfig.GetSSHKeyAsync(_repo.FullPath);
            var groups = targets.GroupBy(x => string.IsNullOrEmpty(x.PrivateSSHKey) ? fallbackKey : x.PrivateSSHKey);
            foreach (var group in groups)
            {
                await new Commands.Submodule(_repo.FullPath)
                    .Use(log)
                    .WithSSHKey(group.Key)
                    .UpdateAsync(group.Select(x => x.Path).ToList(), EnableInit, EnableRecursive, EnableRemote);
            }

            log.Complete();
            _repo.MarkSubmodulesDirtyManually();
            return true;
        }

        private readonly Repository _repo = null;
        private bool _updateAll = true;
    }
}
