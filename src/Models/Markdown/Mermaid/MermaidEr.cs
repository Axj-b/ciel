using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SourceGit.Models
{
    /// <summary>
    ///     Parser for the mermaid erDiagram syntax: entities with attribute rows and crow's-foot
    ///     relations. Anything else returns null so the caller falls back to a plain code block.
    /// </summary>
    public static class MermaidEr
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

                if (!statements[0].Equals("erDiagram", StringComparison.OrdinalIgnoreCase))
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
                {
                    if (node.Rows.Count > 0)
                        node.Shape = MermaidNodeShape.ClassBox;
                }

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
                    return ParseAttribute(_current, statement);

                if (ParseRelation(graph, statement))
                    return true;

                var open = Regex.Match(statement, @"^([A-Za-z_][A-Za-z0-9_-]*)\s*(?:\[(?:""([^""]*)""|'([^']*)')\])?\s*\{$");
                if (open.Success)
                {
                    var node = GetNode(graph, open.Groups[1].Value);
                    if (open.Groups[2].Success && open.Groups[2].Value.Length > 0)
                        node.Label = open.Groups[2].Value;
                    else if (open.Groups[3].Success && open.Groups[3].Value.Length > 0)
                        node.Label = open.Groups[3].Value;

                    _current = node;
                    return true;
                }

                var keyword = MermaidSource.Keyword(statement);
                switch (keyword)
                {
                    case "acctitle":
                    case "accdescr":
                    case "style":
                    case "classdef":
                    case "click":
                    case "direction":
                        return false;
                }

                return false;
            }

            private bool ParseAttribute(MermaidNode node, string statement)
            {
                var match = Regex.Match(
                    statement,
                    @"^(?<type>[^\s""]+)\s+(?<name>[^\s""]+)(?:\s+(?<keys>(?:PK|FK|UK)(?:\s*,\s*(?:PK|FK|UK))*))?(?:\s+""(?<comment>[^""]*)"")?$",
                    RegexOptions.IgnoreCase);
                if (!match.Success || node.Rows.Count >= 60)
                    return false;

                var value = match.Groups["keys"].Success ? match.Groups["keys"].Value.Replace(" ", string.Empty).ToUpperInvariant() : null;
                if (match.Groups["comment"].Success && match.Groups["comment"].Value.Length > 0)
                    value = value == null ? match.Groups["comment"].Value : $"{value} · {match.Groups["comment"].Value}";

                node.Rows.Add(new MermaidTableRow()
                {
                    Text = $"{match.Groups["type"].Value} {match.Groups["name"].Value}",
                    Value = value,
                });

                return true;
            }

            private bool ParseRelation(MermaidGraph graph, string statement)
            {
                var match = Regex.Match(
                    statement,
                    @"^(?<a>[A-Za-z_][A-Za-z0-9_-]*)\s*(?<l>\|\||\|o|o\||\}\||\|{|\}o|o\{)\s*(?<line>--|\.\.)\s*(?<r>\|\||\|o|o\||\}\||\|{|\}o|o\{)\s*(?<b>[A-Za-z_][A-Za-z0-9_-]*)\s*(?::\s*(?<label>.*))?$",
                    RegexOptions.Singleline);
                if (!match.Success)
                    return false;

                var from = GetNode(graph, match.Groups["a"].Value);
                var to = GetNode(graph, match.Groups["b"].Value);
                graph.Edges.Add(new MermaidEdge()
                {
                    From = from,
                    To = to,
                    Label = Unquote(match.Groups["label"].Success ? match.Groups["label"].Value.Trim() : null),
                    Line = match.Groups["line"].Value == ".." ? MermaidLineStyle.Dotted : MermaidLineStyle.Solid,
                    Tail = Card(match.Groups["l"].Value),
                    Head = Card(match.Groups["r"].Value),
                });

                return true;
            }

            private static string Unquote(string label)
            {
                return label != null && label.Length > 1 && label[0] == '"' && label[^1] == '"' ? label[1..^1] : label;
            }

            private static MermaidEndKind Card(string token)
            {
                return token switch
                {
                    "||" => MermaidEndKind.One,
                    "|o" or "o|" => MermaidEndKind.ZeroOne,
                    "}|" or "|{" => MermaidEndKind.Many,
                    _ => MermaidEndKind.ZeroMany,
                };
            }

            private static MermaidNode GetNode(MermaidGraph graph, string id)
            {
                if (graph.NodeMap.TryGetValue(id, out var existing))
                    return existing;

                var node = graph.GetOrCreateNode(id, null);
                node.Label = id;
                return node;
            }

            private readonly string _code;
            private MermaidNode _current;
        }
    }
}
