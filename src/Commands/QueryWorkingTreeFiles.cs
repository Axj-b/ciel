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
        public QueryWorkingTreeFiles(string repo)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = "ls-files -z --cached --others --exclude-standard";
        }

        public async Task<List<string>> GetResultAsync()
        {
            var outs = new List<string>();
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            if (!rs.IsSuccess)
                return outs;

            foreach (var path in rs.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                outs.Add(path);

            return outs;
        }
    }
}
