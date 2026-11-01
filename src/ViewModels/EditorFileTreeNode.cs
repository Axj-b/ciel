using System.Collections.Generic;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     One row of the editor's working tree. Folders and files share the same type so the tree can
    ///     be flattened into a single virtualized list.
    /// </summary>
    public class EditorFileTreeNode : ObservableObject
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Repository relative path. Empty for the invisible root node.</summary>
        public string Path { get; set; } = string.Empty;

        public bool IsFolder { get; set; } = false;

        public int Depth { get; set; } = 0;

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public List<EditorFileTreeNode> Children { get; } = [];

        private bool _isExpanded = false;
    }
}
