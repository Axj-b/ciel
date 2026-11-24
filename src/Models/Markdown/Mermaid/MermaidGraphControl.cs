using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SourceGit.Models
{
    /// <summary>Theme brushes used to draw a mermaid diagram, resolved when the view is built.</summary>
    public class MermaidTheme
    {
        public IBrush NodeFill { get; set; }
        public IBrush NodeStroke { get; set; }
        public IBrush NodeText { get; set; }
        public IBrush Edge { get; set; }
        public IBrush EdgeLabelBg { get; set; }
        public IBrush EdgeLabelText { get; set; }
        public IBrush SubgraphFill { get; set; }
        public IBrush SubgraphStroke { get; set; }
        public IBrush SubgraphTitle { get; set; }
    }

    /// <summary>
    ///     A prepared text label. Center-aligned FormattedText lays its lines out inside the
    ///     MaxTextWidth box, so drawing must origin at center - BoxWidth/2, not center - ink/2.
    /// </summary>
    public class MermaidLabel
    {
        public FormattedText Text { get; set; }
        public double BoxWidth { get; set; }
    }

    /// <summary>Everything prepared for drawing a graph: layout, labels and compartment content.</summary>
    public class MermaidGraphView
    {
        public MermaidLayoutResult Layout { get; set; }
        public Dictionary<MermaidNode, MermaidLabel> Labels { get; } = new();
        public Dictionary<MermaidNode, MermaidTableDraw> Tables { get; } = new();
        public Dictionary<MermaidEdge, MermaidLabel> EdgeLabels { get; } = new();
        public Dictionary<MermaidEdge, MermaidLabel> TailLabels { get; } = new();
        public Dictionary<MermaidEdge, MermaidLabel> HeadLabels { get; } = new();
        public Dictionary<MermaidSubgraph, MermaidLabel> Titles { get; } = new();
    }

    /// <summary>
    ///     Draws a laid-out mermaid graph with DrawingContext primitives. All brushes and text are
    ///     prepared up front; the view is rebuilt on theme changes like every other markdown block.
    /// </summary>
    public class MermaidGraphControl : Control
    {
        public MermaidGraphControl(MermaidGraphView view, MermaidTheme theme)
        {
            _layout = view.Layout;
            _labels = view.Labels;
            _tables = view.Tables;
            _edgeLabels = view.EdgeLabels;
            _tailLabels = view.TailLabels;
            _headLabels = view.HeadLabels;
            _titles = view.Titles;
            _theme = theme;

            foreach (var item in _layout.Nodes)
            {
                var style = item.Node.ResolvedStyle;
                var fill = style?.Fill is { } fillColor ? new SolidColorBrush(fillColor) : theme.NodeFill;
                var stroke = style?.Stroke is { } strokeColor ? new SolidColorBrush(strokeColor) : theme.NodeStroke;
                var width = style?.StrokeWidth ?? 1.2;
                var pen = new Pen(stroke, width, style?.DashArray != null ? new DashStyle(style.DashArray, 0) : null);
                _nodePaints[item.Node] = (fill, pen);
            }

            foreach (var route in _layout.Edges)
            {
                var edge = route.Edge;
                var style = edge.Style;
                var stroke = style?.Stroke is { } color ? new SolidColorBrush(color) : theme.Edge;
                var width = style?.StrokeWidth ?? (edge.Line == MermaidLineStyle.Thick ? 2.6 : 1.4);
                DashStyle dash = null;
                if (style?.DashArray != null)
                    dash = new DashStyle(style.DashArray, 0);
                else if (edge.Line == MermaidLineStyle.Dotted)
                    dash = new DashStyle(new double[] { 3, 3 }, 0);

                _edgePens[edge] = new Pen(stroke, width, dash);
            }
        }

        protected override Size MeasureOverride(Size availableSize) => _layout.Size;

        public override void Render(DrawingContext context)
        {
            foreach (var region in _layout.Subgraphs)
            {
                context.DrawRectangle(_theme.SubgraphFill, new Pen(_theme.SubgraphStroke, 1), region.Bounds, 4, 4);
                if (_titles.TryGetValue(region.Subgraph, out var title))
                    context.DrawText(title.Text, new Point(region.Bounds.X + 10, region.Bounds.Y + 7));
            }

            foreach (var route in _layout.Edges)
                DrawEdge(context, route);

            foreach (var item in _layout.Nodes)
                DrawNode(context, item);

            // Labels paint last so a dense layout cannot clip them behind a node box.
            foreach (var route in _layout.Edges)
                DrawEdgeLabels(context, route);
        }

        private void DrawEdge(DrawingContext context, MermaidLayoutEdge route)
        {
            var edge = route.Edge;
            var pen = _edgePens[edge];

            if (route.IsSelfLoop)
            {
                var rect = route.SelfLoopBounds;
                var cy = rect.Y + rect.Height / 2;
                var start = new Point(rect.Right, cy - 7);
                var end = new Point(rect.Right, cy + 7);
                var c1 = new Point(rect.Right + 28, cy - 18);
                var c2 = new Point(rect.Right + 28, cy + 18);

                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(start, false);
                    ctx.CubicBezierTo(c1, c2, end);
                }

                context.DrawGeometry(null, pen, geometry);
                if (edge.Head != MermaidEndKind.None)
                    DrawHead(context, end, end - c2, edge.Head, pen.Brush, _theme.NodeFill);
            }
            else if (route.Points.Count >= 2)
            {
                var points = new List<Point>(route.Points);
                var headDir = points[^1] - points[^2];
                var tailDir = points[0] - points[1];

                var headTrim = Trim(edge.Head);
                if (headTrim > 0)
                    points[^1] = Shorten(points[^1], headDir, headTrim);

                if (edge.Tail != MermaidEndKind.None)
                    points[0] = Shorten(points[0], tailDir, Trim(edge.Tail));
                else if (edge.TailArrow)
                    points[0] = Shorten(points[0], tailDir, 8);

                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(points[0], false);
                    for (var i = 1; i < points.Count; i++)
                        ctx.LineTo(points[i]);
                }

                context.DrawGeometry(null, pen, geometry);

                if (edge.Head != MermaidEndKind.None)
                    DrawHead(context, route.Points[^1], headDir, edge.Head, pen.Brush, _theme.NodeFill);
                if (edge.Tail != MermaidEndKind.None)
                    DrawHead(context, route.Points[0], tailDir, edge.Tail, pen.Brush, _theme.NodeFill);
                else if (edge.TailArrow)
                    DrawHead(context, route.Points[0], tailDir, MermaidEndKind.Arrow, pen.Brush, _theme.NodeFill);
            }
        }

        private void DrawEdgeLabels(DrawingContext context, MermaidLayoutEdge route)
        {
            var edge = route.Edge;

            if (edge.Label != null && _edgeLabels.TryGetValue(edge, out var label))
            {
                var center = route.LabelCenter;
                var bg = new Rect(
                    center.X - label.Text.Width / 2 - 5,
                    center.Y - label.Text.Height / 2 - 2,
                    label.Text.Width + 10,
                    label.Text.Height + 4);
                context.DrawRectangle(_theme.EdgeLabelBg, null, bg, 3, 3);
                context.DrawText(label.Text, new Point(center.X - label.BoxWidth / 2, center.Y - label.Text.Height / 2));
            }

            if (edge.TailLabel != null && _tailLabels.TryGetValue(edge, out var tail))
                DrawEndLabel(context, route, tail, 0.16);
            if (edge.HeadLabel != null && _headLabels.TryGetValue(edge, out var head))
                DrawEndLabel(context, route, head, 0.84);
        }

        private static double Trim(MermaidEndKind kind)
        {
            return kind switch
            {
                MermaidEndKind.None or MermaidEndKind.Arrow => kind == MermaidEndKind.Arrow ? 8 : 0,
                MermaidEndKind.Cross or MermaidEndKind.Circle or MermaidEndKind.Open => 7,
                _ => 0,
            };
        }

        private void DrawEndLabel(DrawingContext context, MermaidLayoutEdge route, MermaidLabel label, double fraction)
        {
            if (route.Points.Count < 2)
                return;

            Locate(route.Points, fraction, out var point, out var direction);
            var length = direction.Length;
            var normal = length < 0.001 ? new Vector(0, 1) : new Vector(-direction.Y / length, direction.X / length);
            if (normal.Y < 0)
                normal = -normal;

            // Opposite side of the line from the relation label, which straddles the route.
            var origin = point + normal * 14;
            var bg = new Rect(
                origin.X - label.Text.Width / 2 - 3,
                origin.Y - label.Text.Height / 2 - 1,
                label.Text.Width + 6,
                label.Text.Height + 2);
            context.DrawRectangle(_theme.EdgeLabelBg, null, bg, 3, 3);
            context.DrawText(label.Text, new Point(origin.X - label.Text.Width / 2, origin.Y - label.Text.Height / 2));
        }

        private static void Locate(IReadOnlyList<Point> points, double fraction, out Point point, out Vector direction)
        {
            var total = 0.0;
            for (var i = 1; i < points.Count; i++)
            {
                var dx = points[i].X - points[i - 1].X;
                var dy = points[i].Y - points[i - 1].Y;
                total += Math.Sqrt(dx * dx + dy * dy);
            }

            if (total < 0.001)
            {
                point = points[0];
                direction = new Vector(1, 0);
                return;
            }

            var target = total * fraction;
            for (var i = 1; i < points.Count; i++)
            {
                var segment = new Vector(points[i].X - points[i - 1].X, points[i].Y - points[i - 1].Y);
                var length = segment.Length;
                if (target <= length || i == points.Count - 1)
                {
                    point = points[i - 1] + segment * (length < 0.001 ? 0 : target / length);
                    direction = segment;
                    return;
                }

                target -= length;
            }

            point = points[^1];
            direction = points[^1] - points[^2];
        }

        private static Point Shorten(Point tip, Vector direction, double amount)
        {
            var length = direction.Length;
            if (length < 0.001)
                return tip;

            return tip - direction * (amount / length);
        }

        private static void DrawHead(DrawingContext context, Point tip, Vector direction, MermaidEndKind kind, IBrush brush, IBrush fill)
        {
            var length = direction.Length;
            if (length < 0.001)
                return;

            var dir = direction / length;
            var normal = new Vector(-dir.Y, dir.X);

            switch (kind)
            {
                case MermaidEndKind.Arrow:
                {
                    var basePoint = tip - dir * 9;
                    var geometry = new StreamGeometry();
                    using (var ctx = geometry.Open())
                    {
                        ctx.BeginFigure(tip, true);
                        ctx.LineTo(basePoint + normal * 4.5);
                        ctx.LineTo(basePoint - normal * 4.5);
                        ctx.EndFigure(true);
                    }

                    context.DrawGeometry(brush, null, geometry);
                    break;
                }
                case MermaidEndKind.Cross:
                {
                    var center = tip - dir * 4;
                    var a = (dir + normal) * 4;
                    var b = (dir - normal) * 4;
                    var pen = new Pen(brush, 1.6);
                    context.DrawLine(pen, center - a, center + a);
                    context.DrawLine(pen, center - b, center + b);
                    break;
                }
                case MermaidEndKind.Circle:
                {
                    context.DrawEllipse(brush, null, tip - dir * 4, 4, 4);
                    break;
                }
                case MermaidEndKind.HollowTriangle:
                {
                    var back = tip - dir * 12;
                    DrawPolygon(context, fill, new Pen(brush, 1.4), tip, back + normal * 6, back - normal * 6);
                    break;
                }
                case MermaidEndKind.FilledDiamond:
                case MermaidEndKind.HollowDiamond:
                {
                    var middle = tip - dir * 7;
                    var back = tip - dir * 14;
                    DrawPolygon(context, kind == MermaidEndKind.FilledDiamond ? brush : fill, new Pen(brush, 1.3),
                        tip, middle + normal * 5, back, middle - normal * 5);
                    break;
                }
                case MermaidEndKind.One:
                {
                    var center = tip - dir * 2.5;
                    var pen = new Pen(brush, 1.4);
                    context.DrawLine(pen, center + normal * 5, center - normal * 5);
                    break;
                }
                case MermaidEndKind.Many:
                {
                    var origin = tip - dir * 9;
                    var pen = new Pen(brush, 1.4);
                    context.DrawLine(pen, origin, tip + normal * 5);
                    context.DrawLine(pen, origin, tip);
                    context.DrawLine(pen, origin, tip - normal * 5);
                    break;
                }
                case MermaidEndKind.ZeroOne:
                {
                    var center = tip - dir * 2.5;
                    var pen = new Pen(brush, 1.4);
                    context.DrawLine(pen, center + normal * 5, center - normal * 5);
                    context.DrawEllipse(fill, new Pen(brush, 1.3), tip - dir * 9, 3.5, 3.5);
                    break;
                }
                case MermaidEndKind.ZeroMany:
                {
                    var origin = tip - dir * 9;
                    var pen = new Pen(brush, 1.4);
                    context.DrawLine(pen, origin, tip + normal * 5);
                    context.DrawLine(pen, origin, tip);
                    context.DrawLine(pen, origin, tip - normal * 5);
                    context.DrawEllipse(fill, new Pen(brush, 1.3), tip - dir * 13, 3.5, 3.5);
                    break;
                }
            }
        }

        private void DrawNode(DrawingContext context, MermaidLayoutNode item)
        {
            var rect = item.Bounds;
            var (fill, pen) = _nodePaints[item.Node];
            var x = rect.X;
            var y = rect.Y;
            var w = rect.Width;
            var h = rect.Height;
            var cx = rect.Center.X;
            var cy = rect.Center.Y;

            switch (item.Node.Shape)
            {
                case MermaidNodeShape.ClassBox:
                {
                    context.DrawRectangle(fill, pen, rect, 3, 3);
                    if (_tables.TryGetValue(item.Node, out var table) && _labels.TryGetValue(item.Node, out var tableTitle))
                        MermaidTable.Draw(context, rect, tableTitle, table, _theme);

                    return;
                }
                case MermaidNodeShape.StartMarker:
                {
                    context.DrawEllipse(_theme.NodeText, null, rect.Center, w / 2, h / 2);
                    return;
                }
                case MermaidNodeShape.EndMarker:
                {
                    context.DrawEllipse(fill, new Pen(_theme.NodeText, 1.5), rect.Center, w / 2, h / 2);
                    context.DrawEllipse(_theme.NodeText, null, rect.Center, w / 4.6, h / 4.6);
                    return;
                }
                case MermaidNodeShape.Rectangle:
                    context.DrawRectangle(fill, pen, rect);
                    break;
                case MermaidNodeShape.Rounded:
                    context.DrawRectangle(fill, pen, rect, 5, 5);
                    break;
                case MermaidNodeShape.Stadium:
                    context.DrawRectangle(fill, pen, rect, h / 2, h / 2);
                    break;
                case MermaidNodeShape.Circle:
                    context.DrawEllipse(fill, pen, rect.Center, w / 2, h / 2);
                    break;
                case MermaidNodeShape.Subroutine:
                    context.DrawRectangle(fill, pen, rect);
                    context.DrawLine(pen, new Point(x + 8, y), new Point(x + 8, y + h));
                    context.DrawLine(pen, new Point(x + w - 8, y), new Point(x + w - 8, y + h));
                    break;
                case MermaidNodeShape.Diamond:
                    DrawPolygon(context, fill, pen, new Point(cx, y), new Point(x + w, cy), new Point(cx, y + h), new Point(x, cy));
                    break;
                case MermaidNodeShape.Hexagon:
                {
                    var s = Math.Min(18, w * 0.25);
                    DrawPolygon(context, fill, pen,
                        new Point(x + s, y), new Point(x + w - s, y), new Point(x + w, cy),
                        new Point(x + w - s, y + h), new Point(x + s, y + h), new Point(x, cy));
                    break;
                }
                case MermaidNodeShape.Parallelogram:
                {
                    var s = Math.Min(14, w * 0.2);
                    DrawPolygon(context, fill, pen,
                        new Point(x + s, y), new Point(x + w, y), new Point(x + w - s, y + h), new Point(x, y + h));
                    break;
                }
                case MermaidNodeShape.ParallelogramAlt:
                {
                    var s = Math.Min(14, w * 0.2);
                    DrawPolygon(context, fill, pen,
                        new Point(x, y), new Point(x + w - s, y), new Point(x + w, y + h), new Point(x + s, y + h));
                    break;
                }
                case MermaidNodeShape.Trapezoid:
                {
                    var s = Math.Min(14, w * 0.2);
                    DrawPolygon(context, fill, pen,
                        new Point(x + s, y), new Point(x + w - s, y), new Point(x + w, y + h), new Point(x, y + h));
                    break;
                }
                case MermaidNodeShape.TrapezoidAlt:
                {
                    var s = Math.Min(14, w * 0.2);
                    DrawPolygon(context, fill, pen,
                        new Point(x, y), new Point(x + w, y), new Point(x + w - s, y + h), new Point(x + s, y + h));
                    break;
                }
                case MermaidNodeShape.Asymmetric:
                {
                    var s = Math.Min(10, w * 0.2);
                    DrawPolygon(context, fill, pen,
                        new Point(x + s, y), new Point(x + w, y), new Point(x + w, y + h),
                        new Point(x + s, y + h), new Point(x, cy));
                    break;
                }
                case MermaidNodeShape.Cylinder:
                {
                    var ry = Math.Min(7, h * 0.2);
                    var geometry = new StreamGeometry();
                    using (var ctx = geometry.Open())
                    {
                        ctx.BeginFigure(new Point(x, y + ry), true);
                        ctx.ArcTo(new Point(x + w, y + ry), new Size(w / 2, ry), 0, false, SweepDirection.Clockwise);
                        ctx.LineTo(new Point(x + w, y + h - ry));
                        ctx.ArcTo(new Point(x, y + h - ry), new Size(w / 2, ry), 0, false, SweepDirection.Clockwise);
                        ctx.EndFigure(true);
                    }

                    context.DrawGeometry(fill, pen, geometry);

                    var inner = new StreamGeometry();
                    using (var ctx = inner.Open())
                    {
                        ctx.BeginFigure(new Point(x, y + ry), false);
                        ctx.ArcTo(new Point(x + w, y + ry), new Size(w / 2, ry), 0, false, SweepDirection.CounterClockwise);
                    }

                    context.DrawGeometry(null, pen, inner);
                    break;
                }
            }

            if (_labels.TryGetValue(item.Node, out var label))
            {
                var offset = item.Node.Shape == MermaidNodeShape.Cylinder ? 3 : 0;
                context.DrawText(label.Text, new Point(cx - label.BoxWidth / 2, cy - label.Text.Height / 2 + offset));
            }
        }

        private static void DrawPolygon(DrawingContext context, IBrush fill, IPen pen, params Point[] points)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(points[0], true);
                for (var i = 1; i < points.Length; i++)
                    ctx.LineTo(points[i]);
                ctx.EndFigure(true);
            }

            context.DrawGeometry(fill, pen, geometry);
        }

        private readonly MermaidLayoutResult _layout;
        private readonly IReadOnlyDictionary<MermaidNode, MermaidLabel> _labels;
        private readonly IReadOnlyDictionary<MermaidNode, MermaidTableDraw> _tables;
        private readonly IReadOnlyDictionary<MermaidEdge, MermaidLabel> _edgeLabels;
        private readonly IReadOnlyDictionary<MermaidEdge, MermaidLabel> _tailLabels;
        private readonly IReadOnlyDictionary<MermaidEdge, MermaidLabel> _headLabels;
        private readonly IReadOnlyDictionary<MermaidSubgraph, MermaidLabel> _titles;
        private readonly MermaidTheme _theme;
        private readonly Dictionary<MermaidNode, (IBrush Fill, IPen Pen)> _nodePaints = new();
        private readonly Dictionary<MermaidEdge, Pen> _edgePens = new();
    }
}
