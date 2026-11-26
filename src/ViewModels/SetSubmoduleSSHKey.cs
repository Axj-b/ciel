using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class SetSubmoduleSSHKey : Popup
    {
        public Models.Submodule Submodule
        {
            get;
        }

        [CustomValidation(typeof(SetSubmoduleSSHKey), nameof(ValidateSSHKey))]
        public string SSHKey
        {
            get => _sshKey;
            set => SetProperty(ref _sshKey, value, true);
        }

        public SetSubmoduleSSHKey(Repository repo, Models.Submodule submodule)
        {
            _repo = repo;
            Submodule = submodule;
            _sshKey = submodule.PrivateSSHKey;
        }

        public static ValidationResult ValidateSSHKey(string sshkey, ValidationContext ctx)
        {
            if (!string.IsNullOrEmpty(sshkey) && !File.Exists(sshkey))
                return new ValidationResult("Given SSH private key can NOT be found!");

            return ValidationResult.Success;
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = "Setting submodule's SSH key...";

            var log = _repo.CreateLog("Set Submodule SSH Key");
            Use(log);

            var key = Models.GitAccount.NormalizeKeyPath(_sshKey);
            var configKey = Models.Submodule.BuildSSHKeyConfigKey(Submodule.Path);
            var config = new Commands.Config(_repo.FullPath).Use(log);
            var local = await config.ReadAllLocalAsync();
            local.TryGetValue(configKey, out var current);

            if ((current ?? string.Empty) == key)
            {
                log.Complete();
                return true;
            }

            var succ = await config.SetAsync(configKey.Quoted(), key);
            if (succ)
            {
                Submodule.PrivateSSHKey = key;
                if (Submodule.Status != Models.SubmoduleStatus.NotInited)
                {
                    await Commands.GitIdentityConfig.ApplySubmoduleSSHKeyAsync(
                        Path.Combine(_repo.FullPath, Submodule.Path),
                        key,
                        log);
                }
            }

            log.Complete();
            return succ;
        }

        private readonly Repository _repo;
        private string _sshKey = string.Empty;
    }
}
