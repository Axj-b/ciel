using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SourceGit.Models
{
    /// <summary>
    ///     Draws a mermaid gantt chart: a task-name column, section headers, a date axis with grid
    ///     lines and one bar (or milestone diamond) per task. Everything is measured up front and
    ///     the view is rebuilt on theme changes like the other diagrams.
    /// </summary>
    public class MermaidGanttControl : Control
    {
        public MermaidGanttControl(MermaidGanttChart gantt, MermaidTheme theme, Typeface typeface)
        {
            _theme = theme;
            _gantt = gantt;
            var bold = new Typeface(typeface.FontFamily, FontStyle.Normal, FontWeight.SemiBold);

            if (!string.IsNullOrEmpty(gantt.Title))
                _title = Label(gantt.Title, bold, 14, theme.NodeText, 640, false);

            // Time range, padded by half a day so bars never sit exactly on the chart edge.
            var min = DateTime.MaxValue;
            var max = DateTime.MinValue;
            foreach (var task in gantt.Tasks)
            {
                var end = task.Milestone ? task.Start : task.End;
                min = task.Start < min ? task.Start : min;
                max = end > max ? end : max;
            }

            if (max <= min)
                max = min.AddDays(1);

            _rangeStart = min.AddDays(-0.5);
            _span = (max.AddDays(0.5) - _rangeStart).TotalDays;
            _dayWidth = Math.Clamp(860.0 / _span, 1.5, 26.0);
            if (_dayWidth * _span < 420)
                _dayWidth = 420 / _span;

            _chartWidth = _span * _dayWidth;

            // Labels and per-row heights.
            var labelWidth = 80.0;
            var sections = new List<SectionDraw>();
            foreach (var section in gantt.Sections)
            {
                var draw = new SectionDraw() { Section = section };
                if (section.Name != null)
                {
                    draw.Label = Label(section.Name, bold, 12.5, theme.NodeText, 220, false);
                    draw.Height = Math.Max(24, draw.Label.Text.Height + 10);
                    labelWidth = Math.Max(labelWidth, draw.Label.Text.Width);
                }

                foreach (var task in section.Tasks)
                {
                    var taskDraw = new TaskDraw()
                    {
                        Task = task,
                        Label = Label(task.Name, typeface, 12, theme.NodeText, 220, false),
                    };
                    taskDraw.Height = Math.Max(24, taskDraw.Label.Text.Height + 12);
                    labelWidth = Math.Max(labelWidth, taskDraw.Label.Text.Width);
                    draw.Tasks.Add(taskDraw);
                }

                sections.Add(draw);
            }

            var titleHeight = _title == null ? 0 : _title.Text.Height + 14;
            var axisHeight = 22.0;
            _chartX = Pad + labelWidth + 14;
            _chartRight = _chartX + _chartWidth;
            _width = Math.Max(_chartRight + Pad + 16, (_title?.Text.Width ?? 0) + 2 * Pad);
            _chartTop = Pad + titleHeight + axisHeight;

            // Assign rows and build bars in one pass.
            var y = _chartTop;
            foreach (var section in sections)
            {
                if (section.Label != null)
                {
                    section.Top = y;
                    y += section.Height;
                }

                foreach (var task in section.Tasks)
                {
                    task.Top = y;
                    y += task.Height;
                    var middle = task.Top + task.Height / 2;
                    if (task.Task.Milestone)
                    {
                        var x = ToX(task.Task.Start);
                        task.Diamond = new[]
                        {
                            new Point(x, middle - 6),
                            new Point(x + 6, middle),
                            new Point(x, middle + 6),
                            new Point(x - 6, middle),
                        };
                    }
                    else
                    {
                        var left = ToX(task.Task.Start);
                        var right = ToX(task.Task.End);
                        task.Bar = new Rect(left, middle - 7, Math.Max(2, right - left), 14);
                    }
                }
            }

            _contentBottom = y;
            _height = y + Pad;
            _sections = sections;

            // Axis ticks.
            var ticks = new List<TickDraw>();
            foreach (var date in BuildTicks(_rangeStart, _rangeStart.AddDays(_span)))
            {
                ticks.Add(new TickDraw()
                {
                    X = ToX(date),
                    Label = Label(FormatTick(date), typeface, 10.5, theme.SubgraphTitle, 120, false),
                });
            }

            _ticks = ticks;
        }

        protected override Size MeasureOverride(Size availableSize) => new(_width, _height);

        public override void Render(DrawingContext context)
        {
            if (_title != null)
                context.DrawText(_title.Text, new Point((_width - _title.Text.Width) / 2, Pad));

            // Section headers and their separators.
            foreach (var section in _sections)
            {
                if (section.Label == null)
                    continue;

                var labelY = section.Top + (section.Height - section.Label.Text.Height) / 2;
                context.DrawText(section.Label.Text, new Point(Pad, labelY));
                context.DrawLine(new Pen(_theme.SubgraphStroke, 1),
                    new Point(_chartX, section.Top + section.Height - 1),
                    new Point(_chartRight, section.Top + section.Height - 1));
            }

            // Axis baseline and grid.
            var gridPen = new Pen(_theme.SubgraphStroke, 1);
            context.DrawLine(gridPen, new Point(_chartX, _chartTop), new Point(_chartRight, _chartTop));
            foreach (var tick in _ticks)
            {
                context.DrawLine(gridPen, new Point(tick.X, _chartTop), new Point(tick.X, _contentBottom));
                var maxLeft = Math.Max(_chartX, _width - Pad - tick.Label.Text.Width);
                var left = Math.Clamp(tick.X - tick.Label.Text.Width / 2, _chartX, maxLeft);
                context.DrawText(tick.Label.Text, new Point(left, _chartTop - 17));
            }

            // Task labels, bars and milestones.
            foreach (var section in _sections)
            {
                foreach (var task in section.Tasks)
                {
                    var labelY = task.Top + (task.Height - task.Label.Text.Height) / 2;
                    context.DrawText(task.Label.Text, new Point(Pad, labelY));

                    var fill = new SolidColorBrush(StatusColor(task.Task.Status));
                    if (task.Diamond != null)
                    {
                        var geometry = new StreamGeometry();
                        using (var ctx = geometry.Open())
                        {
                            ctx.BeginFigure(task.Diamond[0], true);
                            ctx.LineTo(task.Diamond[1]);
                            ctx.LineTo(task.Diamond[2]);
                            ctx.LineTo(task.Diamond[3]);
                            ctx.EndFigure(true);
                        }

                        context.DrawGeometry(fill, new Pen(_theme.NodeStroke, 1), geometry);
                    }
                    else
                    {
                        context.DrawRectangle(fill, null, task.Bar, 2, 2);
                    }
                }
            }
        }

        private double ToX(DateTime date)
        {
            return _chartX + (date - _rangeStart).TotalDays * _dayWidth;
        }

        private string FormatTick(DateTime date)
        {
            if (_gantt.AxisFormat != null)
                return MermaidGanttChart.FormatAxis(date, _gantt.AxisFormat);

            if (_span <= 120)
                return date.ToString("MMM d", CultureInfo.InvariantCulture);
            if (_span <= 900)
                return date.ToString("MMM yyyy", CultureInfo.InvariantCulture);
            return date.ToString("yyyy", CultureInfo.InvariantCulture);
        }

        private static List<DateTime> BuildTicks(DateTime start, DateTime end)
        {
            var span = (end - start).TotalDays;
            var ticks = new List<DateTime>();
            if (span <= 10)
            {
                for (var d = start.Date; d <= end; d = d.AddDays(1))
                    ticks.Add(d);
            }
            else if (span <= 40)
            {
                for (var d = start.Date; d <= end; d = d.AddDays(5))
                    ticks.Add(d);
            }
            else if (span <= 120)
            {
                var d = start.Date;
                while (d.DayOfWeek != DayOfWeek.Monday)
                    d = d.AddDays(1);

                for (; d <= end; d = d.AddDays(7))
                    ticks.Add(d);
            }
            else if (span <= 500)
            {
                var d = new DateTime(start.Year, start.Month, 1);
                while (d < start)
                    d = d.AddMonths(1);

                for (; d <= end; d = d.AddMonths(1))
                    ticks.Add(d);
            }
            else if (span <= 2000)
            {
                var d = new DateTime(start.Year, ((start.Month - 1) / 3) * 3 + 1, 1);
                while (d < start)
                    d = d.AddMonths(3);

                for (; d <= end; d = d.AddMonths(3))
                    ticks.Add(d);
            }
            else
            {
                var d = new DateTime(start.Year, 1, 1);
                while (d < start)
                    d = d.AddYears(1);

                for (; d <= end; d = d.AddYears(1))
                    ticks.Add(d);
            }

            if (ticks.Count == 0)
                ticks.Add(start.Date);

            return ticks;
        }

        private static Color StatusColor(MermaidGanttStatus status)
        {
            return status switch
            {
                MermaidGanttStatus.Active => Color.Parse("#3F6FD8"),
                MermaidGanttStatus.Done => Color.Parse("#9AA5B1"),
                MermaidGanttStatus.Crit => Color.Parse("#E5484D"),
                _ => Color.Parse("#6C8CD5"),
            };
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

        private class TaskDraw
        {
            public MermaidGanttTask Task;
            public MermaidLabel Label;
            public double Top;
            public double Height;
            public Rect Bar;
            public Point[] Diamond;
        }

        private class SectionDraw
        {
            public MermaidGanttSection Section;
            public MermaidLabel Label;
            public double Top;
            public double Height;
            public List<TaskDraw> Tasks { get; } = new();
        }

        private class TickDraw
        {
            public double X;
            public MermaidLabel Label;
        }

        private readonly MermaidTheme _theme;
        private readonly MermaidGanttChart _gantt;
        private readonly MermaidLabel _title;
        private readonly List<SectionDraw> _sections;
        private readonly List<TickDraw> _ticks;
        private readonly DateTime _rangeStart;
        private readonly double _span;
        private readonly double _dayWidth;
        private readonly double _chartWidth;
        private readonly double _chartX;
        private readonly double _chartRight;
        private readonly double _chartTop;
        private readonly double _contentBottom;
        private readonly double _width;
        private readonly double _height;
        private const double Pad = 16;
    }
}
