using System.Collections.Generic;

using Avalonia.Media;

namespace SourceGit.Models
{
    public enum MermaidNodeShape
    {
        Rectangle,
        Rounded,
        Stadium,
        Subroutine,
        Cylinder,
        Circle,
        Diamond,
        Hexagon,
        Parallelogram,
        ParallelogramAlt,
        Trapezoid,
        TrapezoidAlt,
        Asymmetric,
    }

    public enum MermaidLineStyle
    {
        Solid,
        Dotted,
        Thick,
    }

    public enum MermaidEndKind
    {
        None,
        Arrow,
        Cross,
        Circle,
    }

    /// <summary>Colors and stroke overrides coming from classDef/style/linkStyle statements.</summary>
    public class MermaidNodeStyle
    {
        public Color? Fill { get; set; }
        public Color? Stroke { get; set; }
        public double? StrokeWidth { get; set; }
        public Color? TextColor { get; set; }
        public double[] DashArray { get; set; }
    }

    public class MermaidNode
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public MermaidNodeShape Shape { get; set; } = MermaidNodeShape.Rectangle;
        public MermaidNodeStyle Style { get; set; }
        public MermaidNodeStyle ResolvedStyle { get; set; }
        public List<string> Classes { get; } = new();
        public MermaidSubgraph Parent { get; set; }
    }

    public class MermaidEdge
    {
        public MermaidNode From { get; set; }
        public MermaidNode To { get; set; }
        public string Label { get; set; }
        public MermaidLineStyle Line { get; set; } = MermaidLineStyle.Solid;
        public MermaidEndKind Head { get; set; } = MermaidEndKind.Arrow;
        public bool TailArrow { get; set; }
        public int Span { get; set; } = 1;
        public MermaidNodeStyle Style { get; set; }
    }

    public class MermaidSubgraph
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public MermaidSubgraph Parent { get; set; }
        public List<MermaidNode> Nodes { get; } = new();
        public List<MermaidSubgraph> Children { get; } = new();
    }

    public class MermaidGraph
    {
        public string Direction { get; set; } = "TD";
        public List<MermaidNode> Nodes { get; } = new();
        public Dictionary<string, MermaidNode> NodeMap { get; } = new(System.StringComparer.Ordinal);
        public List<MermaidEdge> Edges { get; } = new();
        public List<MermaidSubgraph> Subgraphs { get; } = new();
        public Dictionary<string, MermaidNodeStyle> ClassStyles { get; } = new(System.StringComparer.Ordinal);

        public MermaidNode GetOrCreateNode(string id, MermaidSubgraph parent)
        {
            if (NodeMap.TryGetValue(id, out var existing))
                return existing;

            var node = new MermaidNode() { Id = id, Label = id, Parent = parent };
            NodeMap[id] = node;
            Nodes.Add(node);
            parent?.Nodes.Add(node);
            return node;
        }
    }
}
