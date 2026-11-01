using System;
using System.ComponentModel;
using System.Threading.Tasks;

using AvaloniaEdit.Document;

using CommunityToolkit.Mvvm.ComponentModel;

using SourceGit.Models;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     One open file in the editor page. The AvaloniaEdit document owns the text and the undo
    ///     history, so switching tabs never loses unsaved edits or undo steps.
    /// </summary>
    public class FileEditorTab : ObservableObject
    {
        public FileEditorTab(EditableTextFile file)
        {
            File = file;
            Document = new TextDocument(file.Text);
            Document.UndoStack.MarkAsOriginalFile();
            Document.UndoStack.PropertyChanged += OnUndoStackPropertyChanged;
        }

        public EditableTextFile File { get; private set; }

        public TextDocument Document { get; }

        public string Path => File.RelativePath;

        public string Name => System.IO.Path.GetFileName(Path);

        public string MetaText => File.MetaText;

        public bool IsDirty => !Document.UndoStack.IsOriginalFile;

        public bool IsReadOnly => File.IsReadOnly;

        /// <summary>Caret offset is restored when the user switches back to this tab.</summary>
        public int CaretOffset { get; set; } = 0;

        public async Task SaveAsync()
        {
            // Capture first so edits typed while the write is in flight keep the tab dirty.
            var text = Document.Text;
            var normalized = EditableTextFile.NormalizeLineEndings(text, File.LineEnding);
            await File.SaveAsync(text).ConfigureAwait(true);

            // The editor always works with the platform newline, while the file keeps its own
            // line ending style, so compare normalized text before clearing the dirty marker.
            if (EditableTextFile.NormalizeLineEndings(Document.Text, File.LineEnding).Equals(normalized, StringComparison.Ordinal))
                Document.UndoStack.MarkAsOriginalFile();

            OnPropertyChanged(nameof(MetaText));
            OnPropertyChanged(nameof(IsReadOnly));
        }

        /// <summary>Replaces the document with the current file content (discards undo history).</summary>
        public async Task<EditableTextFile> ReloadAsync(string repository)
        {
            var file = await EditableTextFile.LoadAsync(repository, Path).ConfigureAwait(true);
            if (file.Status != Models.TextFileLoadStatus.Success)
                return file;

            File = file;
            Document.Text = file.Text;
            Document.UndoStack.ClearAll();
            Document.UndoStack.MarkAsOriginalFile();

            OnPropertyChanged(nameof(MetaText));
            OnPropertyChanged(nameof(IsReadOnly));
            return file;
        }

        public void Cleanup()
        {
            Document.UndoStack.PropertyChanged -= OnUndoStackPropertyChanged;
        }

        private void OnUndoStackPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(UndoStack.IsOriginalFile))
                return;

            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(MetaText));
        }
    }
}
