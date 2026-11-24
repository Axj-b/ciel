using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SourceGit.Models
{
    /// <summary>
    ///     Parser for the mermaid classDiagram syntax: classes with attribute/method compartments,
    ///     annotations and inheritance/composition/aggregation/association/dependency relations.
    ///     Anything else returns null so the caller falls back to a plain code block.
    /// </summary>
    public static class MermaidClass
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
                _code = code;
            }

            public MermaidGraph Run()
            {
                var statements = MermaidSource.Statements(_code);
                if (statements == null || statements.Count == 0)
                    return null;

                if (!Regex.IsMatch(statements[0], @"^classDiagram(?:-v2)?$", RegexOptions.IgnoreCase))
                    return null;

                var graph = new MermaidGraph() { Direction = "TD" };
                for (var i = 1; i < statements.Count; i++)
                {
                    if (!ParseStatement(graph, statements[i]))
                        return null;
                }

                if (_current != null || graph.Nodes.Count == 0 || graph.Nodes.Count > 200 || graph.Edges.Count > 600)
                    return null;

                foreach (var node in graph.Nodes)
                    ClassifyRows(node);

                return graph;
            }

            private bool ParseStatement(MermaidGraph graph, string statement)
            {
                if (statement == "}")
                {
                    if (_current == null)
                        return false;

                    _current = null;
                    return true;
                }

                if (_current != null)
                {
                    var stereotype = Regex.Match(statement, @"^<<([^>]+)>>$");
                    if (stereotype.Success)
                    {
                        if (_current.Annotation != null || _current.Rows.Count > 0)
                            return false;

                        _current.Annotation = stereotype.Groups[1].Value.Trim();
                        return true;
                    }

                    if (statement.Contains('{') || statement.Contains('}') || statement.StartsWith(':'))
                        return false;

                    _current.Rows.Add(new MermaidTableRow() { Text = statement });
                    return true;
                }

                var keyword = MermaidSource.Keyword(statement);
                switch (keyword)
                {
                    case "class":
                        return ParseClass(graph, statement[5..].Trim());
                    case "direction":
                    {
                        var direction = statement[9..].Trim().ToUpperInvariant();
                        if (direction is not ("TD" or "TB" or "LR" or "RL" or "BT"))
                            return false;

                        graph.Direction = direction == "TB" ? "TD" : direction;
                        return true;
                    }
                    case "note":
                    case "namespace":
                    case "click":
                    case "link":
                    case "callback":
                    case "cssclass":
                    case "style":
                    case "classdef":
                    case "acctitle":
                    case "accdescr":
                        return false;
                }

                if (ParseRelation(graph, statement))
                    return true;

                var annotation = Regex.Match(statement, @"^<<([^>]+)>>\s+([A-Za-z_][A-Za-z0-9_]*)$");
                if (annotation.Success)
                {
                    var node = GetNode(graph, annotation.Groups[2].Value);
                    node.Annotation = annotation.Groups[1].Value.Trim();
                    return true;
                }

                var member = Regex.Match(statement, @"^([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(.+)$", RegexOptions.Singleline);
                if (member.Success)
                {
                    var node = GetNode(graph, member.Groups[1].Value);
                    node.Rows.Add(new MermaidTableRow() { Text = member.Groups[2].Value.Trim() });
                    return true;
                }

                return false;
            }

            private bool ParseClass(MermaidGraph graph, string rest)
            {
                var open = rest.EndsWith('{');
                if (open)
                    rest = rest[..^1].Trim();

                if (!Regex.IsMatch(rest, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                    return false;

                var node = GetNode(graph, rest);
                if (open)
                    _current = node;

                return true;
            }

            private bool ParseRelation(MermaidGraph graph, string statement)
            {
                var match = Regex.Match(
                    statement,
                    @"^(?<a>[A-Za-z_][A-Za-z0-9_]*)\s*(?:""(?<al>[^""]*)""\s*)?(?<op><\|--|--\|>|<\|\.\.|\.\.\|>|\*--|--\*|o--|--o|<--|-->|<\.\.|\.\.>|--|\.\.)\s*(?:""(?<bl>[^""]*)""\s*)?(?<b>[A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*(?<label>.*))?$",
                    RegexOptions.Singleline);
                if (!match.Success)
                    return false;

                var op = match.Groups["op"].Value;
                var reverse = op[0] is '<' or '*' or 'o';
                var arrow = op switch
                {
                    "<|--" or "--|>" or "<|.." or "..|>" => MermaidEndKind.HollowTriangle,
                    "*--" or "--*" => MermaidEndKind.FilledDiamond,
                    "o--" or "--o" => MermaidEndKind.HollowDiamond,
                    "<--" or "-->" or "<.." or "..>" => MermaidEndKind.Arrow,
                    _ => MermaidEndKind.None,
                };

                var line = op.Contains('.') ? MermaidLineStyle.Dotted : MermaidLineStyle.Solid;
                var left = match.Groups["a"].Value;
                var right = match.Groups["b"].Value;
                var leftCard = match.Groups["al"].Success ? match.Groups["al"].Value.Trim() : null;
                var rightCard = match.Groups["bl"].Success ? match.Groups["bl"].Value.Trim() : null;

                var from = GetNode(graph, reverse ? right : left);
                var to = GetNode(graph, reverse ? left : right);
                var label = match.Groups["label"].Success ? match.Groups["label"].Value.Trim() : null;
                if (label != null && label.Length > 1 && label[0] == '"' && label[^1] == '"')
                    label = label[1..^1];

                graph.Edges.Add(new MermaidEdge()
                {
                    From = from,
                    To = to,
                    Label = label,
                    Line = line,
                    Head = arrow,
                    TailLabel = reverse ? rightCard : leftCard,
                    HeadLabel = reverse ? leftCard : rightCard,
                });

                return true;
            }

            private static MermaidNode GetNode(MermaidGraph graph, string id)
            {
                var node = graph.GetOrCreateNode(id, null);
                node.Shape = MermaidNodeShape.ClassBox;
                node.Label = id;
                return node;
            }

            private static void ClassifyRows(MermaidNode node)
            {
                var firstMethod = -1;
                var hasAttribute = false;
                for (var i = 0; i < node.Rows.Count; i++)
                {
                    var isMethod = node.Rows[i].Text.Contains('(');
                    if (isMethod && firstMethod < 0)
                        firstMethod = i;
                    else if (!isMethod)
                        hasAttribute = true;
                }

                if (firstMethod > 0 && hasAttribute)
                    node.Rows[firstMethod].Separator = true;
            }

            private readonly string _code;
            private MermaidNode _current;
        }
    }
}
