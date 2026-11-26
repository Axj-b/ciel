namespace SourceGit.Models
{
    public enum SubmoduleStatus
    {
        Normal = 0,
        NotInited,
        RevisionChanged,
        Unmerged,
        Modified,
    }

    public class Submodule
    {
        public string Path { get; set; } = string.Empty;
        public string SHA { get; set; } = string.Empty;
        public string URL { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public string PrivateSSHKey { get; set; } = string.Empty;
        public SubmoduleStatus Status { get; set; } = SubmoduleStatus.Normal;
        public bool IsDirty => Status > SubmoduleStatus.NotInited;

        /// <summary>
        /// The local config key used to remember the SSH key for a submodule.
        /// Stored per checkout (not in .gitmodules), because the key is a
        /// machine-specific setting and must not be committed.
        /// </summary>
        public static string BuildSSHKeyConfigKey(string path)
        {
            // Same unquoted form as `remote.<name>.sshkey`; `git config` keys given on the
            // command line must not contain literal quote characters.
            return $"submodule.{path}.sshkey";
        }
    }
}
