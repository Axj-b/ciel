namespace SourceGit.Models
{
    public enum RepositoryScriptKind
    {
        Cmd,
        Batch,
        PowerShell,
        Shell,
    }

    /// <summary>
    /// A script discovered in the repository root (or in its 'scripts' / 'actions' folders).
    /// These are never persisted - they always come from the working tree.
    /// </summary>
    public class RepositoryScriptAction
    {
        /// <summary>Display name: relative path without the file extension.</summary>
        public string Name { get; set; }

        /// <summary>Path relative to the repository root, using forward slashes.</summary>
        public string RelativePath { get; set; }

        /// <summary>Absolute path, using the platform directory separator.</summary>
        public string FullPath { get; set; }

        public RepositoryScriptKind Kind { get; set; }
    }
}
