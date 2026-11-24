using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SourceGit.Models
{
    /// <summary>
    ///     Draws a mermaid pie chart: slices, leader lines, percentage labels and an optional title.
    ///     Slices use a fixed, theme-independent palette that stays readable on light and dark
    ///     backgrounds.
    /// </summary>
    public class MermaidPieControl : Control
    {
        public MermaidPieControl(MermaidPieChart pie, MermaidTheme theme, Typeface typeface)
        {
            _theme = theme;
            var bold = new Typeface(typeface.FontFamily, FontStyle.Normal, FontWeight.SemiBold);
            _radius = 96;

            if (!string.IsNullOrEmpty(pie.Title))
                _title = Label(pie.Title, bold, 14, theme.NodeText, 520, false);

            var total = 0.0;
            foreach (var slice in pie.Slices)
                total += slice.Value;

            // Labels and slice geometry in local coordinates, centered on the origin.
            var angle = -Math.PI / 2;
            var slices = new List<SliceDraw>();
            for (var i = 0; i < pie.Slices.Count; i++)
            {
                var slice = pie.Slices[i];
                if (slice.Value <= 0)
                    continue;

                var sweep = slice.Value / total * Math.PI * 2;
                var middle = angle + sweep / 2;
                var direction = new Vector(Math.Cos(middle), Math.Sin(middle));
                var anchor = direction * (_radius + 18);
                var percentage = slice.Value / total * 100;
                var text = pie.ShowData
                    ? $"{slice.Label} ({FormatValue(slice.Value)}, {Math.Round(percentage)}%)"
                    : $"{slice.Label} ({Math.Round(percentage)}%)";

                var label = Label(text, typeface, 12, theme.NodeText, 300, false);
                var draw = new SliceDraw()
                {
                    StartAngle = angle,
                    SweepAngle = sweep,
                    Color = Palette[i % Palette.Length],
                    Label = label,
                    Direction = direction,
                    AnchorY = anchor.Y,
                    LabelTop = anchor.Y - label.Text.Height / 2,
                };

                if (direction.X >= 0)
                {
                    draw.DotLeft = anchor.X - 3;
                    draw.LabelLeft = anchor.X + 9;
                }
                else
                {
                    draw.LabelLeft = anchor.X - label.Text.Width - 9;
                    draw.DotLeft = anchor.X - 3;
                }

                slices.Add(draw);
                angle += sweep;
            }

            // Local extents of the circle and every label, then translate into the control.
            var left = -_radius;
            var right = _radius;
            var top = -_radius;
            var bottom = _radius;
            foreach (var slice in slices)
            {
                left = Math.Min(left, Math.Min(slice.DotLeft, slice.LabelLeft));
                right = Math.Max(right, slice.LabelLeft + slice.Label.Text.Width);
                top = Math.Min(top, slice.LabelTop);
                bottom = Math.Max(bottom, slice.LabelTop + slice.Label.Text.Height);
            }

            var titleHeight = _title == null ? 0 : _title.Text.Height + 12;
            var width = right - left + 2 * Pad;
            if (_title != null)
                width = Math.Max(width, _title.Text.Width + 2 * Pad);

            _size = new Size(width, bottom - top + 2 * Pad + titleHeight);
            _center = new Point((width - (right - left)) / 2 - left, Pad + titleHeight - top);
            foreach (var slice in slices)
            {
                slice.LabelLeft += _center.X;
                slice.DotLeft += _center.X;
                slice.LabelTop += _center.Y;
            }

            _slices = slices;
        }

        protected override Size MeasureOverride(Size availableSize) => _size;

        public override void Render(DrawingContext context)
        {
            if (_title != null)
                context.DrawText(_title.Text, new Point((_size.Width - _title.Text.Width) / 2, Pad));

            var separator = new Pen(_theme.NodeFill, 1.5);
            foreach (var slice in _slices)
            {
                var fill = new SolidColorBrush(slice.Color);
                if (slice.SweepAngle >= Math.PI * 2 - 0.0001)
                {
                    context.DrawEllipse(fill, separator, _center, _radius, _radius);
                    continue;
                }

                var start = new Point(
                    _center.X + Math.Cos(slice.StartAngle) * _radius,
                    _center.Y + Math.Sin(slice.StartAngle) * _radius);
                var endAngle = slice.StartAngle + slice.SweepAngle;
                var end = new Point(
                    _center.X + Math.Cos(endAngle) * _radius,
                    _center.Y + Math.Sin(endAngle) * _radius);

                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(_center, true);
                    ctx.LineTo(start);
                    ctx.ArcTo(end, new Size(_radius, _radius), 0, slice.SweepAngle > Math.PI, SweepDirection.Clockwise);
                    ctx.EndFigure(true);
                }

                context.DrawGeometry(fill, separator, geometry);
            }

            var leader = new Pen(_theme.Edge, 1);
            foreach (var slice in _slices)
            {
                var edge = _center + slice.Direction * (_radius + 2);
                var dot = new Point(slice.DotLeft + 3, slice.AnchorY + _center.Y);
                context.DrawLine(leader, edge, dot);
                context.DrawEllipse(new SolidColorBrush(slice.Color), null, dot, 3, 3);
                context.DrawText(slice.Label.Text, new Point(slice.LabelLeft, slice.LabelTop));
            }
        }

        private static string FormatValue(double value)
        {
            return value == Math.Floor(value)
                ? ((long)value).ToString(CultureInfo.InvariantCulture)
                : value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static MermaidLabel Label(string text, Typeface typeface, double fontSize, IBrush brush, double maxWidth, bool centered)
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

        private class SliceDraw
        {
            public double StartAngle;
            public double SweepAngle;
            public Color Color;
            public MermaidLabel Label;
            public Vector Direction;
            public double AnchorY;
            public double DotLeft;
            public double LabelLeft;
            public double LabelTop;
        }

        private static readonly Color[] Palette =
        {
            Color.Parse("#4E79A7"),
            Color.Parse("#F28E2B"),
            Color.Parse("#E15759"),
            Color.Parse("#76B7B2"),
            Color.Parse("#59A14F"),
            Color.Parse("#EDC948"),
            Color.Parse("#B07AA1"),
            Color.Parse("#FF9DA7"),
            Color.Parse("#9C755F"),
            Color.Parse("#BAB0AC"),
        };

        private readonly MermaidTheme _theme;
        private readonly MermaidLabel _title;
        private readonly List<SliceDraw> _slices;
        private readonly Point _center;
        private readonly Size _size;
        private readonly double _radius;
        private const double Pad = 16;
    }
}
