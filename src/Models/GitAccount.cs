using System;
using System.IO;
using System.Text.RegularExpressions;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.Models
{
    /// <summary>
    /// A reusable Git identity (name, email, SSH key and optional HTTPS username).
    /// Accounts are stored globally in the app preferences; a repository can use
    /// one by writing the values into its own local git config.
    /// </summary>
    public partial class GitAccount : ObservableObject
    {
        [GeneratedRegex(@"-i\s+(?:\""([^\""]+)\""|'([^']+)'|(\S+))")]
        private static partial Regex REG_SSH_KEY();

        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string UserName
        {
            get => _userName;
            set => SetProperty(ref _userName, value);
        }

        public string UserEmail
        {
            get => _userEmail;
            set => SetProperty(ref _userEmail, value);
        }

        public string PrivateSSHKey
        {
            get => _privateSSHKey;
            set => SetProperty(ref _privateSSHKey, value);
        }

        public string HTTPSUsername
        {
            get => _httpsUsername;
            set => SetProperty(ref _httpsUsername, value);
        }

        public string HTTPSHost
        {
            get => _httpsHost;
            set => SetProperty(ref _httpsHost, value);
        }

        public static string NormalizeKeyPath(string key)
        {
            var path = (key ?? string.Empty).Trim();
            if (path.Length >= 2 && ((path[0] == '"' && path[^1] == '"') || (path[0] == '\'' && path[^1] == '\'')))
                path = path.Substring(1, path.Length - 2).Trim();

            if (string.IsNullOrEmpty(path))
                return string.Empty;

            path = Environment.ExpandEnvironmentVariables(path);
            if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Substring(2));

            return path;
        }

        public static string BuildSSHCommand(string key)
        {
            var path = NormalizeKeyPath(key);
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            // OpenSSH accepts forward slashes on every supported platform and they
            // avoid escaping issues in git config/shell command lines.
            path = path.Replace('\\', '/');
            return $"ssh -i \"{path}\" -o IdentitiesOnly=yes -o AddKeysToAgent=yes";
        }

        public static string ExtractKeyFromSSHCommand(string command)
        {
            if (string.IsNullOrEmpty(command))
                return string.Empty;

            var match = REG_SSH_KEY().Match(command);
            if (!match.Success)
                return string.Empty;

            for (var i = 1; i < match.Groups.Count; i++)
            {
                if (match.Groups[i].Success)
                    return match.Groups[i].Value;
            }

            return string.Empty;
        }

        public static bool IsGeneratedSSHCommand(string command)
        {
            var key = ExtractKeyFromSSHCommand(command);
            return !string.IsNullOrEmpty(key) && command.Equals(BuildSSHCommand(key), StringComparison.Ordinal);
        }

        public static string NormalizeHTTPSHost(string host)
        {
            var value = (host ?? string.Empty).Trim();
            if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(8);
            else if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(7);

            var slash = value.IndexOf('/');
            if (slash >= 0)
                value = value.Substring(0, slash);

            var at = value.IndexOf('@');
            if (at >= 0)
                value = value.Substring(at + 1);

            value = value.Trim().TrimEnd('/');
            return string.IsNullOrEmpty(value) ? "github.com" : value;
        }

        private string _id = Guid.NewGuid().ToString("N");
        private string _name = string.Empty;
        private string _userName = string.Empty;
        private string _userEmail = string.Empty;
        private string _privateSSHKey = string.Empty;
        private string _httpsUsername = string.Empty;
        private string _httpsHost = "github.com";
    }
}
