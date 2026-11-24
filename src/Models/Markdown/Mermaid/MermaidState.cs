using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SourceGit.Models
{
    /// <summary>
    ///     Parser for the mermaid stateDiagram-v2 syntax. States become rounded nodes, [*] becomes a
    ///     start/end marker, composite states become subgraphs. Anything else returns null so the
    ///     caller falls back to a plain code block.
    /// </summary>
    public static class MermaidState
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

                if (!Regex.IsMatch(statements[0], @"^stateDiagram(?:-v2)?$", RegexOptions.IgnoreCase))
                    return null;

                var graph = new MermaidGraph() { Direction = "TD" };
                var scope = new Stack<MermaidSubgraph>();
                var composites = new Dictionary<string, MermaidSubgraph>(StringComparer.Ordinal);
                for (var i = 1; i < statements.Count; i++)
                {
                    if (!ParseStatement(graph, statements[i], scope, composites))
                        return null;
                }

                if (scope.Count > 0 || graph.Nodes.Count == 0 || graph.Nodes.Count > 300 || graph.Edges.Count > 800)
                    return null;

                MergeBidirectional(graph);
                return graph;
            }

            /// <summary>
            ///     Two transitions between the same states would be routed on top of each other and
            ///     hide one label, so they are drawn as one double-headed edge with both labels.
            /// </summary>
            private static void MergeBidirectional(MermaidGraph graph)
            {
                for (var i = 0; i < graph.Edges.Count; i++)
                {
                    var edge = graph.Edges[i];
                    if (edge.TailArrow || edge.From == edge.To)
                        continue;

                    for (var j = i + 1; j < graph.Edges.Count; j++)
                    {
                        var other = graph.Edges[j];
                        if (other.From != edge.To || other.To != edge.From || other.TailArrow)
                            continue;

                        edge.Label = Join(edge.Label, other.Label);
                        edge.TailArrow = true;
                        graph.Edges.RemoveAt(j);
                        break;
                    }
                }
            }

            private static string Join(string first, string second)
            {
                if (string.IsNullOrEmpty(first))
                    return string.IsNullOrEmpty(second) ? null : second;
                if (string.IsNullOrEmpty(second))
                    return first;

                return $"{first} / {second}";
            }

            private bool ParseStatement(MermaidGraph graph, string statement, Stack<MermaidSubgraph> scope, Dictionary<string, MermaidSubgraph> composites)
            {
                var parent = scope.Count > 0 ? scope.Peek() : null;

                if (statement == "}")
                {
                    if (scope.Count == 0)
                        return false;

                    scope.Pop();
                    return true;
                }

                var keyword = MermaidSource.Keyword(statement);
                switch (keyword)
                {
                    case "state":
                        return ParseState(graph, statement[5..].Trim(), parent, scope, composites);
                    case "direction":
                    {
                        if (scope.Count > 0)
                            return false;

                        var direction = statement[9..].Trim().ToUpperInvariant();
                        if (direction is not ("TD" or "TB" or "LR" or "RL" or "BT"))
                            return false;

                        graph.Direction = direction == "TB" ? "TD" : direction;
                        return true;
                    }
                    case "note":
                    case "classdef":
                    case "class":
                    case "style":
                    case "click":
                    case "acctitle":
                    case "accdescr":
                        return false;
                }

                return ParseTransition(graph, statement, parent, composites);
            }

            private bool ParseState(
                MermaidGraph graph,
                string rest,
                MermaidSubgraph parent,
                Stack<MermaidSubgraph> scope,
                Dictionary<string, MermaidSubgraph> composites)
            {
                var composite = rest.EndsWith('{');
                if (composite)
                    rest = rest[..^1].Trim();

                if (rest.Length == 0 || rest.Contains("<<", StringComparison.Ordinal))
                    return false;

                string id;
                string label;
                var alias = Regex.Match(rest, @"^""([^""]*)""\s+as\s+([A-Za-z_][A-Za-z0-9_]*)$");
                if (alias.Success)
                {
                    label = alias.Groups[1].Value;
                    id = alias.Groups[2].Value;
                }
                else
                {
                    var description = Regex.Match(rest, @"^([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(.*)$", RegexOptions.Singleline);
                    if (description.Success)
                    {
                        if (composite)
                            return false;

                        id = description.Groups[1].Value;
                        label = description.Groups[2].Value.Trim();
                        if (label.Length == 0)
                            return false;

                        var described = graph.GetOrCreateNode(id, parent);
                        described.Label = $"{id}\n{label}";
                        described.Shape = MermaidNodeShape.Rounded;
                        return true;
                    }

                    if (!IsId(rest))
                        return false;

                    id = label = rest;
                }

                if (!IsId(id))
                    return false;

                if (composite)
                {
                    if (composites.ContainsKey(id))
                        return false;

                    var subgraph = new MermaidSubgraph() { Id = id, Title = label, Parent = parent };
                    graph.Subgraphs.Add(subgraph);
                    composites[id] = subgraph;
                    scope.Push(subgraph);
                }
                else
                {
                    var node = graph.GetOrCreateNode(id, parent);
                    node.Label = label;
                    node.Shape = MermaidNodeShape.Rounded;
                }

                return true;
            }

            private bool ParseTransition(MermaidGraph graph, string statement, MermaidSubgraph parent, Dictionary<string, MermaidSubgraph> composites)
            {
                var match = Regex.Match(statement, @"^(\S+?)\s*-->\s*(\S+?)\s*(?::\s*(.*))?$", RegexOptions.Singleline);
                if (!match.Success)
                    return false;

                var from = Resolve(graph, match.Groups[1].Value, parent, true, composites);
                var to = Resolve(graph, match.Groups[2].Value, parent, false, composites);
                if (from == null || to == null)
                    return false;

                graph.Edges.Add(new MermaidEdge()
                {
                    From = from,
                    To = to,
                    Label = match.Groups[3].Success ? match.Groups[3].Value.Trim() : null,
                });

                return true;
            }

            private MermaidNode Resolve(MermaidGraph graph, string id, MermaidSubgraph parent, bool start, Dictionary<string, MermaidSubgraph> composites)
            {
                if (id == "[*]")
                {
                    var marker = new MermaidNode()
                    {
                        Id = start ? $"$start{_markers++}" : $"$end{_markers++}",
                        Label = string.Empty,
                        Shape = start ? MermaidNodeShape.StartMarker : MermaidNodeShape.EndMarker,
                        Parent = parent,
                    };

                    graph.Nodes.Add(marker);
                    graph.NodeMap[marker.Id] = marker;
                    parent?.Nodes.Add(marker);
                    return marker;
                }

                if (!IsId(id) || composites.ContainsKey(id))
                    return null;

                var node = graph.GetOrCreateNode(id, parent);
                node.Shape = MermaidNodeShape.Rounded;
                return node;
            }

            private static bool IsId(string id)
            {
                if (id.Length == 0 || id.Length > 80)
                    return false;

                if (!char.IsLetter(id[0]) && id[0] != '_')
                    return false;

                foreach (var c in id)
                {
                    if (!char.IsLetterOrDigit(c) && c != '_')
                        return false;
                }

                return true;
            }

            private readonly string _code;
            private int _markers;
        }
    }
}
