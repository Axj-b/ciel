using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SourceGit.Models
{
    /// <summary>
    ///     Entry point for native mermaid rendering. Returns null whenever the diagram cannot be
    ///     rendered faithfully, so the markdown renderer falls back to a plain code block.
    /// </summary>
    public static class MermaidDiagram
    {
        public static Control TryBuild(string code)
        {
            try
            {
                var pie = MermaidPie.Parse(code);
                if (pie != null)
                    return new MermaidPieControl(pie, ResolveTheme(), ResolveTypeface());

                var gantt = MermaidGantt.Parse(code);
                if (gantt != null)
                    return new MermaidGanttControl(gantt, ResolveTheme(), ResolveTypeface());

                var graph = MermaidFlowchart.Parse(code);
                if (graph == null)
                    graph = MermaidState.Parse(code);
                if (graph == null)
                    graph = MermaidClass.Parse(code);
                if (graph == null)
                    graph = MermaidEr.Parse(code);

                if (graph == null)
                {
                    var sequence = MermaidSequence.Parse(code);
                    if (sequence == null)
                        return null;

                    return new MermaidSequenceControl(sequence, ResolveTheme(), ResolveTypeface());
                }

                var theme = ResolveTheme();
                var typeface = ResolveTypeface();
                var bold = new Typeface(typeface.FontFamily, FontStyle.Normal, FontWeight.SemiBold);
                var italic = new Typeface(typeface.FontFamily, FontStyle.Italic);

                var view = new MermaidGraphView();
                var nodeSizes = new Dictionary<MermaidNode, Size>();
                foreach (var node in graph.Nodes)
                    nodeSizes[node] = MeasureNode(node, view, typeface, bold, italic, theme);

                var labelSizes = new Dictionary<MermaidEdge, Size>();
                foreach (var edge in graph.Edges)
                {
                    if (!string.IsNullOrEmpty(edge.Label))
                    {
                        var brush = edge.Style?.TextColor is { } color ? new SolidColorBrush(color) : theme.EdgeLabelText;
                        var label = CreateText(CleanLabel(edge.Label, edge.Label), typeface, 11.5, brush, 200, true);
                        view.EdgeLabels[edge] = label;
                        labelSizes[edge] = new Size(label.Text.Width + 10, label.Text.Height + 4);
                    }

                    if (!string.IsNullOrEmpty(edge.TailLabel))
                        view.TailLabels[edge] = CreateText(edge.TailLabel, typeface, 10.5, theme.EdgeLabelText, 80, false);
                    if (!string.IsNullOrEmpty(edge.HeadLabel))
                        view.HeadLabels[edge] = CreateText(edge.HeadLabel, typeface, 10.5, theme.EdgeLabelText, 80, false);
                }

                var titleSizes = new Dictionary<MermaidSubgraph, Size>();
                foreach (var subgraph in graph.Subgraphs)
                {
                    var title = CreateText(CleanLabel(subgraph.Title, subgraph.Id), bold, 12, theme.SubgraphTitle, 320, false);
                    view.Titles[subgraph] = title;
                    titleSizes[subgraph] = new Size(title.Text.Width, title.Text.Height);
                }

                view.Layout = MermaidGraphLayout.Layout(graph, nodeSizes, labelSizes, titleSizes);
                if (view.Layout == null)
                    return null;

                return new MermaidGraphControl(view, theme);
            }
            catch
            {
                return null;
            }
        }

        private static Size MeasureNode(MermaidNode node, MermaidGraphView view, Typeface typeface, Typeface bold, Typeface italic, MermaidTheme theme)
        {
            if (node.Shape is MermaidNodeShape.StartMarker or MermaidNodeShape.EndMarker)
                return new Size(18, 18);

            if (node.Shape == MermaidNodeShape.ClassBox)
            {
                var title = CreateText(CleanLabel(node.Label, node.Id), bold, 12.5, NodeTextBrush(node, theme), 260, false);
                var table = new MermaidTableDraw();
                if (!string.IsNullOrEmpty(node.Annotation))
                    table.Annotation = CreateText($"\u00AB{node.Annotation}\u00BB", italic, 10.5, theme.SubgraphTitle, 260, false);

                foreach (var row in node.Rows)
                {
                    table.Rows.Add(new MermaidTableRowDraw()
                    {
                        Text = CreateText(row.Text, typeface, 11.5, theme.NodeText, 320, false),
                        Value = string.IsNullOrEmpty(row.Value)
                            ? null
                            : CreateText(row.Value, typeface, 10.5, theme.SubgraphTitle, 160, false),
                        Separator = row.Separator,
                    });
                }

                view.Labels[node] = title;
                view.Tables[node] = table;
                return MermaidTable.Measure(title, table);
            }

            var label = CreateText(CleanLabel(node.Label, node.Id), typeface, 13, NodeTextBrush(node, theme), 240, true);
            view.Labels[node] = label;
            return ShapeSize(node.Shape, label.Text.Width, label.Text.Height);
        }

        private static MermaidLabel CreateText(string text, Typeface typeface, double fontSize, IBrush brush, double maxWidth, bool centered)
        {
            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                brush)
            {
                MaxTextWidth = maxWidth,
                TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left,
            };

            return new MermaidLabel() { Text = formatted, BoxWidth = centered ? maxWidth : 0 };
        }

        private static string CleanLabel(string label, string fallback)
        {
            var text = string.IsNullOrEmpty(label) ? fallback : label;
            text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<[^>]+>", string.Empty);
            text = text.Trim();
            return text.Length == 0 ? fallback : text;
        }

        private static Size ShapeSize(MermaidNodeShape shape, double textWidth, double textHeight)
        {
            double width, height;
            switch (shape)
            {
                case MermaidNodeShape.Stadium:
                    height = textHeight + 14;
                    width = textWidth + 2 * Math.Max(12, height / 2);
                    break;
                case MermaidNodeShape.Subroutine:
                    width = textWidth + 40;
                    height = textHeight + 14;
                    break;
                case MermaidNodeShape.Cylinder:
                    width = textWidth + 24;
                    height = textHeight + 24;
                    break;
                case MermaidNodeShape.Circle:
                    var diameter = Math.Sqrt(textWidth * textWidth + textHeight * textHeight) + 18;
                    width = height = diameter;
                    break;
                case MermaidNodeShape.Diamond:
                    width = 1.5 * textWidth + 26;
                    height = 1.6 * textHeight + 22;
                    break;
                case MermaidNodeShape.Hexagon:
                    width = textWidth + 44;
                    height = textHeight + 14;
                    break;
                case MermaidNodeShape.Parallelogram:
                case MermaidNodeShape.ParallelogramAlt:
                case MermaidNodeShape.Trapezoid:
                case MermaidNodeShape.TrapezoidAlt:
                    width = textWidth + 48;
                    height = textHeight + 14;
                    break;
                case MermaidNodeShape.Asymmetric:
                    width = textWidth + 34;
                    height = textHeight + 14;
                    break;
                default:
                    width = textWidth + 24;
                    height = textHeight + 14;
                    break;
            }

            return new Size(Math.Max(36, width), Math.Max(28, height));
        }

        private static IBrush NodeTextBrush(MermaidNode node, MermaidTheme theme)
        {
            var style = node.ResolvedStyle;
            if (style?.TextColor is { } explicitColor)
                return new SolidColorBrush(explicitColor);

            if (style?.Fill is { } fill)
            {
                var luminance = (0.299 * fill.R + 0.587 * fill.G + 0.114 * fill.B) / 255.0;
                return new SolidColorBrush(luminance > 0.55 ? Color.Parse("#1F1F1F") : Color.Parse("#EFEFEF"));
            }

            return theme.NodeText;
        }

        private static Typeface ResolveTypeface()
        {
            var family = Application.Current?.FindResource("Fonts.Default") as FontFamily ?? FontFamily.Default;
            return new Typeface(family);
        }

        private static MermaidTheme ResolveTheme()
        {
            var badge = Resolve("Brush.Badge") as ISolidColorBrush;
            return new MermaidTheme()
            {
                NodeFill = Resolve("Brush.Contents"),
                NodeStroke = Resolve("Brush.Border1"),
                NodeText = Resolve("Brush.FG1"),
                Edge = Resolve("Brush.FG2"),
                EdgeLabelBg = Resolve("Brush.SubPanel"),
                EdgeLabelText = Resolve("Brush.FG2"),
                SubgraphFill = badge != null ? new SolidColorBrush(badge.Color, 0.16) : Brushes.Transparent,
                SubgraphStroke = Resolve("Brush.Border2"),
                SubgraphTitle = Resolve("Brush.FG2"),
            };
        }

        private static IBrush Resolve(string key)
        {
            return Application.Current?.FindResource(key) as IBrush ?? Brushes.Transparent;
        }
    }
}
