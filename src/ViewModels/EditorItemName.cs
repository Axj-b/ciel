using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     Small popup used to name a new file/folder or to rename an existing row of the editor tree.
    ///     The actual file system operation stays in <see cref="EditorPage"/>.
    /// </summary>
    public class EditorItemName : Popup
    {
        public EditorItemName(EditorPage page, EditorFileTreeNode target, EditorItemNameMode mode)
        {
            _page = page;
            _target = target;
            _mode = mode;

            if (mode == EditorItemNameMode.Rename)
            {
                _folder = string.Empty;
                _name = target?.Name ?? string.Empty;
            }
            else
            {
                _folder = GetFolder(target);
            }
        }

        [CustomValidation(typeof(EditorItemName), nameof(ValidateName))]
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value, true);
        }

        public string Title => _mode switch
        {
            EditorItemNameMode.NewFolder => App.Text("Editor.NewFolder.Title"),
            EditorItemNameMode.Rename => App.Text("Editor.Rename.Title"),
            _ => App.Text("Editor.NewFile.Title"),
        };

        public string TargetText => _mode == EditorItemNameMode.Rename
            ? _target?.Path ?? string.Empty
            : string.IsNullOrEmpty(_folder) ? "/" : _folder;

        public bool ShowFileIcon => _mode == EditorItemNameMode.NewFile;

        public bool ShowFolderIcon => _mode == EditorItemNameMode.NewFolder ||
                                      (_mode == EditorItemNameMode.Rename && _target?.IsFolder == true);

        public bool ShowRenameIcon => _mode == EditorItemNameMode.Rename && _target?.IsFolder != true;

        public static ValidationResult ValidateName(string name, ValidationContext context)
        {
            if (context.ObjectInstance is not EditorItemName vm)
                return ValidationResult.Success;

            var trimmed = name?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
                return new ValidationResult(App.Text("Editor.NameRequired"));

            if (trimmed is "." or ".." ||
                trimmed.IndexOfAny(s_invalidNameChars) >= 0 ||
                (OperatingSystem.IsWindows() && (trimmed.EndsWith('.') || trimmed.EndsWith(' '))))
                return new ValidationResult(App.Text("Editor.NameInvalid"));

            var relative = vm.GetTargetPath(trimmed);
            if (vm._mode == EditorItemNameMode.Rename &&
                relative.Equals(vm._target.Path, StringComparison.OrdinalIgnoreCase))
                return ValidationResult.Success;

            if (vm.TargetExists(relative))
                return new ValidationResult(App.Text("Editor.NameExists", trimmed));

            return ValidationResult.Success;
        }

        public override async Task<bool> Sure()
        {
            var name = _name.Trim();
            var error = _mode == EditorItemNameMode.Rename
                ? await _page.RenameItemAsync(_target, name).ConfigureAwait(true)
                : await _page.CreateItemAsync(_folder, name, _mode == EditorItemNameMode.NewFolder).ConfigureAwait(true);

            if (string.IsNullOrEmpty(error))
                return true;

            _page.Notify(error, true);
            return false;
        }

        private static string GetFolder(EditorFileTreeNode target)
        {
            if (target == null)
                return string.Empty;

            if (target.IsFolder)
                return target.Path;

            var index = target.Path.LastIndexOf('/');
            return index < 0 ? string.Empty : target.Path[..index];
        }

        private string GetTargetPath(string name)
        {
            if (_mode != EditorItemNameMode.Rename)
                return string.IsNullOrEmpty(_folder) ? name : _folder + "/" + name;

            var path = _target.Path;
            var index = path.LastIndexOf('/');
            return index < 0 ? name : path[..index] + "/" + name;
        }

        private bool TargetExists(string relative)
        {
            var full = Native.OS.GetAbsPath(_page.RepositoryPath, relative);
            return File.Exists(full) || Directory.Exists(full);
        }

        private static readonly char[] s_invalidNameChars = OperatingSystem.IsWindows()
            ? ['/', '\\', ':', '*', '?', '"', '<', '>', '|']
            : ['/'];

        private readonly EditorPage _page;
        private readonly EditorFileTreeNode _target;
        private readonly EditorItemNameMode _mode;
        private readonly string _folder;
        private string _name = string.Empty;
    }
}
