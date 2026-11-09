using System;
using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    /// <summary>
    ///     Shows the content of a file stored in a revision. Markdown files are rendered by default
    ///     and can be toggled back to their source form.
    /// </summary>
    public partial class RevisionTextFilePresenter : UserControl
    {
        public RevisionTextFilePresenter()
        {
            InitializeComponent();
            Preview.OpenFileRequested += OnOpenFileRequested;
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            ApplyMode();
        }

        private void ApplyMode()
        {
            if (DataContext is not Models.RevisionTextFile file)
            {
                Toolbar.IsVisible = false;
                SourceView.IsVisible = true;
                Preview.IsVisible = false;
                Preview.IsPreviewActive = false;
                return;
            }

            if (!file.IsMarkdown)
            {
                Toolbar.IsVisible = false;
                SourceView.IsVisible = true;
                Preview.IsVisible = false;
                Preview.IsPreviewActive = false;
                return;
            }

            // Every revision file starts rendered; only an explicit toggle for the same file
            // should bring the source view back.
            if (!ReferenceEquals(file, _file))
            {
                _file = file;
                _isRendered = true;
            }

            Toolbar.IsVisible = true;
            SourceView.IsVisible = !_isRendered;
            Preview.IsVisible = _isRendered;
            Preview.IsPreviewActive = _isRendered;

            SourceIcon.IsVisible = !_isRendered;
            RenderIcon.IsVisible = _isRendered;
            ToolTip.SetTip(Toggle, App.Text(_isRendered ? "Editor.View.Rendered" : "Editor.View.Source"));

            Preview.Source = file.Content;
            Preview.FileName = file.FileName;
            Preview.RepositoryPath = file.Repository;
        }

        private void OnToggleMode(object sender, RoutedEventArgs e)
        {
            _isRendered = !_isRendered;
            ApplyMode();
            e.Handled = true;
        }

        private async void OnOpenFileRequested(string path)
        {
            if (DataContext is not Models.RevisionTextFile file || string.IsNullOrEmpty(path))
                return;

            var repoView = this.FindAncestorOfType<Repository>();
            if (repoView is { DataContext: ViewModels.Repository repo })
            {
                await repo.OpenEditorPageAtAsync(path);
                return;
            }

            // Detached windows have no editor page; fall back to the system editor.
            if (!string.IsNullOrEmpty(file.Repository))
            {
                var fullPath = Path.Combine(file.Repository, path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fullPath))
                    Native.OS.OpenWithDefaultEditor(fullPath);
            }
        }

        private bool _isRendered = true;
        private Models.RevisionTextFile _file = null;
    }
}
