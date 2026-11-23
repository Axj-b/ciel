using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

using Avalonia.Media;

namespace SourceGit.Models
{
    /// <summary>
    ///     Parser for the mermaid flowchart/graph syntax. Anything that cannot be fully understood
    ///     returns null so the caller falls back to a plain code block instead of a wrong diagram.
    /// </summary>
    public static class MermaidFlowchart
    {
        public static MermaidGraph Parse(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > 64 * 1024)
                return null;

            try
            {
                return new Parser(code).Run();
            }
            catch
            {
                return null;
            }
        }

        private class Parser
        {
            public Parser(string code)
            {
                _code = code.Replace("\r\n", "\n").Replace('\r', '\n');
            }

            public MermaidGraph Run()
            {
                var lines = _code.Split('\n');
                var start = SkipFrontmatter(lines);
                if (start < 0)
                    return null;

                var statements = new List<string>();
                for (var i = start; i < lines.Length; i++)
                    SplitStatements(StripComment(lines[i]), statements);

                if (statements.Count == 0)
                    return null;

                // Header: flowchart/graph plus an optional direction.
                var match = Regex.Match(statements[0], @"^(?:flowchart|graph)(?:\s+(TD|TB|LR|RL|BT))?$", RegexOptions.IgnoreCase);
                if (!match.Success)
                    return null;

                var graph = new MermaidGraph();
                var direction = match.Groups[1].Success ? match.Groups[1].Value.ToUpperInvariant() : "TD";
                graph.Direction = direction == "TB" ? "TD" : direction;

                var scope = new Stack<MermaidSubgraph>();
                for (var i = 1; i < statements.Count; i++)
                {
                    if (!ParseStatement(graph, statements[i], scope))
                        return null;
                }

                if (scope.Count > 0 || graph.Nodes.Count == 0 || graph.Nodes.Count > 400 || graph.Edges.Count > 1200)
                    return null;

                ResolveStyles(graph);
                return graph;
            }

            private bool ParseStatement(MermaidGraph graph, string statement, Stack<MermaidSubgraph> scope)
            {
                var parent = scope.Count > 0 ? scope.Peek() : null;

                if (statement.Equals("end", StringComparison.OrdinalIgnoreCase))
                {
                    if (scope.Count == 0)
                        return false;

                    scope.Pop();
                    return true;
                }

                var keyword = Regex.Match(statement, @"^[A-Za-z]+").Value;
                switch (keyword.ToLowerInvariant())
                {
                    case "subgraph":
                        return ParseSubgraph(graph, statement[8..].Trim(), parent, scope);
                    case "direction":
                        return Regex.IsMatch(statement, @"^direction\s+(TD|TB|LR|RL|BT)$", RegexOptions.IgnoreCase);
                    case "classdef":
                        return ParseClassDef(graph, statement[8..].Trim());
                    case "class":
                        return ParseClassAssignment(graph, statement[5..].Trim());
                    case "style":
                        return ParseStyleStatement(graph, statement[5..].Trim());
                    case "linkstyle":
                        return ParseLinkStyle(graph, statement[9..].Trim());
                    case "acctitle":
                    case "accdescr":
                        return true;
                    case "click":
                    case "href":
                    case "callback":
                        return false;
                }

                return ParseChain(graph, statement, parent);
            }

            private bool ParseSubgraph(MermaidGraph graph, string rest, MermaidSubgraph parent, Stack<MermaidSubgraph> scope)
            {
                if (rest.Length == 0)
                    return false;

                string id, title;
                var bracket = Regex.Match(rest, @"^([\w-]+)\s*\[(.*)\]$", RegexOptions.Singleline);
                if (bracket.Success)
                {
                    id = bracket.Groups[1].Value;
                    title = Unquote(bracket.Groups[2].Value.Trim());
                }
                else
                {
                    if (rest.IndexOfAny(['[', ']', '{', '}', '(', ')', '"']) >= 0)
                        return false;

                    id = title = rest;
                }

                var subgraph = new MermaidSubgraph() { Id = id, Title = DecodeEntities(title), Parent = parent };
                graph.Subgraphs.Add(subgraph);
                parent?.Children.Add(subgraph);
                scope.Push(subgraph);
                return true;
            }

            private bool ParseClassDef(MermaidGraph graph, string rest)
            {
                var split = rest.IndexOfAny([' ', '\t']);
                if (split <= 0)
                    return false;

                var name = rest[..split];
                graph.ClassStyles[name] = ParseStyles(rest[(split + 1)..]);
                return true;
            }

            private bool ParseClassAssignment(MermaidGraph graph, string rest)
            {
                var parts = rest.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                    return false;

                var names = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var id in parts[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!graph.NodeMap.TryGetValue(id, out var node))
                        return false;

                    foreach (var name in names)
                        node.Classes.Add(name);
                }

                return true;
            }

            private bool ParseStyleStatement(MermaidGraph graph, string rest)
            {
                var parts = rest.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                    return false;

                var style = ParseStyles(parts[1]);
                foreach (var id in parts[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var node = graph.GetOrCreateNode(id, null);
                    node.Style = Merge(node.Style, style);
                }

                return true;
            }

            private bool ParseLinkStyle(MermaidGraph graph, string rest)
            {
                var parts = rest.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                    return false;

                var style = ParseStyles(parts[1]);
                if (parts[0].Equals("default", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var edge in graph.Edges)
                        edge.Style = Merge(edge.Style, style);
                    return true;
                }

                foreach (var index in parts[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!int.TryParse(index, out var i) || i < 0 || i >= graph.Edges.Count)
                        return false;

                    graph.Edges[i].Style = Merge(graph.Edges[i].Style, style);
                }

                return true;
            }

            private bool ParseChain(MermaidGraph graph, string statement, MermaidSubgraph parent)
            {
                var pos = 0;
                var group = ParseNodeGroup(graph, statement, ref pos, parent);
                if (group == null)
                    return false;

                while (true)
                {
                    SkipWhitespace(statement, ref pos);
                    if (pos == statement.Length)
                        return true;

                    var edge = ParseEdge(statement, ref pos);
                    if (edge == null)
                        return false;

                    var next = ParseNodeGroup(graph, statement, ref pos, parent);
                    if (next == null)
                        return false;

                    foreach (var from in group)
                    {
                        foreach (var to in next)
                        {
                            graph.Edges.Add(new MermaidEdge()
                            {
                                From = from,
                                To = to,
                                Label = edge.Label,
                                Line = edge.Line,
                                Head = edge.Head,
                                TailArrow = edge.TailArrow,
                                Span = edge.Span,
                            });
                        }
                    }

                    group = next;
                }
            }

            private List<MermaidNode> ParseNodeGroup(MermaidGraph graph, string s, ref int pos, MermaidSubgraph parent)
            {
                var group = new List<MermaidNode>();
                while (true)
                {
                    var node = ParseNodeRef(graph, s, ref pos, parent);
                    if (node == null)
                        return null;

                    group.Add(node);
                    SkipWhitespace(s, ref pos);
                    if (pos >= s.Length || s[pos] != '&')
                        return group;

                    pos++;
                }
            }

            private MermaidNode ParseNodeRef(MermaidGraph graph, string s, ref int pos, MermaidSubgraph parent)
            {
                SkipWhitespace(s, ref pos);
                var idStart = pos;
                while (pos < s.Length && IsIdChar(s[pos]))
                {
                    // A hyphen starts an edge when followed by another link marker (A---B, A-.->B).
                    if (s[pos] == '-' && pos + 1 < s.Length && s[pos + 1] is '-' or '=' or '.' or '>')
                        break;

                    pos++;
                }

                if (pos == idStart)
                    return null;

                var id = s[idStart..pos];
                var node = graph.GetOrCreateNode(id, parent);

                var shapeStart = pos;
                SkipWhitespace(s, ref pos);
                if (!ParseShape(s, ref pos, out var shape, out var label))
                {
                    pos = shapeStart;
                }
                else
                {
                    node.Shape = shape;
                    node.Label = label;
                }

                // :::className attachments.
                while (pos + 2 < s.Length && s[pos] == ':' && s[pos + 1] == ':' && s[pos + 2] == ':')
                {
                    pos += 3;
                    var classStart = pos;
                    while (pos < s.Length && IsIdChar(s[pos]))
                        pos++;

                    if (pos == classStart)
                        return null;

                    node.Classes.Add(s[classStart..pos]);
                }

                return node;
            }

            private bool ParseShape(string s, ref int pos, out MermaidNodeShape shape, out string label)
            {
                shape = MermaidNodeShape.Rectangle;
                label = null;

                if (pos >= s.Length)
                    return false;

                string open, close, closeAlt = null;
                var two = pos + 1 < s.Length ? s.Substring(pos, 2) : string.Empty;
                switch (two)
                {
                    case "[[": open = "[["; close = "]]"; shape = MermaidNodeShape.Subroutine; break;
                    case "[(": open = "[("; close = ")]"; shape = MermaidNodeShape.Cylinder; break;
                    case "([": open = "(["; close = "])"; shape = MermaidNodeShape.Stadium; break;
                    case "((": open = "(("; close = "))"; shape = MermaidNodeShape.Circle; break;
                    case "{{": open = "{{"; close = "}}"; shape = MermaidNodeShape.Hexagon; break;
                    case "[/": open = "[/"; close = "/]"; closeAlt = "\\]"; shape = MermaidNodeShape.Parallelogram; break;
                    case "[\\": open = "[\\"; close = "\\]"; closeAlt = "/]"; shape = MermaidNodeShape.ParallelogramAlt; break;
                    default:
                        switch (s[pos])
                        {
                            case '[': open = "["; close = "]"; shape = MermaidNodeShape.Rectangle; break;
                            case '(': open = "("; close = ")"; shape = MermaidNodeShape.Rounded; break;
                            case '{': open = "{"; close = "}"; shape = MermaidNodeShape.Diamond; break;
                            case '>': open = ">"; close = "]"; shape = MermaidNodeShape.Asymmetric; break;
                            default: return false;
                        }
                        break;
                }

                var contentStart = pos + open.Length;
                if (contentStart >= s.Length)
                    return false;

                string content;
                int contentEnd;
                if (s[contentStart] == '"')
                {
                    var quoteEnd = s.IndexOf('"', contentStart + 1);
                    if (quoteEnd < 0)
                        return false;

                    content = s[(contentStart + 1)..quoteEnd];
                    var afterQuote = quoteEnd + 1;
                    if (!s.AsSpan(afterQuote).StartsWith(close) &&
                        (closeAlt == null || !s.AsSpan(afterQuote).StartsWith(closeAlt)))
                        return false;

                    contentEnd = quoteEnd;
                    pos = afterQuote + (s.AsSpan(afterQuote).StartsWith(close) ? close.Length : closeAlt!.Length);
                }
                else
                {
                    var closeIndex = s.IndexOf(close, contentStart, StringComparison.Ordinal);
                    if (closeAlt != null)
                    {
                        var altIndex = s.IndexOf(closeAlt, contentStart, StringComparison.Ordinal);
                        if (altIndex >= 0 && (closeIndex < 0 || altIndex < closeIndex))
                        {
                            // [/text\] and [\text/] are trapezoids.
                            shape = shape == MermaidNodeShape.Parallelogram ? MermaidNodeShape.Trapezoid : MermaidNodeShape.TrapezoidAlt;
                            closeIndex = altIndex;
                            close = closeAlt;
                        }
                    }

                    if (closeIndex < 0)
                        return false;

                    content = s[contentStart..closeIndex];
                    contentEnd = closeIndex;
                    pos = closeIndex + close.Length;
                }

                if (contentEnd < contentStart)
                    return false;

                label = DecodeEntities(content.Trim());
                return true;
            }

            private MermaidEdge ParseEdge(string s, ref int pos)
            {
                SkipWhitespace(s, ref pos);
                var start = pos;

                var tail = false;
                if (pos + 1 < s.Length && s[pos] == '<' && (s[pos + 1] == '-' || s[pos + 1] == '='))
                {
                    tail = true;
                    pos++;
                }

                if (pos >= s.Length || (s[pos] != '-' && s[pos] != '='))
                {
                    pos = start;
                    return null;
                }

                var marker = s[pos];
                var line = marker == '=' ? MermaidLineStyle.Thick : MermaidLineStyle.Solid;
                var run = CountRun(s, ref pos, marker);
                var head = MermaidEndKind.None;
                var span = 1;
                string label = null;

                if (marker == '-' && pos < s.Length && s[pos] == '.')
                {
                    // Dotted family: -.-> / -.- / -. text .->
                    line = MermaidLineStyle.Dotted;
                    var dots = CountRun(s, ref pos, '.');
                    span = Math.Max(1, dots);

                    if (pos < s.Length && s[pos] == '-')
                    {
                        CountRun(s, ref pos, '-');
                        head = ReadHead(s, ref pos);
                    }
                    else
                    {
                        var labelStart = pos;
                        var i = pos;
                        while (true)
                        {
                            while (i < s.Length && s[i] != '.')
                                i++;

                            if (i >= s.Length)
                            {
                                pos = start;
                                return null;
                            }

                            var afterDots = i;
                            while (afterDots < s.Length && s[afterDots] == '.')
                                afterDots++;

                            if (afterDots < s.Length && s[afterDots] == '-')
                                break;

                            i = afterDots;
                        }

                        label = s[labelStart..i].Trim();
                        pos = i;
                        CountRun(s, ref pos, '.');
                        CountRun(s, ref pos, '-');
                        head = ReadHead(s, ref pos);
                    }
                }
                else if (pos < s.Length && IsHeadChar(s[pos]))
                {
                    head = ReadHead(s, ref pos);
                    span = Math.Max(1, run - 1);
                }
                else
                {
                    // Either a plain line (--- / ===) or an inline label (-- text -->).
                    var afterRun = pos;
                    SkipWhitespace(s, ref pos);

                    if (run == 2)
                    {
                        // Two markers must be the start of a labelled link.
                        var i = pos;
                        while (true)
                        {
                            while (i < s.Length && s[i] != marker)
                                i++;

                            if (i >= s.Length)
                            {
                                pos = start;
                                return null;
                            }

                            var runEnd = i;
                            while (runEnd < s.Length && s[runEnd] == marker)
                                runEnd++;

                            if (runEnd - i >= 2)
                                break;

                            i = runEnd;
                        }

                        label = s[afterRun..i].Trim();
                        if (label.Length == 0)
                        {
                            pos = start;
                            return null;
                        }

                        var secondRun = runEndOf(i, s, marker);
                        pos = secondRun;
                        head = ReadHead(s, ref pos);
                        span = Math.Max(1, secondRun - i - (head == MermaidEndKind.None ? 2 : 1));
                    }
                    else
                    {
                        // Plain line. A second marker run later in the statement would make this
                        // ambiguous (long labelled links), so fall back instead of guessing.
                        if (s[pos..].Contains(marker == '-' ? "--" : "=="))
                        {
                            pos = start;
                            return null;
                        }

                        pos = afterRun;
                        span = Math.Max(1, run - 2);
                    }
                }

                // Optional |label| suffix.
                SkipWhitespace(s, ref pos);
                if (pos < s.Length && s[pos] == '|')
                {
                    var end = s.IndexOf('|', pos + 1);
                    if (end < 0 || label != null)
                    {
                        pos = start;
                        return null;
                    }

                    label = s[(pos + 1)..end].Trim();
                    pos = end + 1;
                }

                if (label != null)
                    label = DecodeEntities(Unquote(label));

                if (tail && head == MermaidEndKind.None)
                    head = MermaidEndKind.Arrow;

                return new MermaidEdge() { Line = line, Head = head, TailArrow = tail, Span = span, Label = label };
            }

            private static int runEndOf(int start, string s, char marker)
            {
                var i = start;
                while (i < s.Length && s[i] == marker)
                    i++;
                return i;
            }

            private static MermaidEndKind ReadHead(string s, ref int pos)
            {
                if (pos >= s.Length)
                    return MermaidEndKind.None;

                switch (s[pos])
                {
                    case '>': pos++; return MermaidEndKind.Arrow;
                    case 'x': pos++; return MermaidEndKind.Cross;
                    case 'o': pos++; return MermaidEndKind.Circle;
                    default: return MermaidEndKind.None;
                }
            }

            private static bool IsHeadChar(char c) => c is '>' or 'x' or 'o';

            private static int CountRun(string s, ref int pos, char c)
            {
                var start = pos;
                while (pos < s.Length && s[pos] == c)
                    pos++;
                return pos - start;
            }

            private static void SkipWhitespace(string s, ref int pos)
            {
                while (pos < s.Length && char.IsWhiteSpace(s[pos]))
                    pos++;
            }

            private static bool IsIdChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-';

            private static string Unquote(string text)
            {
                return text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;
            }

            private static string DecodeEntities(string text)
            {
                if (!text.Contains('#'))
                    return text;

                return Regex.Replace(text, @"#(quot|amp|lt|gt|semi|\d+);", m => m.Groups[1].Value switch
                {
                    "quot" => "\"",
                    "amp" => "&",
                    "lt" => "<",
                    "gt" => ">",
                    "semi" => ";",
                    _ => char.ConvertFromUtf32(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)),
                });
            }

            private static string StripComment(string line)
            {
                var inQuote = false;
                for (var i = 0; i < line.Length; i++)
                {
                    var c = line[i];
                    if (c == '"')
                        inQuote = !inQuote;
                    else if (!inQuote && c == '%' && i + 1 < line.Length && line[i + 1] == '%')
                        return line[..i];
                }

                return line;
            }

            private static int SkipFrontmatter(string[] lines)
            {
                var first = -1;
                for (var i = 0; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                        continue;

                    first = i;
                    break;
                }

                if (first < 0)
                    return -1;

                if (lines[first].Trim() != "---")
                    return first;

                for (var i = first + 1; i < lines.Length; i++)
                {
                    if (lines[i].Trim() == "---")
                        return i + 1 < lines.Length ? i + 1 : -1;
                }

                return -1;
            }

            private static void SplitStatements(string line, List<string> output)
            {
                var depth = 0;
                var inQuote = false;
                var start = 0;
                for (var i = 0; i < line.Length; i++)
                {
                    var c = line[i];
                    if (c == '"')
                        inQuote = !inQuote;
                    else if (!inQuote)
                    {
                        if (c is '[' or '(' or '{')
                            depth++;
                        else if (c is ']' or ')' or '}')
                            depth = Math.Max(0, depth - 1);
                        else if (c == ';' && depth == 0)
                        {
                            AddStatement(line[start..i], output);
                            start = i + 1;
                        }
                    }
                }

                AddStatement(line[start..], output);
            }

            private static void AddStatement(string text, List<string> output)
            {
                var trimmed = text.Trim();
                if (trimmed.Length > 0)
                    output.Add(trimmed);
            }

            private static MermaidNodeStyle ParseStyles(string text)
            {
                var style = new MermaidNodeStyle();
                foreach (var part in Regex.Split(text, @",(?=[A-Za-z-]+\s*:)"))
                {
                    var colon = part.IndexOf(':');
                    if (colon <= 0)
                        continue;

                    var key = part[..colon].Trim().ToLowerInvariant();
                    var value = part[(colon + 1)..].Trim();
                    switch (key)
                    {
                        case "fill":
                            if (Color.TryParse(value, out var fill))
                                style.Fill = fill;
                            break;
                        case "stroke":
                            if (Color.TryParse(value, out var stroke))
                                style.Stroke = stroke;
                            break;
                        case "color":
                            if (Color.TryParse(value, out var color))
                                style.TextColor = color;
                            break;
                        case "stroke-width":
                            var width = value.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? value[..^2] : value;
                            if (double.TryParse(width, NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
                                style.StrokeWidth = Math.Clamp(w, 0.5, 12);
                            break;
                        case "stroke-dasharray":
                            var pieces = value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
                            var dashes = new List<double>();
                            foreach (var piece in pieces)
                            {
                                if (double.TryParse(piece, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                                    dashes.Add(Math.Clamp(d, 1, 20));
                            }

                            if (dashes.Count > 0)
                                style.DashArray = dashes.ToArray();
                            break;
                    }
                }

                return style;
            }

            private static MermaidNodeStyle Merge(MermaidNodeStyle existing, MermaidNodeStyle added)
            {
                if (existing == null)
                    return added;
                if (added == null)
                    return existing;

                return new MermaidNodeStyle()
                {
                    Fill = added.Fill ?? existing.Fill,
                    Stroke = added.Stroke ?? existing.Stroke,
                    StrokeWidth = added.StrokeWidth ?? existing.StrokeWidth,
                    TextColor = added.TextColor ?? existing.TextColor,
                    DashArray = added.DashArray ?? existing.DashArray,
                };
            }

            private static void ResolveStyles(MermaidGraph graph)
            {
                foreach (var node in graph.Nodes)
                {
                    MermaidNodeStyle resolved = null;
                    foreach (var name in node.Classes)
                    {
                        if (graph.ClassStyles.TryGetValue(name, out var classStyle))
                            resolved = Merge(resolved, classStyle);
                    }

                    if (graph.ClassStyles.TryGetValue("default", out var defaultStyle))
                        resolved = Merge(defaultStyle, resolved);

                    node.ResolvedStyle = Merge(resolved, node.Style);
                }
            }

            private readonly string _code;
        }
    }
}
