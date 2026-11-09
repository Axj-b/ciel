using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SourceGit.Models
{
    /// <summary>
    /// Finds runnable scripts without any user configuration:
    /// <list type="bullet">
    ///   <item>files directly in the repository root;</item>
    ///   <item>files in the 'scripts' and 'actions' folders (recursively, with a depth cap).</item>
    /// </list>
    /// Windows recognizes .cmd/.bat/.ps1, other platforms .ps1/.sh.
    /// </summary>
    public static class RepositoryScriptDetector
    {
        private const int MaxDepth = 4;
        private const int MaxResults = 256;

        private static readonly string[] _folders = ["scripts", "actions"];
        private static readonly string[] _windowsExtensions = [".cmd", ".bat", ".ps1"];
        private static readonly string[] _unixExtensions = [".ps1", ".sh"];
        private static readonly HashSet<string> _ignoredFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            ".vs",
            ".idea",
            "node_modules",
            "bin",
            "obj",
        };

        public static List<RepositoryScriptAction> Discover(string repositoryPath)
        {
            var results = new List<RepositoryScriptAction>();
            if (string.IsNullOrWhiteSpace(repositoryPath))
                return results;

            string root;
            try
            {
                root = Path.GetFullPath(repositoryPath.Replace('/', Path.DirectorySeparatorChar))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return results;
            }

            if (!Directory.Exists(root))
                return results;

            var extensions = OperatingSystem.IsWindows() ? _windowsExtensions : _unixExtensions;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (var file in Directory.GetFiles(root))
                    TryAdd(root, file, extensions, visited, results);
            }
            catch
            {
                // unreadable root - keep whatever we can detect below
            }

            foreach (var folder in _folders)
            {
                var dir = Path.Combine(root, folder);
                if (Directory.Exists(dir))
                    CollectRecursive(root, dir, 0, extensions, visited, results);
            }

            Disambiguate(results);
            results.Sort((l, r) => string.Compare(l.Name, r.Name, StringComparison.OrdinalIgnoreCase));
            return results;
        }

        private static void CollectRecursive(string root, string dir, int depth, string[] extensions, HashSet<string> visited, List<RepositoryScriptAction> results)
        {
            if (depth > MaxDepth || results.Count >= MaxResults)
                return;

            try
            {
                foreach (var file in Directory.GetFiles(dir))
                    TryAdd(root, file, extensions, visited, results);
            }
            catch
            {
                return;
            }

            if (depth == MaxDepth || results.Count >= MaxResults)
                return;

            string[] subdirs;
            try
            {
                subdirs = Directory.GetDirectories(dir);
            }
            catch
            {
                return;
            }

            foreach (var subdir in subdirs)
            {
                var name = Path.GetFileName(subdir);
                if (string.IsNullOrEmpty(name) || name[0] == '.' || _ignoredFolders.Contains(name))
                    continue;

                CollectRecursive(root, subdir, depth + 1, extensions, visited, results);
                if (results.Count >= MaxResults)
                    return;
            }
        }

        private static void TryAdd(string root, string file, string[] extensions, HashSet<string> visited, List<RepositoryScriptAction> results)
        {
            if (results.Count >= MaxResults)
                return;

            var fileName = Path.GetFileName(file);
            if (string.IsNullOrEmpty(fileName) || fileName[0] == '.')
                return;

            var extension = Path.GetExtension(file);
            if (string.IsNullOrEmpty(extension) || !extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                return;

            var kind = extension.ToLowerInvariant() switch
            {
                ".cmd" => RepositoryScriptKind.Cmd,
                ".bat" => RepositoryScriptKind.Batch,
                ".ps1" => RepositoryScriptKind.PowerShell,
                ".sh" => RepositoryScriptKind.Shell,
                _ => (RepositoryScriptKind?)null,
            };

            if (kind is null)
                return;

            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (!visited.Add(relative))
                return;

            results.Add(new RepositoryScriptAction()
            {
                Name = relative.Substring(0, relative.Length - extension.Length),
                RelativePath = relative,
                FullPath = file,
                Kind = kind.Value,
            });
        }

        /// <summary>
        /// Two scripts with the same base name (e.g. build.ps1 and build.cmd) would look identical
        /// in the menu, so fall back to the full relative path for those.
        /// </summary>
        private static void Disambiguate(List<RepositoryScriptAction> scripts)
        {
            var duplicates = scripts
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);

            foreach (var group in duplicates)
            {
                foreach (var script in group)
                    script.Name = script.RelativePath;
            }
        }
    }
}
