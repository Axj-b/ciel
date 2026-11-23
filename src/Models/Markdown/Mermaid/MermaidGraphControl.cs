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

    /// <summary>
    ///     Draws a laid-out mermaid graph with DrawingContext primitives. All brushes and text are
    ///     prepared up front; the view is rebuilt on theme changes like every other markdown block.
    /// </summary>
    public class MermaidGraphControl : Control
    {
        public MermaidGraphControl(
            MermaidLayoutResult layout,
            IReadOnlyDictionary<MermaidNode, MermaidLabel> labels,
            IReadOnlyDictionary<MermaidEdge, MermaidLabel> edgeLabels,
            IReadOnlyDictionary<MermaidSubgraph, MermaidLabel> titles,
            MermaidTheme theme)
        {
            _layout = layout;
            _labels = labels;
            _edgeLabels = edgeLabels;
            _titles = titles;
            _theme = theme;

            foreach (var item in layout.Nodes)
            {
                var style = item.Node.ResolvedStyle;
                var fill = style?.Fill is { } fillColor ? new SolidColorBrush(fillColor) : theme.NodeFill;
                var stroke = style?.Stroke is { } strokeColor ? new SolidColorBrush(strokeColor) : theme.NodeStroke;
                var width = style?.StrokeWidth ?? 1.2;
                var pen = new Pen(stroke, width, style?.DashArray != null ? new DashStyle(style.DashArray, 0) : null);
                _nodePaints[item.Node] = (fill, pen);
            }

            foreach (var route in layout.Edges)
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
                    DrawHead(context, end, end - c2, edge.Head, pen.Brush);
            }
            else if (route.Points.Count >= 2)
            {
                var points = new List<Point>(route.Points);
                var headDir = points[^1] - points[^2];
                var tailDir = points[0] - points[1];

                if (edge.Head == MermaidEndKind.Arrow)
                    points[^1] = Shorten(points[^1], headDir, 8);
                else if (edge.Head != MermaidEndKind.None)
                    points[^1] = Shorten(points[^1], headDir, 7);

                if (edge.TailArrow)
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
                    DrawHead(context, route.Points[^1], headDir, edge.Head, pen.Brush);
                if (edge.TailArrow)
                    DrawHead(context, route.Points[0], tailDir, MermaidEndKind.Arrow, pen.Brush);
            }

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
        }

        private static Point Shorten(Point tip, Vector direction, double amount)
        {
            var length = direction.Length;
            if (length < 0.001)
                return tip;

            return tip - direction * (amount / length);
        }

        private static void DrawHead(DrawingContext context, Point tip, Vector direction, MermaidEndKind kind, IBrush brush)
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
        private readonly IReadOnlyDictionary<MermaidEdge, MermaidLabel> _edgeLabels;
        private readonly IReadOnlyDictionary<MermaidSubgraph, MermaidLabel> _titles;
        private readonly MermaidTheme _theme;
        private readonly Dictionary<MermaidNode, (IBrush Fill, IPen Pen)> _nodePaints = new();
        private readonly Dictionary<MermaidEdge, Pen> _edgePens = new();
    }
}
