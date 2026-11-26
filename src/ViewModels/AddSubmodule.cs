using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class AddSubmodule : Popup
    {
        [Required(ErrorMessage = "Url is required!!!")]
        [CustomValidation(typeof(AddSubmodule), nameof(ValidateURL))]
        public string Url
        {
            get => _url;
            set
            {
                if (SetProperty(ref _url, value, true))
                    UseSSH = Models.Remote.IsSSH(value);
            }
        }

        public bool UseSSH
        {
            get => _useSSH;
            set
            {
                if (SetProperty(ref _useSSH, value))
                    ValidateProperty(_sshKey, nameof(SSHKey));
            }
        }

        [CustomValidation(typeof(AddSubmodule), nameof(ValidateSSHKey))]
        public string SSHKey
        {
            get => _sshKey;
            set => SetProperty(ref _sshKey, value, true);
        }

        public string RelativePath
        {
            get => _relativePath;
            set => SetProperty(ref _relativePath, value);
        }

        public bool Recursive
        {
            get;
            set;
        }

        public AddSubmodule(Repository repo)
        {
            _repo = repo;
        }

        public static ValidationResult ValidateURL(string url, ValidationContext ctx)
        {
            if (!Models.Remote.IsValidURL(url))
                return new ValidationResult("Invalid repository URL format");

            return ValidationResult.Success;
        }

        public static ValidationResult ValidateSSHKey(string sshkey, ValidationContext ctx)
        {
            if (ctx.ObjectInstance is AddSubmodule { _useSSH: true } && !string.IsNullOrEmpty(sshkey))
            {
                if (!File.Exists(sshkey))
                    return new ValidationResult("Given SSH private key can NOT be found!");
            }

            return ValidationResult.Success;
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = "Adding submodule...";

            var log = _repo.CreateLog("Add Submodule");
            Use(log);

            var relativePath = _relativePath;
            if (string.IsNullOrEmpty(relativePath))
            {
                if (_url.EndsWith("/.git", StringComparison.Ordinal))
                    relativePath = Path.GetFileName(Path.GetDirectoryName(_url));
                else if (_url.EndsWith(".git", StringComparison.Ordinal))
                    relativePath = Path.GetFileNameWithoutExtension(_url);
                else
                    relativePath = Path.GetFileName(_url);
            }

            var key = _useSSH ? Models.GitAccount.NormalizeKeyPath(_sshKey) : string.Empty;
            var succ = await new Commands.Submodule(_repo.FullPath)
                .Use(log)
                .WithSSHKey(key)
                .AddAsync(_url, relativePath, Recursive);

            if (succ && !string.IsNullOrEmpty(key))
            {
                await new Commands.Config(_repo.FullPath)
                    .Use(log)
                    .SetAsync(Models.Submodule.BuildSSHKeyConfigKey(relativePath).Quoted(), key);

                await Commands.GitIdentityConfig.ApplySubmoduleSSHKeyAsync(
                    Path.Combine(_repo.FullPath, relativePath),
                    key,
                    log);
            }

            log.Complete();
            return succ;
        }

        private readonly Repository _repo = null;
        private string _url = string.Empty;
        private string _relativePath = string.Empty;
        private string _sshKey = string.Empty;
        private bool _useSSH = false;
    }
}
