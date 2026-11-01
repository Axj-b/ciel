using System;

using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.TextMate;

namespace SourceGit.Views
{
    /// <summary>
    ///     Editable text surface for the repository editor page. One instance is reused for every tab;
    ///     the page swaps <see cref="TextEditor.Document"/> when the selected tab changes.
    /// </summary>
    public class CodeEditor : TextEditor
    {
        public static readonly StyledProperty<int> TabWidthProperty =
            AvaloniaProperty.Register<CodeEditor, int>(nameof(TabWidth), 4);

        public static readonly StyledProperty<string> FileNameProperty =
            AvaloniaProperty.Register<CodeEditor, string>(nameof(FileName), string.Empty);

        public int TabWidth
        {
            get => GetValue(TabWidthProperty);
            set => SetValue(TabWidthProperty, value);
        }

        /// <summary>Repository relative path of the file currently shown. Used to pick the grammar.</summary>
        public string FileName
        {
            get => GetValue(FileNameProperty);
            set => SetValue(FileNameProperty, value);
        }

        protected override Type StyleKeyOverride => typeof(TextEditor);

        public CodeEditor() : base(new TextArea(), new TextDocument())
        {
            ShowLineNumbers = true;
            WordWrap = false;

            Options.IndentationSize = TabWidth;
            Options.EnableHyperlinks = false;
            Options.EnableEmailHyperlinks = false;
            Options.HighlightCurrentLine = true;
            Options.AllowScrollBelowDocument = false;
            Options.ShowTabs = false;
            Options.ShowSpaces = false;

            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

            TextArea.LeftMargins[0].Margin = new Thickness(8, 0);
            TextArea.TextView.Margin = new Thickness(4, 0);
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            _textMate ??= Models.TextMateHelper.CreateForEditor(this);
            Models.TextMateHelper.SetThemeByApp(_textMate);
            Models.TextMateHelper.SetGrammarByFileName(_textMate, FileName);
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);

            if (_textMate != null)
            {
                _textMate.Dispose();
                _textMate = null;
            }

            GC.Collect();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == TabWidthProperty)
            {
                Options.IndentationSize = TabWidth;
            }
            else if (change.Property == FileNameProperty)
            {
                Models.TextMateHelper.SetGrammarByFileName(_textMate, FileName);
            }
            else if (change.Property.Name == nameof(ActualThemeVariant) && change.NewValue != null)
            {
                Models.TextMateHelper.SetThemeByApp(_textMate);
            }
        }

        private TextMate.Installation _textMate = null;
    }
}
