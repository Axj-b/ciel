using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    /// <summary>
    ///     Lists every file of the working tree: tracked files plus untracked files that are not ignored.
    ///     Deleted files are filtered out by the caller, since they cannot be opened.
    /// </summary>
    public class QueryWorkingTreeFiles : Command
    {
        public QueryWorkingTreeFiles(string repo, bool includeIgnored = false)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = "ls-files -z --cached --others --exclude-standard";
            _includeIgnored = includeIgnored;
        }

        public async Task<(List<string> Files, HashSet<string> Ignored)> GetResultAsync()
        {
            var files = new HashSet<string>(await ReadPathsAsync().ConfigureAwait(false), StringComparer.Ordinal);
            var ignored = new HashSet<string>(StringComparer.Ordinal);

            if (_includeIgnored)
            {
                // `--ignored` filters the `--others` output down to ignored entries, so this has to
                // be a second query: the standard query above would lose the regular untracked files.
                Args = "ls-files -z --others --ignored --exclude-standard";
                foreach (var path in await ReadPathsAsync().ConfigureAwait(false))
                {
                    ignored.Add(path);
                    files.Add(path);
                }
            }

            return (new List<string>(files), ignored);
        }

        private async Task<List<string>> ReadPathsAsync()
        {
            var outs = new List<string>();
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            if (!rs.IsSuccess)
                return outs;

            foreach (var path in rs.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                outs.Add(path);

            return outs;
        }

        private readonly bool _includeIgnored;
    }
}
