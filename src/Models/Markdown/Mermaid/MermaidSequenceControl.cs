using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SourceGit.Models
{
    /// <summary>
    ///     Column-based layout and drawing for mermaid sequence diagrams: participant headers and
    ///     lifelines, activation bars, messages, notes, fragments and participant boxes. Everything
    ///     is measured and placed up front; the view is rebuilt on theme changes like other blocks.
    /// </summary>
    public class MermaidSequenceControl : Control
    {
        public MermaidSequenceControl(MermaidSequenceDiagram diagram, MermaidTheme theme, Typeface typeface)
        {
            _theme = theme;
            var bold = new Typeface(typeface.FontFamily, FontStyle.Normal, FontWeight.SemiBold);

            // Columns and headers.
            var columns = new List<Column>();
            foreach (var participant in diagram.Participants)
            {
                var header = Label(participant.Label, typeface, 13, theme.NodeText, 200, true);
                var size = participant.IsActor
                    ? new Size(Math.Max(header.Text.Width + 16, 34), 32 + header.Text.Height + 4)
                    : new Size(header.Text.Width + 24, header.Text.Height + 14);
                columns.Add(new Column() { Participant = participant, Header = header, HeaderSize = size });
            }

            var index = new Dictionary<MermaidSequenceParticipant, int>();
            for (var i = 0; i < columns.Count; i++)
                index[columns[i].Participant] = i;

            // Message labels, notes and fragment texts.
            var messages = new List<MessageDraw>();
            var notes = new List<NoteDraw>();
            var counter = 0;
            foreach (var item in diagram.Items)
            {
                switch (item)
                {
                    case MermaidSequenceMessage message:
                    {
                        var text = message.Label ?? string.Empty;
                        if (diagram.Autonumber && text.Length > 0)
                            text = $"{++counter}. {text}";
                        else if (diagram.Autonumber)
                            counter++;

                        messages.Add(new MessageDraw()
                        {
                            Message = message,
                            From = index[message.From],
                            To = index[message.To],
                            Label = text.Length > 0 ? Label(text, typeface, 12.5, theme.NodeText, 300, true) : null,
                        });
                        break;
                    }
                    case MermaidSequenceNote note:
                        notes.Add(new NoteDraw()
                        {
                            Note = note,
                            Text = Label(note.Text, typeface, 12.5, theme.NodeText, 260, false),
                            First = index[note.Participants[0]],
                            Last = index[note.Participants[^1]],
                        });
                        break;
                }
            }

            // Column gaps: adjacent message labels, self messages, notes and header widths.
            var n = columns.Count;
            var gaps = new double[Math.Max(1, n - 1)];
            for (var i = 0; i < gaps.Length; i++)
                gaps[i] = 70;

            var leftExtra = 0.0;
            var rightExtra = 0.0;
            foreach (var message in messages)
            {
                var labelWidth = message.Label?.Text.Width ?? 0;
                if (message.From == message.To)
                {
                    if (message.From < n - 1)
                        gaps[message.From] = Math.Max(gaps[message.From], labelWidth + 52);
                    else
                        rightExtra = Math.Max(rightExtra, labelWidth + 46);
                }
                else if (Math.Abs(message.From - message.To) == 1)
                {
                    gaps[Math.Min(message.From, message.To)] = Math.Max(gaps[Math.Min(message.From, message.To)], labelWidth + 34);
                }
            }

            foreach (var note in notes)
            {
                var noteWidth = note.Text.Text.Width + 16;
                switch (note.Note.Position)
                {
                    case MermaidNotePosition.Left:
                        if (note.First > 0)
                            gaps[note.First - 1] = Math.Max(gaps[note.First - 1], noteWidth + 24);
                        else
                            leftExtra = Math.Max(leftExtra, noteWidth + 16);
                        break;
                    case MermaidNotePosition.Right:
                        if (note.First < n - 1)
                            gaps[note.First] = Math.Max(gaps[note.First], noteWidth + 24);
                        else
                            rightExtra = Math.Max(rightExtra, noteWidth + 16);
                        break;
                    default:
                        if (note.First == note.Last)
                        {
                            if (note.First > 0)
                                gaps[note.First - 1] = Math.Max(gaps[note.First - 1], noteWidth / 2 + 14);
                            else
                                leftExtra = Math.Max(leftExtra, noteWidth / 2 + 14);

                            if (note.First < n - 1)
                                gaps[note.First] = Math.Max(gaps[note.First], noteWidth / 2 + 14);
                            else
                                rightExtra = Math.Max(rightExtra, noteWidth / 2 + 14);
                        }
                        break;
                }
            }

            for (var i = 0; i + 1 < n; i++)
                gaps[i] = Math.Max(gaps[i], (columns[i].HeaderSize.Width + columns[i + 1].HeaderSize.Width) / 2 + 16);

            var x = new double[n];
            x[0] = EdgePad + leftExtra + columns[0].HeaderSize.Width / 2;
            for (var i = 1; i < n; i++)
                x[i] = x[i - 1] + gaps[i - 1];

            // Notes spanning two participants widen the gaps they cover.
            foreach (var note in notes)
            {
                if (note.Note.Position != MermaidNotePosition.Over || note.First == note.Last)
                    continue;

                var needed = note.Text.Text.Width + 40;
                var span = x[note.Last] - x[note.First];
                if (span >= needed)
                    continue;

                var add = (needed - span) / (note.Last - note.First);
                for (var i = note.First; i < note.Last; i++)
                {
                    gaps[i] += add;
                    for (var j = i + 1; j < n; j++)
                        x[j] += add;
                }
            }

            for (var i = 0; i < n; i++)
                columns[i].X = x[i];

            var width = x[n - 1] + rightExtra + columns[n - 1].HeaderSize.Width / 2 + EdgePad;

            // Vertical placement.
            var headerHeight = 0.0;
            foreach (var column in columns)
                headerHeight = Math.Max(headerHeight, column.HeaderSize.Height);

            var hasBoxes = diagram.Boxes.Count > 0;
            var headerTop = EdgePad + (hasBoxes ? 22 : 0);
            var lifelineStart = headerTop + headerHeight + 14;
            var y = lifelineStart + 8;

            var fragments = new List<FragmentDraw>();
            var stack = new Stack<FragmentDraw>();
            var bars = new List<BarDraw>();
            var openBars = new Dictionary<int, List<(double Start, int Level, BarDraw Bar)>>();

            double LevelOf(int column) => openBars.TryGetValue(column, out var list) ? list.Count : 0;

            void ApplyActivation(int column, bool activate, double at)
            {
                if (activate)
                {
                    if (!openBars.TryGetValue(column, out var list))
                        openBars[column] = list = new List<(double, int, BarDraw)>();

                    var bar = new BarDraw();
                    list.Add((at, list.Count, bar));
                    bars.Add(bar);
                    bar.Column = column;
                    bar.Level = list.Count - 1;
                    bar.Top = at;
                }
                else if (openBars.TryGetValue(column, out var list) && list.Count > 0)
                {
                    var entry = list[^1];
                    list.RemoveAt(list.Count - 1);
                    entry.Bar.Bottom = at;
                }
            }

            foreach (var item in diagram.Items)
            {
                switch (item)
                {
                    case MermaidSequenceMessage message:
                    {
                        var draw = messages.Find(m => m.Message == message);
                        var self = draw.From == draw.To;
                        var labelHeight = draw.Label?.Text.Height ?? 0;
                        var rowHeight = self ? Math.Max(labelHeight, 24) + 12 : labelHeight + 16;
                        draw.Y = self ? y + rowHeight / 2 : y + labelHeight + 8;
                        draw.Self = self;
                        draw.X1 = x[draw.From] + (LevelOf(draw.From) > 0 ? 5 : 0) * (self || draw.To > draw.From ? 1 : -1);
                        draw.X2 = x[draw.To] + (LevelOf(draw.To) > 0 ? 5 : 0) * (self ? 1 : draw.To > draw.From ? -1 : 1);

                        foreach (var fragment in stack)
                            fragment.Touch(draw.From, draw.To);

                        if (message.Activate)
                            ApplyActivation(draw.To, true, draw.Y);
                        if (message.Deactivate)
                            ApplyActivation(draw.From, false, draw.Y);

                        y += rowHeight + 10;
                        break;
                    }
                    case MermaidSequenceNote note:
                    {
                        var draw = notes.Find(nn => nn.Note == note);
                        var noteHeight = draw.Text.Text.Height + 12;
                        draw.Bounds = new Rect(0, y + 4, draw.Text.Text.Width + 16, noteHeight);
                        foreach (var fragment in stack)
                            fragment.Touch(draw.First, draw.Last);

                        y += noteHeight + 14;
                        break;
                    }
                    case MermaidSequenceFragmentStart start:
                    {
                        var fragment = new FragmentDraw()
                        {
                            Kind = start.Kind,
                            Color = start.Color,
                            Top = y,
                            Depth = stack.Count,
                            Tab = start.Kind == MermaidFragmentKind.Rect ? null : Label(start.Kind.ToString().ToLowerInvariant(), bold, 10.5, theme.SubgraphTitle, 100, false),
                            Text = start.Kind == MermaidFragmentKind.Rect || string.IsNullOrEmpty(start.Text) ? null : Label(start.Text, typeface, 12, theme.NodeText, 400, false),
                        };
                        fragments.Add(fragment);
                        stack.Push(fragment);
                        y += 30;
                        break;
                    }
                    case MermaidSequenceFragmentDivider divider:
                    {
                        if (stack.Count == 0)
                            break;

                        var fragment = stack.Peek();
                        var text = string.IsNullOrEmpty(divider.Text) ? divider.Keyword : divider.Text;
                        fragment.Dividers.Add(new DividerDraw() { Y = y + 4, Label = Label($"[{text}]", typeface, 12, theme.SubgraphTitle, 300, true) });
                        y += 24;
                        break;
                    }
                    case MermaidSequenceFragmentEnd:
                    {
                        if (stack.Count == 0)
                            break;

                        var fragment = stack.Pop();
                        fragment.Bottom = y + 6;
                        y += 16;
                        break;
                    }
                    case MermaidSequenceActivation activation:
                    {
                        var column = index[activation.Participant];
                        foreach (var fragment in stack)
                            fragment.Touch(column, column);

                        ApplyActivation(column, activation.Activate, y);
                        break;
                    }
                }
            }

            var lifelineEnd = y + 4;
            foreach (var list in openBars.Values)
            {
                foreach (var entry in list)
                    entry.Bar.Bottom = lifelineEnd;
            }

            var bottomTop = lifelineEnd + 12;
            var height = bottomTop + headerHeight + EdgePad;

            // Horizontal extents of fragments, boxes and final draw geometry.
            foreach (var fragment in fragments)
            {
                if (fragment.Kind == MermaidFragmentKind.Rect)
                {
                    fragment.Bounds = new Rect(EdgePad, fragment.Top, width - 2 * EdgePad, fragment.Bottom - fragment.Top);
                    continue;
                }

                var left = fragment.Min < 0 ? x[0] : x[fragment.Min];
                var right = fragment.Max < 0 ? x[n - 1] : x[fragment.Max];
                fragment.Bounds = new Rect(
                    left - 28 + fragment.Depth * 6,
                    fragment.Top,
                    right - left + 56 - fragment.Depth * 12,
                    fragment.Bottom - fragment.Top);

                if (fragment.Text != null)
                    width = Math.Max(width, fragment.Bounds.X + (fragment.Tab?.Text.Width ?? 0) + 22 + fragment.Text.Text.Width + EdgePad);
            }

            foreach (var column in columns)
            {
                column.Lifeline = new Rect(column.X, lifelineStart, 0, lifelineEnd - lifelineStart);
                column.TopHeader = new Rect(column.X - column.HeaderSize.Width / 2, headerTop, column.HeaderSize.Width, column.HeaderSize.Height);
                column.BottomHeader = new Rect(column.X - column.HeaderSize.Width / 2, bottomTop, column.HeaderSize.Width, column.HeaderSize.Height);
            }

            foreach (var bar in bars)
            {
                var column = columns[bar.Column];
                bar.Bounds = new Rect(column.X - 5 + bar.Level * 4, bar.Top, 10, Math.Max(4, bar.Bottom - bar.Top));
            }

            foreach (var note in notes)
            {
                var bounds = note.Bounds;
                double left;
                switch (note.Note.Position)
                {
                    case MermaidNotePosition.Left:
                        left = x[note.First] - 12 - bounds.Width;
                        break;
                    case MermaidNotePosition.Right:
                        left = x[note.First] + 12;
                        break;
                    default:
                        left = (x[note.First] + x[note.Last]) / 2 - bounds.Width / 2;
                        break;
                }

                note.Bounds = new Rect(left, bounds.Y, bounds.Width, bounds.Height);
            }

            foreach (var box in diagram.Boxes)
            {
                if (box.Members.Count == 0)
                    continue;

                var first = int.MaxValue;
                var last = int.MinValue;
                foreach (var member in box.Members)
                {
                    var i = index[member];
                    first = Math.Min(first, i);
                    last = Math.Max(last, i);
                }

                var left = x[first] - columns[first].HeaderSize.Width / 2 - 10;
                var right = x[last] + columns[last].HeaderSize.Width / 2 + 10;
                _boxes.Add(new BoxDraw()
                {
                    Bounds = new Rect(left, EdgePad, right - left, height - 2 * EdgePad),
                    Color = box.Color,
                    Title = string.IsNullOrEmpty(box.Title) ? null : Label(box.Title, bold, 12, theme.SubgraphTitle, 300, true),
                });
            }

            _columns = columns;
            _messages = messages;
            _notes = notes;
            _fragments = fragments;
            _bars = bars;
            _size = new Size(width, height);
        }

        protected override Size MeasureOverride(Size availableSize) => _size;

        public override void Render(DrawingContext context)
        {
            foreach (var box in _boxes)
            {
                var fill = box.Color is { } color ? new SolidColorBrush(color, color.A == 255 ? 0.18 : color.A / 255.0) : _theme.SubgraphFill;
                context.DrawRectangle(fill, new Pen(_theme.SubgraphStroke, 1), box.Bounds, 4, 4);
                if (box.Title != null)
                    context.DrawText(box.Title.Text, new Point(box.Bounds.Center.X - box.Title.BoxWidth / 2, box.Bounds.Y + 4));
            }

            foreach (var fragment in _fragments)
            {
                var fill = fragment.Kind == MermaidFragmentKind.Rect && fragment.Color is { } color
                    ? new SolidColorBrush(color, color.A == 255 ? 0.22 : color.A / 255.0)
                    : null;
                context.DrawRectangle(fill, new Pen(_theme.SubgraphStroke, 1), fragment.Bounds, 3, 3);
            }

            var lifelinePen = new Pen(_theme.NodeStroke, 1, new DashStyle(new double[] { 3, 3 }, 0));
            foreach (var column in _columns)
                context.DrawLine(lifelinePen, new Point(column.X, column.Lifeline.Y), new Point(column.X, column.Lifeline.Bottom));

            var barFill = new SolidColorBrush((_theme.SubgraphFill as ISolidColorBrush)?.Color ?? Colors.Gray, 0.45);
            foreach (var bar in _bars)
                context.DrawRectangle(barFill, new Pen(_theme.NodeStroke, 1), bar.Bounds);

            foreach (var message in _messages)
                DrawMessage(context, message);

            foreach (var note in _notes)
            {
                context.DrawRectangle(_theme.SubgraphFill, new Pen(_theme.SubgraphStroke, 1), note.Bounds, 2, 2);
                context.DrawText(note.Text.Text, new Point(note.Bounds.X + 8, note.Bounds.Y + 6));
            }

            foreach (var column in _columns)
            {
                DrawHeader(context, column, column.TopHeader);
                DrawHeader(context, column, column.BottomHeader);
            }

            foreach (var fragment in _fragments)
            {
                if (fragment.Tab != null)
                {
                    var tabWidth = fragment.Tab.Text.Width + 14;
                    var tab = new Rect(fragment.Bounds.X, fragment.Bounds.Y, tabWidth, 17);
                    context.DrawRectangle(_theme.EdgeLabelBg, new Pen(_theme.SubgraphStroke, 1), tab);
                    context.DrawText(fragment.Tab.Text, new Point(tab.X + 7, tab.Y + 3));
                    if (fragment.Text != null)
                        context.DrawText(fragment.Text.Text, new Point(tab.Right + 8, tab.Y + 3));
                }

                foreach (var divider in fragment.Dividers)
                {
                    context.DrawLine(new Pen(_theme.SubgraphStroke, 1, new DashStyle(new double[] { 4, 3 }, 0)),
                        new Point(fragment.Bounds.X, divider.Y), new Point(fragment.Bounds.Right, divider.Y));
                    var center = fragment.Bounds.Center.X;
                    var bg = new Rect(center - divider.Label.Text.Width / 2 - 5, divider.Y - divider.Label.Text.Height / 2 - 1, divider.Label.Text.Width + 10, divider.Label.Text.Height + 2);
                    context.DrawRectangle(_theme.EdgeLabelBg, null, bg);
                    context.DrawText(divider.Label.Text, new Point(center - divider.Label.BoxWidth / 2, divider.Y - divider.Label.Text.Height / 2));
                }
            }
        }

        private void DrawHeader(DrawingContext context, Column column, Rect rect)
        {
            if (column.Participant.IsActor)
            {
                var cx = rect.Center.X;
                var top = rect.Y;
                var pen = new Pen(_theme.NodeText, 1.4);
                context.DrawEllipse(_theme.NodeFill, pen, new Point(cx, top + 6), 5, 5);
                context.DrawLine(pen, new Point(cx, top + 11), new Point(cx, top + 22));
                context.DrawLine(pen, new Point(cx - 7, top + 15), new Point(cx + 7, top + 15));
                context.DrawLine(pen, new Point(cx, top + 22), new Point(cx - 6, top + 30));
                context.DrawLine(pen, new Point(cx, top + 22), new Point(cx + 6, top + 30));
                context.DrawText(column.Header.Text, new Point(cx - column.Header.BoxWidth / 2, top + 32));
            }
            else
            {
                context.DrawRectangle(_theme.NodeFill, new Pen(_theme.NodeStroke, 1.2), rect, 3, 3);
                context.DrawText(column.Header.Text, new Point(rect.Center.X - column.Header.BoxWidth / 2, rect.Center.Y - column.Header.Text.Height / 2));
            }
        }

        private void DrawMessage(DrawingContext context, MessageDraw message)
        {
            var arrow = message.Message.Arrow;
            var dotted = arrow is MermaidSequenceArrow.DottedLine or MermaidSequenceArrow.DottedArrow
                or MermaidSequenceArrow.DottedCross or MermaidSequenceArrow.DottedOpen or MermaidSequenceArrow.BiDottedArrow;
            var pen = new Pen(_theme.Edge, 1.4, dotted ? new DashStyle(new double[] { 4, 3 }, 0) : null);
            var head = arrow is MermaidSequenceArrow.Line or MermaidSequenceArrow.DottedLine ? MermaidEndKind.None
                : arrow is MermaidSequenceArrow.Cross or MermaidSequenceArrow.DottedCross ? MermaidEndKind.Cross
                : arrow is MermaidSequenceArrow.Open or MermaidSequenceArrow.DottedOpen ? MermaidEndKind.Open
                : MermaidEndKind.Arrow;

            if (message.Self)
            {
                var x = message.X1;
                var cy = message.Y;
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(new Point(x, cy - 8), false);
                    ctx.LineTo(new Point(x + 32, cy - 8));
                    ctx.LineTo(new Point(x + 32, cy + 8));
                    ctx.LineTo(new Point(x, cy + 8));
                }

                context.DrawGeometry(null, pen, geometry);
                if (head != MermaidEndKind.None)
                    DrawHead(context, new Point(x, cy + 8), new Vector(-1, 0), head, pen.Brush);
                if (message.Label != null)
                {
                    var left = x + 40;
                    context.DrawText(message.Label.Text, new Point(left + message.Label.Text.Width / 2 - message.Label.BoxWidth / 2, cy - message.Label.Text.Height / 2));
                }
                return;
            }

            var start = new Point(message.X1, message.Y);
            var end = new Point(message.X2, message.Y);
            var direction = new Vector(end.X - start.X, end.Y - start.Y);
            if (direction.Length < 0.001)
                return;

            var lineEnd = head == MermaidEndKind.None ? end : Shorten(end, direction, head == MermaidEndKind.Arrow ? 8 : 6);
            var lineStart = arrow is MermaidSequenceArrow.BiArrow or MermaidSequenceArrow.BiDottedArrow ? Shorten(start, -direction, 8) : start;
            context.DrawLine(pen, lineStart, lineEnd);

            if (head != MermaidEndKind.None)
                DrawHead(context, end, direction, head, pen.Brush);
            if (arrow is MermaidSequenceArrow.BiArrow or MermaidSequenceArrow.BiDottedArrow)
                DrawHead(context, start, -direction, MermaidEndKind.Arrow, pen.Brush);

            if (message.Label != null)
            {
                var center = (start.X + end.X) / 2;
                context.DrawText(message.Label.Text, new Point(center - message.Label.BoxWidth / 2, message.Y - 8 - message.Label.Text.Height));
            }
        }

        private static Point Shorten(Point tip, Vector direction, double amount)
        {
            var length = direction.Length;
            return length < 0.001 ? tip : tip - direction * (amount / length);
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
                case MermaidEndKind.Open:
                {
                    var basePoint = tip - dir * 9;
                    var pen = new Pen(brush, 1.4);
                    context.DrawLine(pen, tip, basePoint + normal * 4.5);
                    context.DrawLine(pen, tip, basePoint - normal * 4.5);
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
            }
        }

        private static MermaidLabel Label(string text, Typeface typeface, double fontSize, IBrush brush, double maxWidth, bool centered)
        {
            var formatted = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
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

        private class Column
        {
            public MermaidSequenceParticipant Participant;
            public double X;
            public MermaidLabel Header;
            public Size HeaderSize;
            public Rect Lifeline;
            public Rect TopHeader;
            public Rect BottomHeader;
        }

        private class MessageDraw
        {
            public MermaidSequenceMessage Message;
            public int From;
            public int To;
            public MermaidLabel Label;
            public double X1;
            public double X2;
            public double Y;
            public bool Self;
        }

        private class NoteDraw
        {
            public MermaidSequenceNote Note;
            public MermaidLabel Text;
            public int First;
            public int Last;
            public Rect Bounds;
        }

        private class FragmentDraw
        {
            public MermaidFragmentKind Kind;
            public Color? Color;
            public double Top;
            public double Bottom;
            public int Depth;
            public int Min = -1;
            public int Max = -1;
            public Rect Bounds;
            public MermaidLabel Tab;
            public MermaidLabel Text;
            public List<DividerDraw> Dividers = new();

            public void Touch(int from, int to)
            {
                Min = Min < 0 ? Math.Min(from, to) : Math.Min(Min, Math.Min(from, to));
                Max = Math.Max(Max, Math.Max(from, to));
            }
        }

        private class DividerDraw
        {
            public double Y;
            public MermaidLabel Label;
        }

        private class BarDraw
        {
            public int Column;
            public int Level;
            public double Top;
            public double Bottom;
            public Rect Bounds;
        }

        private class BoxDraw
        {
            public Rect Bounds;
            public Color? Color;
            public MermaidLabel Title;
        }

        private readonly MermaidTheme _theme;
        private readonly List<Column> _columns;
        private readonly List<MessageDraw> _messages;
        private readonly List<NoteDraw> _notes;
        private readonly List<FragmentDraw> _fragments;
        private readonly List<BarDraw> _bars;
        private readonly List<BoxDraw> _boxes = new();
        private readonly Size _size;
        private const double EdgePad = 12;
    }
}
