using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Media;

namespace SourceGit.Models
{
    /// <summary>A prepared compartment row. Value is an optional right-aligned cell.</summary>
    public class MermaidTableRowDraw
    {
        public MermaidLabel Text { get; set; }
        public MermaidLabel Value { get; set; }
        public bool Separator { get; set; }
    }

    /// <summary>Prepared content of a class or entity box.</summary>
    public class MermaidTableDraw
    {
        public MermaidLabel Annotation { get; set; }
        public List<MermaidTableRowDraw> Rows { get; } = new();
    }

    /// <summary>
    ///     Measures and draws the compartment boxes used by class and ER diagrams. Measure and Draw
    ///     walk the same vertical sequence so the node size handed to the layout always matches the
    ///     painted content.
    /// </summary>
    public static class MermaidTable
    {
        public static Size Measure(MermaidLabel title, MermaidTableDraw table)
        {
            var width = title.Text.Width;
            if (table.Annotation != null)
                width = Math.Max(width, table.Annotation.Text.Width);

            foreach (var row in table.Rows)
            {
                var rowWidth = row.Text.Text.Width;
                if (row.Value != null)
                    rowWidth += 12 + row.Value.Text.Width;

                width = Math.Max(width, rowWidth);
            }

            var height = PadY + title.Text.Height;
            if (table.Annotation != null)
                height += Gap + table.Annotation.Text.Height;

            if (table.Rows.Count > 0)
            {
                height += SectionGap;
                foreach (var row in table.Rows)
                {
                    if (row.Separator)
                        height += SectionGap;

                    height += RowHeight(row);
                }
            }

            height += PadY;
            return new Size(Math.Max(88, width + 2 * PadX), height);
        }

        public static void Draw(DrawingContext context, Rect bounds, MermaidLabel title, MermaidTableDraw table, MermaidTheme theme)
        {
            var separator = new Pen(theme.NodeStroke, 1);
            var y = bounds.Y + PadY;

            Center(context, title, bounds.Center.X, y);
            y += title.Text.Height;

            if (table.Annotation != null)
            {
                y += Gap;
                Center(context, table.Annotation, bounds.Center.X, y);
                y += table.Annotation.Text.Height;
            }

            if (table.Rows.Count == 0)
                return;

            y += SectionGap;
            context.DrawLine(separator, new Point(bounds.X + 1, y), new Point(bounds.Right - 1, y));

            foreach (var row in table.Rows)
            {
                if (row.Separator)
                {
                    y += SectionGap;
                    context.DrawLine(separator, new Point(bounds.X + 1, y), new Point(bounds.Right - 1, y));
                }

                context.DrawText(row.Text.Text, new Point(bounds.X + PadX, y));
                if (row.Value != null)
                    context.DrawText(row.Value.Text, new Point(bounds.Right - PadX - row.Value.Text.Width, y));

                y += RowHeight(row);
            }
        }

        private static void Center(DrawingContext context, MermaidLabel label, double center, double y)
        {
            context.DrawText(label.Text, new Point(center - label.Text.Width / 2, y));
        }

        private static double RowHeight(MermaidTableRowDraw row)
        {
            return Math.Max(row.Text.Text.Height, row.Value?.Text.Height ?? 0) + RowGap;
        }

        private const double PadX = 12;
        private const double PadY = 8;
        private const double Gap = 3;
        private const double SectionGap = 9;
        private const double RowGap = 4;
    }
}
