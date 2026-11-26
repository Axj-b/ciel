using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public partial class QueryUpdatableSubmodules : Command
    {
        [GeneratedRegex(@"^([\-\+])([0-9a-f]+)\s(.*?)(\s\(.*\))?$")]
        private static partial Regex REG_FORMAT_STATUS();

        public QueryUpdatableSubmodules(string repo, bool includeUninited)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = "submodule status";

            _includeUninited = includeUninited;
        }

        public async Task<List<Models.Submodule>> GetResultAsync()
        {
            var submodules = new List<Models.Submodule>();
            var rs = await ReadToEndAsync().ConfigureAwait(false);

            var lines = rs.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var match = REG_FORMAT_STATUS().Match(line);
                if (match.Success)
                {
                    var stat = match.Groups[1].Value;
                    var path = match.Groups[3].Value;
                    if (!_includeUninited && stat.StartsWith('-'))
                        continue;

                    submodules.Add(new Models.Submodule() { Path = path });
                }
            }

            if (submodules.Count > 0)
            {
                var localConfig = await new Config(WorkingDirectory).ReadAllLocalAsync().ConfigureAwait(false);
                foreach (var module in submodules)
                {
                    if (localConfig.TryGetValue(Models.Submodule.BuildSSHKeyConfigKey(module.Path), out var sshKey))
                        module.PrivateSSHKey = sshKey;
                }
            }

            return submodules;
        }

        private bool _includeUninited = false;
    }
}
