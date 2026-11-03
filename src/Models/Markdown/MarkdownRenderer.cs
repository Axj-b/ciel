using System;
using System.Collections.Generic;
using System.Text;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

using Markdig;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

using SourceGit.Views;

namespace SourceGit.Models
{
    public class MarkdownRenderOptions
    {
        public string RepositoryPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public bool IsDark { get; set; } = true;
        public Action<string, Control> RegisterAnchor { get; set; }
        public Action<MarkdownLink> OpenLink { get; set; }
    }

    /// <summary>
    ///     Builds Avalonia controls from a Markdig syntax tree. Everything is constructed on the UI
    ///     thread; only the Markdig parse itself runs in the background.
    /// </summary>
    public class MarkdownRenderer
    {
        public static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        public static MarkdownDocument Parse(string source)
        {
            return Markdown.Parse(source, Pipeline);
        }

        public MarkdownRenderer(MarkdownRenderOptions options)
        {
            _options = options;
        }

        public void Render(MarkdownDocument document, StackPanel host)
        {
            FootnoteGroup footnotes = null;
            foreach (var block in document)
            {
                switch (block)
                {
                    case FootnoteGroup group:
                        footnotes = group;
                        continue;
                    case LinkReferenceDefinitionGroup:
                        continue;
                }

                var control = BuildBlock(block, false);
                if (control != null)
                    host.Children.Add(control);
            }

            if (footnotes is { Count: > 0 })
                BuildFootnotes(footnotes, host);
        }

        private Control BuildBlock(Block block, bool tight)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    return BuildHeading(heading);
                case ParagraphBlock paragraph:
                    return BuildParagraph(paragraph, tight);
                case ListBlock list:
                    return BuildList(list, tight);
                case QuoteBlock quote:
                    return BuildQuote(quote, tight);
                case Markdig.Extensions.Tables.Table table:
                    return BuildTable(table);
                case FencedCodeBlock fenced:
                    return BuildCode(fenced.Info, GetLines(fenced));
                case CodeBlock code:
                    return BuildCode(string.Empty, GetLines(code));
                case HtmlBlock html:
                    return BuildRawHtml(GetLines(html));
                case ThematicBreakBlock:
                    var rule = new Border();
                    rule.Classes.Add("md_hr");
                    return rule;
                case LeafBlock leaf when leaf.Inline != null:
                    return BuildParagraph(leaf.Inline, tight);
                case ContainerBlock container:
                    var panel = new StackPanel();
                    foreach (var child in container)
                    {
                        var childControl = BuildBlock(child, tight);
                        if (childControl != null)
                            panel.Children.Add(childControl);
                    }
                    return panel;
                default:
                    return null;
            }
        }

        private Control BuildHeading(HeadingBlock heading)
        {
            var text = CreateTextBlock();
            text.Classes.Add("md_h" + Math.Clamp(heading.Level, 1, 6));
            BuildInlines(heading.Inline, text, default, false);

            var anchor = MakeAnchor(GetPlainText(heading.Inline));
            if (anchor.Length > 0)
                _options.RegisterAnchor?.Invoke(anchor, text);

            return text;
        }

        private Control BuildParagraph(ParagraphBlock paragraph, bool tight)
        {
            return BuildParagraph(paragraph.Inline, tight);
        }

        private Control BuildParagraph(ContainerInline inlines, bool tight)
        {
            var image = GetSoleImage(inlines);
            if (image != null)
                return BuildImage(image);

            var text = CreateTextBlock();
            text.Classes.Add("md_p");
            if (tight)
                text.Margin = default;

            BuildInlines(inlines, text, default, tight);
            return text;
        }

        private Control BuildList(ListBlock list, bool tight)
        {
            var panel = new StackPanel();
            var loose = !tight && list.IsLoose;

            foreach (var item in list)
            {
                if (item is not ListItemBlock listItem)
                    continue;

                var row = new Grid()
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    Margin = new Thickness(0, loose ? 3 : 1, 0, loose ? 3 : 1),
                };

                var marker = BuildListMarker(list, listItem);
                marker.Margin = new Thickness(0, 0, 8, 0);
                marker.VerticalAlignment = VerticalAlignment.Top;
                row.Children.Add(marker);

                var content = new StackPanel()
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };

                foreach (var child in listItem)
                {
                    var childControl = BuildBlock(child, true);
                    if (childControl != null)
                        content.Children.Add(childControl);
                }

                Grid.SetColumn(content, 1);
                row.Children.Add(content);
                panel.Children.Add(row);
            }

            return panel;
        }

        private Control BuildListMarker(ListBlock list, ListItemBlock item)
        {
            var task = GetTaskList(item);
            if (task != null)
                return CreateCheckBox(task.Checked);

            if (list.IsOrdered)
            {
                var index = item.Order > 0 ? item.Order : 1;
                var marker = new TextBlock() { Text = $"{index}." };
                marker.Classes.Add("md_list_marker");
                return marker;
            }

            var bullet = new TextBlock() { Text = "•" };
            bullet.Classes.Add("md_list_marker");
            return bullet;
        }

        private Control BuildQuote(QuoteBlock quote, bool tight)
        {
            var panel = new StackPanel();
            foreach (var child in quote)
            {
                var childControl = BuildBlock(child, false);
                if (childControl != null)
                    panel.Children.Add(childControl);
            }

            var border = new Border() { Child = panel };
            border.Classes.Add("md_quote");
            return border;
        }

        private Control BuildTable(Markdig.Extensions.Tables.Table table)
        {
            var columns = table.ColumnDefinitions?.Count ?? 0;
            if (columns <= 0)
                columns = 1;

            var grid = new Grid();
            for (var i = 0; i < columns; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { MinWidth = 80 });

            var rowIndex = 0;
            foreach (var child in table)
            {
                if (child is not TableRow row)
                    continue;

                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var cellIndex = 0;

                foreach (var cellObj in row)
                {
                    if (cellObj is not TableCell cell)
                        continue;

                    var column = cell.ColumnIndex >= 0 ? cell.ColumnIndex : cellIndex;
                    column = Math.Min(column, columns - 1);

                    var text = CreateTextBlock();
                    text.MaxWidth = 360;
                    text.Margin = default;

                    text.Classes.Add("md_table_text");
                    if (row.IsHeader)
                    {
                        text.FontWeight = FontWeight.Bold;
                        text.Classes.Add("md_table_header");
                    }

                    switch (table.ColumnDefinitions is { Count: > 0 } definitions
                                ? definitions[column].Alignment
                                : TableColumnAlign.Left)
                    {
                        case TableColumnAlign.Center:
                            text.TextAlignment = TextAlignment.Center;
                            break;
                        case TableColumnAlign.Right:
                            text.TextAlignment = TextAlignment.Right;
                            break;
                    }

                    foreach (var block in cell)
                    {
                        if (block is LeafBlock { Inline: not null } leaf)
                            BuildInlines(leaf.Inline, text, default, true);
                        else if (block is LeafBlock leafBlock)
                            AppendText(text, GetLines(leafBlock), "md_raw_html", default);
                    }

                    var border = new Border() { Child = text };
                    border.Classes.Add("md_table_cell");
                    if (row.IsHeader)
                        border.Classes.Add("md_table_header");

                    if (cell.ColumnSpan > 1)
                        Grid.SetColumnSpan(border, Math.Min(cell.ColumnSpan, columns - column));

                    Grid.SetRow(border, rowIndex);
                    Grid.SetColumn(border, column);
                    grid.Children.Add(border);

                    cellIndex++;
                }

                rowIndex++;
            }

            grid.HorizontalAlignment = HorizontalAlignment.Left;

            var wrapper = new Border() { Child = grid, HorizontalAlignment = HorizontalAlignment.Left };
            wrapper.Classes.Add("md_table");

            var scroll = new ScrollViewer()
            {
                Content = wrapper,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };

            return scroll;
        }

        private Control BuildCode(string info, string code)
        {
            var language = NormalizeLanguage(info);
            var lines = MarkdownCodeHighlighter.Highlight(code, language, _options.IsDark);

            var text = new SelectableTextBlock()
            {
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(10, 6, 10, 8),
            };
            text.Classes.Add("md_code");

            for (var i = 0; i < lines.Count; i++)
            {
                foreach (var segment in lines[i].Segments)
                {
                    var run = new Run(segment.Text);
                    if (segment.Foreground != null)
                        run.Foreground = segment.Foreground;
                    text.Inlines!.Add(run);
                }

                if (i + 1 < lines.Count)
                    text.Inlines!.Add(new LineBreak());
            }

            var header = new Grid()
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Height = 24,
                Margin = new Thickness(10, 4, 6, 0),
            };

            var label = new TextBlock()
            {
                Text = string.IsNullOrEmpty(language) ? "text" : language,
                VerticalAlignment = VerticalAlignment.Center,
            };
            label.Classes.Add("md_code_lang");
            header.Children.Add(label);

            var copy = new Button()
            {
                Classes = { "icon_button" },
                Width = 22,
                Height = 22,
                Padding = default,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };

            ToolTip.SetTip(copy, App.Text("Markdown.Copy"));
            if (Application.Current?.FindResource("Icons.Copy") is Geometry copyIcon)
            {
                copy.Content = new Path()
                {
                    Data = copyIcon,
                    Width = 11,
                    Height = 11,
                    Stretch = Stretch.Uniform,
                    Fill = ResolveBrush("Brush.FG2"),
                };
            }

            var codeText = code;
            copy.Click += async (_, e) =>
            {
                await copy.CopyTextAsync(codeText);
                e.Handled = true;
            };

            Grid.SetColumn(copy, 1);
            header.Children.Add(copy);

            var scroll = new ScrollViewer()
            {
                Content = text,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };

            var grid = new Grid() { RowDefinitions = new RowDefinitions("Auto,*") };
            grid.Children.Add(header);
            Grid.SetRow(scroll, 1);
            grid.Children.Add(scroll);

            var border = new Border() { Child = grid };
            border.Classes.Add("md_code_block");
            return border;
        }

        private Control BuildRawHtml(string html)
        {
            var text = new SelectableTextBlock()
            {
                Text = html,
                TextWrapping = TextWrapping.Wrap,
            };
            text.Classes.Add("md_raw_html");
            return text;
        }

        private Control BuildImage(LinkInline image)
        {
            var url = image.Url ?? string.Empty;
            var alt = GetPlainText(image);
            var link = MarkdownHelper.Classify(url, _options.RepositoryPath, _options.FileName);
            if (link.Kind == MarkdownLinkKind.File)
            {
                var bitmap = MarkdownImageCache.Get(link.FullPath);
                if (bitmap != null)
                {
                    var view = new Image()
                    {
                        Source = bitmap,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        MaxWidth = 900,
                        MaxHeight = 720,
                    };

                    if (!string.IsNullOrEmpty(alt))
                        ToolTip.SetTip(view, alt);

                    var border = new Border() { Child = view, Margin = new Thickness(0, 4, 0, 10) };
                    border.Classes.Add("md_image");
                    return border;
                }
            }

            var fallback = CreateTextBlock();
            fallback.Classes.Add("md_image_alt");
            var label = string.IsNullOrEmpty(alt) ? url : alt;
            var run = new Run($"[{label}]");
            fallback.Append(run);
            if (!string.IsNullOrEmpty(url))
                fallback.AddLink(0, run.Text!.Length, link);
            return fallback;
        }

        private MarkdownTextBlock CreateTextBlock()
        {
            var text = new MarkdownTextBlock() { TextWrapping = TextWrapping.Wrap };
            if (_options.OpenLink != null)
                text.LinkActivated += _options.OpenLink;

            return text;
        }

        private void BuildFootnotes(FootnoteGroup group, StackPanel host)
        {
            var rule = new Border();
            rule.Classes.Add("md_hr");
            host.Children.Add(rule);

            var title = new TextBlock() { Text = App.Text("Markdown.Footnotes") };
            title.Classes.Add("md_footnote_title");
            host.Children.Add(title);

            foreach (var item in group)
            {
                if (item is not Footnote footnote)
                    continue;

                var row = new Grid()
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    Margin = new Thickness(0, 2, 0, 2),
                };

                var labelText = footnote.Order > 0 ? footnote.Order.ToString() : footnote.Label;
                var label = new TextBlock() { Text = $"[{labelText}]" };
                label.Classes.Add("md_footnote_label");
                label.Margin = new Thickness(0, 0, 8, 0);
                row.Children.Add(label);

                var anchor = GetFootnoteAnchor(footnote);
                if (anchor.Length > 0)
                    _options.RegisterAnchor?.Invoke(anchor, label);

                var content = new StackPanel();
                foreach (var child in footnote)
                {
                    var control = BuildBlock(child, false);
                    if (control != null)
                        content.Children.Add(control);
                }

                Grid.SetColumn(content, 1);
                row.Children.Add(content);
                host.Children.Add(row);
            }
        }

        private void BuildInlines(ContainerInline container, MarkdownTextBlock target, RunStyle style, bool tight)
        {
            if (container == null)
                return;

            for (var inline = container.FirstChild; inline != null; inline = inline.NextSibling)
            {
                switch (inline)
                {
                    case LiteralInline literal:
                        AppendText(target, literal.Content.ToString(), null, style);
                        break;
                    case CodeInline code:
                        var codeStyle = style;
                        codeStyle.Code = true;
                        AppendText(target, code.Content, null, codeStyle);
                        break;
                    case EmphasisInline emphasis:
                        BuildEmphasis(emphasis, target, style, tight);
                        break;
                    case LinkInline imageLink when imageLink.IsImage:
                        BuildInlineImage(imageLink, target, style);
                        break;
                    case LinkInline link:
                        BuildLink(link, target, style, tight);
                        break;
                    case AutolinkInline autoLink:
                        var autoStyle = style;
                        autoStyle.Link = MarkdownHelper.Classify(autoLink.Url, _options.RepositoryPath, _options.FileName);
                        AppendText(target, autoLink.Url, "md_link", autoStyle);
                        break;
                    case LineBreakInline:
                        target.AppendLineBreak();
                        break;
                    case HtmlInline html:
                        AppendText(target, html.Tag, "md_raw_html", style);
                        break;
                    case TaskList:
                        break;
                    case FootnoteLink footnoteLink:
                        BuildFootnoteLink(footnoteLink, target, style);
                        break;
                    case ContainerInline nested:
                        BuildInlines(nested, target, style, tight);
                        break;
                    default:
                        AppendText(target, inline.ToString(), null, style);
                        break;
                }
            }
        }

        private void BuildEmphasis(EmphasisInline emphasis, MarkdownTextBlock target, RunStyle style, bool tight)
        {
            var nested = style;
            if (emphasis.DelimiterChar is '*' or '_')
            {
                if (emphasis.DelimiterCount >= 2)
                    nested.Bold = true;
                if (emphasis.DelimiterCount == 1 || emphasis.DelimiterCount == 3)
                    nested.Italic = true;
            }
            else if (emphasis.DelimiterChar == '~')
            {
                if (emphasis.DelimiterCount >= 2)
                    nested.Strike = true;
                else
                    nested.Subscript = true;
            }
            else if (emphasis.DelimiterChar == '^')
            {
                nested.Superscript = true;
            }
            else if (emphasis.DelimiterChar == '=' && emphasis.DelimiterCount >= 2)
            {
                nested.Marked = true;
            }

            BuildInlines(emphasis, target, nested, tight);
        }

        private void BuildLink(LinkInline link, MarkdownTextBlock target, RunStyle style, bool tight)
        {
            var nested = style;
            nested.Link = MarkdownHelper.Classify(link.Url, _options.RepositoryPath, _options.FileName);

            var start = target.InlineLength;
            BuildInlines(link, target, nested, tight);
            if (target.InlineLength == start)
                AppendText(target, link.Url ?? string.Empty, "md_link", nested);

            target.AddLink(start, target.InlineLength - start, nested.Link);
        }

        private void BuildInlineImage(LinkInline link, MarkdownTextBlock target, RunStyle style)
        {
            var nested = style;
            nested.Link = MarkdownHelper.Classify(link.Url, _options.RepositoryPath, _options.FileName);
            var alt = GetPlainText(link);

            if (nested.Link.Kind == MarkdownLinkKind.File)
            {
                var bitmap = MarkdownImageCache.Get(nested.Link.FullPath);
                if (bitmap != null)
                {
                    var view = new Image()
                    {
                        Source = bitmap,
                        Stretch = Stretch.Uniform,
                        MaxWidth = 640,
                        MaxHeight = 320,
                    };

                    if (!string.IsNullOrEmpty(alt))
                        ToolTip.SetTip(view, alt);

                    var border = new Border()
                    {
                        Child = view,
                        Margin = new Thickness(2, 0, 2, 0),
                        VerticalAlignment = VerticalAlignment.Bottom,
                    };

                    target.Inlines!.Add(new InlineUIContainer()
                    {
                        Child = border,
                        BaselineAlignment = BaselineAlignment.Bottom,
                    });
                    return;
                }
            }

            var start = target.InlineLength;
            var text = string.IsNullOrEmpty(alt) ? (link.Url ?? string.Empty) : alt;
            AppendText(target, text, "md_image_alt", nested);
            target.AddLink(start, target.InlineLength - start, nested.Link);
        }

        private void BuildFootnoteLink(FootnoteLink link, MarkdownTextBlock target, RunStyle style)
        {
            if (link.IsBackLink)
                return;

            var label = link.Footnote;
            if (label == null)
                return;

            var text = label.Order > 0 ? $"[{label.Order}]" : $"[{label.Label}]";
            var targetLink = new MarkdownLink()
            {
                Kind = MarkdownLinkKind.Anchor,
                Anchor = GetFootnoteAnchor(label),
                Url = $"#{GetFootnoteAnchor(label)}",
            };

            var start = target.InlineLength;
            AppendText(target, text, "md_footnote_ref", style);
            target.AddLink(start, target.InlineLength - start, targetLink);
        }

        private void AppendText(MarkdownTextBlock target, string text, string className, RunStyle style)
        {
            if (text == null)
                return;

            var run = new Run(text);
            if (!string.IsNullOrEmpty(className))
                run.Classes.Add(className);

            if (style.Bold)
                run.FontWeight = FontWeight.Bold;
            if (style.Italic)
                run.FontStyle = FontStyle.Italic;
            if (style.Strike)
                run.TextDecorations = TextDecorations.Strikethrough;
            if (style.Marked)
                run.Classes.Add("md_marked");
            if (style.Superscript)
            {
                run.BaselineAlignment = BaselineAlignment.Superscript;
                run.FontSize = 10;
            }
            else if (style.Subscript)
            {
                run.BaselineAlignment = BaselineAlignment.Subscript;
                run.FontSize = 10;
            }

            if (style.Code)
                run.Classes.Add("md_inline_code");
            if (style.Link != null && style.Link.Kind != MarkdownLinkKind.Blocked)
                run.Classes.Add("md_link");

            target.Append(run);
        }

        private Control CreateCheckBox(bool isChecked)
        {
            var box = new Border()
            {
                Width = 12,
                Height = 12,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 3, 0, 0),
            };
            box.Classes.Add("md_task_box");

            if (isChecked)
            {
                box.Background = ResolveBrush("Brush.Accent");
                box.BorderBrush = ResolveBrush("Brush.Accent");
                box.Child = new Path()
                {
                    Data = Geometry.Parse("M1,5 L4,8 L9,1"),
                    Stroke = Brushes.White,
                    StrokeThickness = 1.6,
                    Stretch = Stretch.Uniform,
                    Width = 8,
                    Height = 8,
                };
            }

            return box;
        }

        private TaskList GetTaskList(ListItemBlock item)
        {
            if (item.Count == 0 || item[0] is not ParagraphBlock paragraph)
                return null;

            for (var inline = paragraph.Inline?.FirstChild; inline != null; inline = inline.NextSibling)
            {
                if (inline is TaskList task)
                    return task;

                if (inline is LiteralInline literal && string.IsNullOrWhiteSpace(literal.Content.ToString()))
                    continue;

                break;
            }

            return null;
        }

        private static LinkInline GetSoleImage(ContainerInline inlines)
        {
            LinkInline image = null;
            for (var inline = inlines?.FirstChild; inline != null; inline = inline.NextSibling)
            {
                if (inline is LinkInline { IsImage: true } link)
                {
                    if (image != null)
                        return null;

                    image = link;
                }
                else if (inline is LiteralInline literal && string.IsNullOrWhiteSpace(literal.Content.ToString()))
                {
                    continue;
                }
                else if (inline is LineBreakInline)
                {
                    continue;
                }
                else
                {
                    return null;
                }
            }

            return image;
        }

        private static string GetPlainText(ContainerInline container)
        {
            var builder = new StringBuilder();
            if (container != null)
                AppendPlainText(builder, container);

            return builder.ToString().Trim();
        }

        private static void AppendPlainText(StringBuilder builder, ContainerInline container)
        {
            for (var inline = container.FirstChild; inline != null; inline = inline.NextSibling)
            {
                switch (inline)
                {
                    case LiteralInline literal:
                        builder.Append(literal.Content.ToString());
                        break;
                    case CodeInline code:
                        builder.Append(code.Content);
                        break;
                    case LineBreakInline:
                        builder.Append(' ');
                        break;
                    case ContainerInline nested:
                        AppendPlainText(builder, nested);
                        break;
                    case AutolinkInline autoLink:
                        builder.Append(autoLink.Url);
                        break;
                }
            }
        }

        private string MakeAnchor(string text)
        {
            var slug = MarkdownHelper.Slugify(text);
            if (slug.Length == 0)
                return string.Empty;

            if (_anchors.TryGetValue(slug, out var count))
            {
                count++;
                _anchors[slug] = count;
                return $"{slug}-{count}";
            }

            _anchors[slug] = 0;
            return slug;
        }

        private static string GetFootnoteAnchor(Footnote footnote)
        {
            var label = string.IsNullOrEmpty(footnote.Label) ? footnote.Order.ToString() : footnote.Label;
            return "fn-" + MarkdownHelper.Slugify(label);
        }

        private static string NormalizeLanguage(string info)
        {
            if (string.IsNullOrWhiteSpace(info))
                return string.Empty;

            var value = info.Trim();
            var cut = value.IndexOfAny([' ', '\t', ',', ';', '{']);
            return cut > 0 ? value[..cut].ToLowerInvariant() : value.ToLowerInvariant();
        }

        private static string GetLines(LeafBlock block)
        {
            var lines = block.Lines;
            if (lines.Count == 0)
                return string.Empty;

            var builder = new StringBuilder();
            for (var i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                    builder.Append('\n');

                builder.Append(lines.Lines[i].ToString());
            }

            return builder.ToString();
        }

        private static IBrush ResolveBrush(string key)
        {
            return Application.Current?.FindResource(key) as IBrush ?? Brushes.Transparent;
        }

        private struct RunStyle
        {
            public bool Bold;
            public bool Italic;
            public bool Strike;
            public bool Code;
            public bool Marked;
            public bool Superscript;
            public bool Subscript;
            public MarkdownLink Link;
        }

        private readonly MarkdownRenderOptions _options;
        private readonly Dictionary<string, int> _anchors = new(StringComparer.Ordinal);
    }
}
