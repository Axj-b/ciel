using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    /// <summary>
    /// Writes a Git identity into a repository's local config so that every git
    /// process running in that repository (including the embedded terminal and
    /// external tools) uses the same name, email, SSH key and HTTPS username.
    /// </summary>
    public static class GitIdentityConfig
    {
        public const string ACCOUNT = "sourcegit.account";
        public const string SSH_KEY = "sourcegit.sshkey";
        public const string HTTPS_USERNAME = "sourcegit.httpsusername";
        public const string HTTPS_HOST = "sourcegit.httpshost";
        public const string CORE_SSH_COMMAND = "core.sshcommand";

        public static async Task ApplyAsync(
            string repo,
            Models.GitAccount account,
            string sshKey,
            string httpsUsername,
            string httpsHost,
            Models.ICommandLog log = null)
        {
            var config = new Config(repo);
            if (log != null)
                config.Use(log);

            var cached = await config.ReadAllLocalAsync().ConfigureAwait(false);

            await SetIfChangedAsync(config, cached, ACCOUNT, account?.Id ?? string.Empty).ConfigureAwait(false);

            var key = Models.GitAccount.NormalizeKeyPath(sshKey);
            var oldCommand = cached.GetValueOrDefault(CORE_SSH_COMMAND, string.Empty);

            await SetIfChangedAsync(config, cached, SSH_KEY, key).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(key))
            {
                await SetIfChangedAsync(config, cached, CORE_SSH_COMMAND, Models.GitAccount.BuildSSHCommand(key)).ConfigureAwait(false);
            }
            else if (Models.GitAccount.IsGeneratedSSHCommand(oldCommand))
            {
                // Only remove a command that this app generated, so manual
                // core.sshCommand values are never clobbered.
                await SetIfChangedAsync(config, cached, CORE_SSH_COMMAND, string.Empty).ConfigureAwait(false);
            }

            var host = Models.GitAccount.NormalizeHTTPSHost(httpsHost);
            var username = (httpsUsername ?? string.Empty).Trim();
            var oldHost = Models.GitAccount.NormalizeHTTPSHost(cached.GetValueOrDefault(HTTPS_HOST, string.Empty));
            var oldUsername = cached.GetValueOrDefault(HTTPS_USERNAME, string.Empty);

            if (!oldUsername.Equals(username, StringComparison.Ordinal) || !oldHost.Equals(host, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(oldUsername) && !oldHost.Equals(host, StringComparison.OrdinalIgnoreCase))
                    await SetIfChangedAsync(config, cached, CredentialUsernameKey(oldHost), string.Empty).ConfigureAwait(false);

                await SetIfChangedAsync(config, cached, HTTPS_USERNAME, username).ConfigureAwait(false);
                await SetIfChangedAsync(config, cached, HTTPS_HOST, string.IsNullOrEmpty(username) ? string.Empty : host).ConfigureAwait(false);
                await SetIfChangedAsync(config, cached, CredentialUsernameKey(host), username).ConfigureAwait(false);
            }
        }

        public static string CredentialUsernameKey(string host)
        {
            return $"credential.https://{Models.GitAccount.NormalizeHTTPSHost(host)}.username";
        }

        private static async Task SetIfChangedAsync(Config config, Dictionary<string, string> cached, string key, string value)
        {
            var old = cached.GetValueOrDefault(key, string.Empty);
            if (old.Equals(value, StringComparison.Ordinal))
                return;

            await config.SetAsync(key, value).ConfigureAwait(false);
            cached[key] = value;
        }
    }
}
