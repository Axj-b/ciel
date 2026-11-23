using System;
using System.Collections.Generic;

using Avalonia;

namespace SourceGit.Models
{
    public class MermaidLayoutNode
    {
        public MermaidNode Node { get; set; }
        public Rect Bounds { get; set; }
        public Point Center { get; set; }
    }

    public class MermaidLayoutEdge
    {
        public MermaidEdge Edge { get; set; }
        public List<Point> Points { get; } = new();
        public bool IsSelfLoop { get; set; }
        public Rect SelfLoopBounds { get; set; }
        public Point LabelCenter { get; set; }
    }

    public class MermaidLayoutSubgraph
    {
        public MermaidSubgraph Subgraph { get; set; }
        public Rect Bounds { get; set; }
    }

    public class MermaidLayoutResult
    {
        public Size Size { get; set; }
        public List<MermaidLayoutNode> Nodes { get; } = new();
        public List<MermaidLayoutEdge> Edges { get; } = new();
        public List<MermaidLayoutSubgraph> Subgraphs { get; } = new();
    }

    /// <summary>
    ///     Layered (Sugiyama style) graph layout: cycle removal, longest-path ranking, dummy nodes
    ///     for long edges, barycenter crossing reduction that keeps subgraph members contiguous,
    ///     and iterative coordinate relaxation. Layout happens in TD space and is transformed to
    ///     the requested direction at the end.
    /// </summary>
    public static class MermaidGraphLayout
    {
        public static MermaidLayoutResult Layout(
            MermaidGraph graph,
            IReadOnlyDictionary<MermaidNode, Size> nodeSizes,
            IReadOnlyDictionary<MermaidEdge, Size> labelSizes,
            IReadOnlyDictionary<MermaidSubgraph, Size> titleSizes)
        {
            var nodes = new List<LNode>();
            var map = new Dictionary<MermaidNode, LNode>();
            var groupIds = new Dictionary<MermaidSubgraph, int>();
            for (var i = 0; i < graph.Subgraphs.Count; i++)
                groupIds[graph.Subgraphs[i]] = i;

            // LR/RL lay ranks out along X; the abstract layout below always treats the rank axis
            // as Y, so cross/rank extents swap for horizontal directions.
            var horizontal = graph.Direction is "LR" or "RL";
            foreach (var node in graph.Nodes)
            {
                var depth = Depth(node.Parent);
                var drawSize = nodeSizes[node];
                var slotW = drawSize.Width + 2 * SubPadX * depth;
                var slotH = drawSize.Height + (SubPadTop + SubPadBottom) * depth;
                var lnode = new LNode()
                {
                    Node = node,
                    DrawSize = drawSize,
                    CrossSize = horizontal ? slotH : slotW,
                    RankSize = horizontal ? slotW : slotH,
                    HalfX = (horizontal ? drawSize.Height : drawSize.Width) / 2,
                    HalfY = (horizontal ? drawSize.Width : drawSize.Height) / 2,
                    GroupPath = Path(node.Parent, groupIds),
                };
                nodes.Add(lnode);
                map[node] = lnode;
            }

            var edges = new List<LEdge>();
            var selfLoops = new List<LEdge>();
            foreach (var edge in graph.Edges)
            {
                var ledge = new LEdge() { Edge = edge, From = map[edge.From], To = map[edge.To], Span = Math.Max(1, edge.Span) };
                if (edge.From == edge.To)
                    selfLoops.Add(ledge);
                else
                    edges.Add(ledge);
            }

            RemoveCycles(nodes, edges);
            AssignRanks(nodes, edges);
            var layers = BuildLayers(nodes, edges, groupIds);
            OrderLayers(layers);
            AssignCoordinates(layers);

            var result = new MermaidLayoutResult();
            var placed = new Dictionary<LNode, MermaidLayoutNode>();
            foreach (var layer in layers)
            {
                foreach (var lnode in layer)
                {
                    if (lnode.Node == null)
                        continue;

                    var center = horizontal ? new Point(lnode.RankCenter, lnode.Cross) : new Point(lnode.Cross, lnode.RankCenter);
                    var bounds = new Rect(
                        center.X - lnode.DrawSize.Width / 2,
                        center.Y - lnode.DrawSize.Height / 2,
                        lnode.DrawSize.Width,
                        lnode.DrawSize.Height);
                    var item = new MermaidLayoutNode() { Node = lnode.Node, Bounds = bounds, Center = center };
                    placed[lnode] = item;
                    result.Nodes.Add(item);
                }
            }

            foreach (var ledge in edges)
            {
                var route = new MermaidLayoutEdge() { Edge = ledge.Edge };
                var seq = new List<LNode>() { ledge.From };
                seq.AddRange(ledge.Chain);
                seq.Add(ledge.To);

                var first = seq[0];
                var second = seq[1];
                var last = seq[^1];
                var prev = seq[^2];
                route.Points.Add(Final(BoundaryPoint(first, Towards(first, second)), horizontal));
                for (var i = 1; i < seq.Count - 1; i++)
                    route.Points.Add(Final(new Point(seq[i].Cross, seq[i].RankCenter), horizontal));
                route.Points.Add(Final(BoundaryPoint(last, Towards(last, prev)), horizontal));

                if (ledge.Reversed)
                    route.Points.Reverse();

                if (ledge.Edge.Label != null && labelSizes.ContainsKey(ledge.Edge))
                {
                    route.LabelCenter = ledge.Chain.Count > 0
                        ? Final(new Point(ledge.Chain[ledge.Chain.Count / 2].Cross, ledge.Chain[ledge.Chain.Count / 2].RankCenter), horizontal)
                        : Midpoint(route.Points[0], route.Points[^1]);
                }

                result.Edges.Add(route);
            }

            foreach (var ledge in selfLoops)
            {
                var item = placed[ledge.From];
                var route = new MermaidLayoutEdge() { Edge = ledge.Edge, IsSelfLoop = true, SelfLoopBounds = item.Bounds };
                if (ledge.Edge.Label != null && labelSizes.ContainsKey(ledge.Edge))
                    route.LabelCenter = new Point(item.Bounds.Right + 30, item.Center.Y);
                result.Edges.Add(route);
            }

            // Subgraph regions, innermost first so parents include child boxes.
            var regions = new Dictionary<MermaidSubgraph, Rect>();
            for (var i = graph.Subgraphs.Count - 1; i >= 0; i--)
            {
                var subgraph = graph.Subgraphs[i];
                Rect? union = null;
                foreach (var node in subgraph.Nodes)
                {
                    if (placed.TryGetValue(map[node], out var item))
                        union = union.HasValue ? union.Value.Union(item.Bounds) : item.Bounds;
                }

                foreach (var child in subgraph.Children)
                {
                    if (regions.TryGetValue(child, out var childRect))
                        union = union.HasValue ? union.Value.Union(childRect) : childRect;
                }

                if (!union.HasValue)
                    continue;

                var rect = new Rect(
                    union.Value.X - SubPadX,
                    union.Value.Y - SubPadTop,
                    union.Value.Width + 2 * SubPadX,
                    union.Value.Height + SubPadTop + SubPadBottom);

                // Keep the title inside the box even when the members are narrow.
                if (titleSizes.TryGetValue(subgraph, out var titleSize))
                    rect = rect.WithWidth(Math.Max(rect.Width, titleSize.Width + 2 * SubPadX));

                regions[subgraph] = rect;
                result.Subgraphs.Add(new MermaidLayoutSubgraph() { Subgraph = subgraph, Bounds = rect });
            }

            result.Subgraphs.Reverse();

            // Total bounds, then normalize to a fixed margin.
            var total = new Rect(0, 0, 0, 0);
            var hasAny = false;
            void Extend(Rect rect)
            {
                total = hasAny ? total.Union(rect) : rect;
                hasAny = true;
            }

            foreach (var item in result.Nodes)
                Extend(item.Bounds);
            foreach (var region in result.Subgraphs)
                Extend(region.Bounds);
            foreach (var route in result.Edges)
            {
                foreach (var point in route.Points)
                    Extend(new Rect(point.X, point.Y, 0, 0));

                if (route.IsSelfLoop)
                    Extend(new Rect(route.SelfLoopBounds.Right, route.SelfLoopBounds.Y, 34, route.SelfLoopBounds.Height));

                if (route.Edge.Label != null && labelSizes.TryGetValue(route.Edge, out var labelSize))
                    Extend(new Rect(route.LabelCenter.X - labelSize.Width / 2, route.LabelCenter.Y - labelSize.Height / 2, labelSize.Width, labelSize.Height));
            }

            if (!hasAny)
                return null;

            var dx = Margin - total.X;
            var dy = Margin - total.Y;
            var width = total.Width + 2 * Margin;
            var height = total.Height + 2 * Margin;

            foreach (var item in result.Nodes)
            {
                item.Bounds = item.Bounds.Translate(new Vector(dx, dy));
                item.Center = item.Center + new Vector(dx, dy);
            }

            foreach (var region in result.Subgraphs)
                region.Bounds = region.Bounds.Translate(new Vector(dx, dy));

            foreach (var route in result.Edges)
            {
                for (var i = 0; i < route.Points.Count; i++)
                    route.Points[i] += new Vector(dx, dy);

                route.SelfLoopBounds = route.SelfLoopBounds.Translate(new Vector(dx, dy));
                route.LabelCenter += new Vector(dx, dy);
            }

            var size = new Size(width, height);
            Transform(graph.Direction, result, ref size);
            result.Size = size;
            return result;
        }

        private static void RemoveCycles(List<LNode> nodes, List<LEdge> edges)
        {
            var outgoing = new Dictionary<LNode, List<LEdge>>();
            foreach (var edge in edges)
            {
                if (!outgoing.TryGetValue(edge.From, out var list))
                    outgoing[edge.From] = list = new List<LEdge>();
                list.Add(edge);
            }

            var color = new Dictionary<LNode, int>();
            var stack = new Stack<(LNode Node, int Index)>();
            foreach (var root in nodes)
            {
                if (color.GetValueOrDefault(root) != 0)
                    continue;

                color[root] = 1;
                stack.Push((root, 0));
                while (stack.Count > 0)
                {
                    var (node, index) = stack.Pop();
                    var list = outgoing.GetValueOrDefault(node);
                    if (list == null || index >= list.Count)
                    {
                        color[node] = 2;
                        continue;
                    }

                    stack.Push((node, index + 1));
                    var target = list[index].To;
                    var state = color.GetValueOrDefault(target);
                    if (state == 1)
                        list[index].Reversed = true;
                    else if (state == 0)
                    {
                        color[target] = 1;
                        stack.Push((target, 0));
                    }
                }
            }

            foreach (var edge in edges)
            {
                if (!edge.Reversed)
                    continue;

                (edge.From, edge.To) = (edge.To, edge.From);
            }
        }

        private static void AssignRanks(List<LNode> nodes, List<LEdge> edges)
        {
            var indegree = new Dictionary<LNode, int>();
            var outgoing = new Dictionary<LNode, List<LEdge>>();
            foreach (var node in nodes)
                indegree[node] = 0;

            foreach (var edge in edges)
            {
                indegree[edge.To]++;
                if (!outgoing.TryGetValue(edge.From, out var list))
                    outgoing[edge.From] = list = new List<LEdge>();
                list.Add(edge);
            }

            var queue = new Queue<LNode>();
            foreach (var node in nodes)
            {
                if (indegree[node] == 0)
                {
                    node.Rank = 0;
                    queue.Enqueue(node);
                }
            }

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                foreach (var edge in outgoing.GetValueOrDefault(node) ?? new List<LEdge>())
                {
                    edge.To.Rank = Math.Max(edge.To.Rank, node.Rank + edge.Span);
                    if (--indegree[edge.To] == 0)
                        queue.Enqueue(edge.To);
                }
            }
        }

        private static List<List<LNode>> BuildLayers(List<LNode> nodes, List<LEdge> edges, Dictionary<MermaidSubgraph, int> groupIds)
        {
            var maxRank = 0;
            foreach (var node in nodes)
                maxRank = Math.Max(maxRank, node.Rank);

            var layers = new List<List<LNode>>();
            for (var i = 0; i <= maxRank; i++)
                layers.Add(new List<LNode>());

            foreach (var node in nodes)
                layers[node.Rank].Add(node);

            foreach (var edge in edges)
            {
                var from = edge.From;
                for (var rank = edge.From.Rank + 1; rank < edge.To.Rank; rank++)
                {
                    var dummy = new LNode()
                    {
                        Rank = rank,
                        CrossSize = 8,
                        RankSize = 8,
                        HalfX = 4,
                        HalfY = 4,
                        DrawSize = new Size(8, 8),
                        GroupPath = edge.From.Node?.Parent == edge.To.Node?.Parent
                            ? Path(edge.From.Node?.Parent, groupIds)
                            : Array.Empty<int>(),
                    };
                    edge.Chain.Add(dummy);
                    layers[rank].Add(dummy);
                    Link(from, dummy);
                    from = dummy;
                }

                Link(from, edge.To);
            }

            foreach (var layer in layers)
            {
                for (var i = 0; i < layer.Count; i++)
                    layer[i].Order = i;
            }

            return layers;
        }

        private static void Link(LNode upper, LNode lower)
        {
            upper.Lower.Add(lower);
            lower.Upper.Add(upper);
        }

        private static void OrderLayers(List<List<LNode>> layers)
        {
            for (var iter = 0; iter < 8; iter++)
            {
                var down = iter % 2 == 0;
                for (var i = down ? 1 : layers.Count - 2; down ? i < layers.Count : i >= 0; i += down ? 1 : -1)
                {
                    var layer = layers[i];
                    var bary = new Dictionary<LNode, double>();
                    foreach (var node in layer)
                    {
                        var neighbors = down ? node.Upper : node.Lower;
                        if (neighbors.Count == 0)
                        {
                            bary[node] = node.Order;
                            continue;
                        }

                        var sum = 0.0;
                        foreach (var neighbor in neighbors)
                            sum += neighbor.Order;
                        bary[node] = sum / neighbors.Count;
                    }

                    var output = new List<LNode>(layer.Count);
                    OrderGroup(layer, bary, 0, output);
                    for (var j = 0; j < output.Count; j++)
                        output[j].Order = j;

                    layers[i] = output;
                }
            }
        }

        private static void OrderGroup(List<LNode> members, Dictionary<LNode, double> bary, int level, List<LNode> output)
        {
            var groups = new Dictionary<int, List<LNode>>();
            var parts = new List<(double Mean, List<LNode> Members)>();
            foreach (var node in members)
            {
                if (node.GroupPath.Length > level)
                {
                    var key = node.GroupPath[level];
                    if (!groups.TryGetValue(key, out var list))
                        groups[key] = list = new List<LNode>();
                    list.Add(node);
                }
                else
                {
                    parts.Add((bary[node], new List<LNode>() { node }));
                }
            }

            foreach (var group in groups.Values)
            {
                var sum = 0.0;
                foreach (var node in group)
                    sum += bary[node];
                parts.Add((sum / group.Count, group));
            }

            parts.Sort((a, b) => a.Mean.CompareTo(b.Mean));
            foreach (var (_, part) in parts)
            {
                if (part.Count == 1)
                {
                    output.Add(part[0]);
                    continue;
                }

                var sameLeaf = true;
                foreach (var node in part)
                {
                    if (node.GroupPath.Length != level + 1)
                    {
                        sameLeaf = false;
                        break;
                    }
                }

                if (sameLeaf)
                {
                    part.Sort((a, b) => bary[a].CompareTo(bary[b]));
                    output.AddRange(part);
                }
                else
                {
                    OrderGroup(part, bary, level + 1, output);
                }
            }
        }

        private static void AssignCoordinates(List<List<LNode>> layers)
        {
            foreach (var layer in layers)
            {
                var x = 0.0;
                foreach (var node in layer)
                {
                    node.Cross = x + node.CrossSize / 2;
                    x += node.CrossSize + NodeSep;
                }

                var width = x - NodeSep;
                foreach (var node in layer)
                    node.Cross -= width / 2;
            }

            for (var iter = 0; iter < 16; iter++)
            {
                var down = iter % 2 == 0;
                for (var i = down ? 1 : layers.Count - 2; down ? i < layers.Count : i >= 0; i += down ? 1 : -1)
                {
                    var layer = layers[i];
                    if (layer.Count == 0)
                        continue;

                    var desired = new double[layer.Count];
                    for (var j = 0; j < layer.Count; j++)
                    {
                        var node = layer[j];
                        var neighbors = down ? node.Upper : node.Lower;
                        if (neighbors.Count == 0)
                        {
                            desired[j] = node.Cross;
                            continue;
                        }

                        var sum = 0.0;
                        foreach (var neighbor in neighbors)
                            sum += neighbor.Cross;
                        desired[j] = sum / neighbors.Count;
                    }

                    // Feasible placement closest to the desired positions: subtract cumulative
                    // minimum gaps, run pool-adjacent-violators, add the gaps back. A plain
                    // forward pass plus re-centering would let closed clusters drift apart.
                    var cum = new double[layer.Count];
                    for (var j = 1; j < layer.Count; j++)
                        cum[j] = cum[j - 1] + (layer[j - 1].CrossSize + layer[j].CrossSize) / 2 + NodeSep;

                    var blockValue = new List<double>();
                    var blockWeight = new List<double>();
                    var blockStart = new List<int>();
                    for (var j = 0; j < layer.Count; j++)
                    {
                        var value = desired[j] - cum[j];
                        var weight = 1.0;
                        var start = j;
                        while (blockValue.Count > 0 && blockValue[^1] > value)
                        {
                            var last = blockValue.Count - 1;
                            start = blockStart[last];
                            value = (value * weight + blockValue[last] * blockWeight[last]) / (weight + blockWeight[last]);
                            weight += blockWeight[last];
                            blockValue.RemoveAt(last);
                            blockWeight.RemoveAt(last);
                            blockStart.RemoveAt(last);
                        }

                        blockValue.Add(value);
                        blockWeight.Add(weight);
                        blockStart.Add(start);
                    }

                    var block = 0;
                    for (var j = 0; j < layer.Count; j++)
                    {
                        while (block + 1 < blockStart.Count && blockStart[block + 1] <= j)
                            block++;

                        layer[j].Cross = blockValue[block] + cum[j];
                    }
                }
            }

            var y = 0.0;
            foreach (var layer in layers)
            {
                var height = 0.0;
                foreach (var node in layer)
                    height = Math.Max(height, node.RankSize);

                foreach (var node in layer)
                    node.RankCenter = y + height / 2;

                y += height + RankSep;
            }
        }

        private static Point Final(Point abstractPoint, bool horizontal)
        {
            return horizontal ? new Point(abstractPoint.Y, abstractPoint.X) : abstractPoint;
        }

        private static Point Towards(LNode node, LNode other)
        {
            return new Point(other.Cross - node.Cross, other.RankCenter - node.RankCenter);
        }

        private static Point BoundaryPoint(LNode node, Point direction)
        {
            var center = new Point(node.Cross, node.RankCenter);
            var hw = node.HalfX;
            var hh = node.HalfY;
            var dx = direction.X;
            var dy = direction.Y;
            if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
                return center + new Point(0, hh);

            double t;
            var shape = node.Node?.Shape ?? MermaidNodeShape.Rectangle;
            if (shape == MermaidNodeShape.Diamond)
                t = 1.0 / (Math.Abs(dx) / hw + Math.Abs(dy) / hh);
            else if (shape is MermaidNodeShape.Circle)
                t = Math.Min(hw, hh) / Math.Sqrt(dx * dx + dy * dy);
            else
                t = Math.Min(hw / Math.Max(Math.Abs(dx), 0.001), hh / Math.Max(Math.Abs(dy), 0.001));

            return center + new Point(dx * t, dy * t);
        }

        private static Point Midpoint(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

        private static int Depth(MermaidSubgraph parent)
        {
            var depth = 0;
            while (parent != null)
            {
                depth++;
                parent = parent.Parent;
            }
            return depth;
        }

        private static int[] Path(MermaidSubgraph parent, Dictionary<MermaidSubgraph, int> groupIds)
        {
            if (parent == null)
                return Array.Empty<int>();

            var chain = new List<MermaidSubgraph>();
            var current = parent;
            while (current != null)
            {
                chain.Add(current);
                current = current.Parent;
            }

            chain.Reverse();
            var path = new int[chain.Count];
            for (var i = 0; i < chain.Count; i++)
                path[i] = groupIds[chain[i]];
            return path;
        }

        private static void Transform(string direction, MermaidLayoutResult result, ref Size size)
        {
            if (direction is not ("BT" or "RL"))
                return;

            var width = size.Width;
            var height = size.Height;

            // BT mirrors vertically, RL horizontally; positions flip, sizes stay.
            Point Map(Point p) => direction == "BT" ? new Point(p.X, height - p.Y) : new Point(width - p.X, p.Y);

            Rect MapRect(Rect r)
            {
                var topLeft = Map(new Point(r.X, r.Y));
                var bottomRight = Map(new Point(r.Right, r.Bottom));
                return new Rect(
                    Math.Min(topLeft.X, bottomRight.X),
                    Math.Min(topLeft.Y, bottomRight.Y),
                    Math.Abs(bottomRight.X - topLeft.X),
                    Math.Abs(bottomRight.Y - topLeft.Y));
            }

            foreach (var node in result.Nodes)
            {
                var center = Map(node.Center);
                node.Bounds = new Rect(
                    center.X - node.Bounds.Width / 2,
                    center.Y - node.Bounds.Height / 2,
                    node.Bounds.Width,
                    node.Bounds.Height);
                node.Center = center;
            }

            foreach (var region in result.Subgraphs)
                region.Bounds = MapRect(region.Bounds);

            foreach (var edge in result.Edges)
            {
                for (var i = 0; i < edge.Points.Count; i++)
                    edge.Points[i] = Map(edge.Points[i]);

                var loopCenter = Map(new Point(edge.SelfLoopBounds.Center.X, edge.SelfLoopBounds.Center.Y));
                edge.SelfLoopBounds = new Rect(
                    loopCenter.X - edge.SelfLoopBounds.Width / 2,
                    loopCenter.Y - edge.SelfLoopBounds.Height / 2,
                    edge.SelfLoopBounds.Width,
                    edge.SelfLoopBounds.Height);
                edge.LabelCenter = Map(edge.LabelCenter);
            }
        }

        private class LNode
        {
            public MermaidNode Node;
            public Size DrawSize;
            public double CrossSize;
            public double RankSize;
            public double HalfX;
            public double HalfY;
            public int Rank;
            public double Cross;
            public double RankCenter;
            public int Order;
            public int[] GroupPath = Array.Empty<int>();
            public List<LNode> Upper = new();
            public List<LNode> Lower = new();
        }

        private class LEdge
        {
            public MermaidEdge Edge;
            public LNode From;
            public LNode To;
            public int Span;
            public bool Reversed;
            public List<LNode> Chain = new();
        }

        private const double NodeSep = 36;
        private const double RankSep = 52;
        private const double SubPadX = 14;
        private const double SubPadTop = 30;
        private const double SubPadBottom = 14;
        private const double Margin = 12;
    }
}
