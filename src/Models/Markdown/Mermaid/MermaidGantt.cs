using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SourceGit.Models
{
    public enum MermaidGanttStatus
    {
        Default,
        Done,
        Active,
        Crit,
    }

    public class MermaidGanttTask
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public bool Milestone { get; set; }
        public MermaidGanttStatus Status { get; set; }
        public MermaidGanttSection Section { get; set; }
    }

    public class MermaidGanttSection
    {
        public string Name { get; set; }
        public List<MermaidGanttTask> Tasks { get; } = new();
    }

    public class MermaidGanttChart
    {
        public string Title { get; set; }
        public string AxisFormat { get; set; }
        public List<MermaidGanttSection> Sections { get; } = new();
        public List<MermaidGanttTask> Tasks { get; } = new();

        /// <summary>
        ///     Formats an axis tick using a strftime-style mermaid format. Returns null for tokens
        ///     this renderer does not understand, which makes the parser reject the diagram.
        /// </summary>
        public static string FormatAxis(DateTime date, string format)
        {
            if (string.IsNullOrEmpty(format))
                return null;

            var builder = new StringBuilder();
            for (var i = 0; i < format.Length; i++)
            {
                var c = format[i];
                if (c != '%')
                {
                    builder.Append(c);
                    continue;
                }

                i++;
                if (i >= format.Length)
                    return null;

                switch (format[i])
                {
                    case 'Y':
                        builder.Append(date.ToString("yyyy", CultureInfo.InvariantCulture));
                        break;
                    case 'y':
                        builder.Append(date.ToString("yy", CultureInfo.InvariantCulture));
                        break;
                    case 'm':
                        builder.Append(date.ToString("MM", CultureInfo.InvariantCulture));
                        break;
                    case 'd':
                        builder.Append(date.ToString("dd", CultureInfo.InvariantCulture));
                        break;
                    case 'e':
                        builder.Append(date.Day.ToString(CultureInfo.InvariantCulture));
                        break;
                    case 'b':
                        builder.Append(date.ToString("MMM", CultureInfo.InvariantCulture));
                        break;
                    case 'B':
                        builder.Append(date.ToString("MMMM", CultureInfo.InvariantCulture));
                        break;
                    case 'H':
                        builder.Append(date.ToString("HH", CultureInfo.InvariantCulture));
                        break;
                    case 'M':
                        builder.Append(date.ToString("mm", CultureInfo.InvariantCulture));
                        break;
                    case '%':
                        builder.Append('%');
                        break;
                    default:
                        return null;
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>
    ///     Strict parser for a practical subset of mermaid gantt charts: title, a YYYY-MM-DD
    ///     dateFormat, a strftime-style axisFormat, sections and tasks with an optional id,
    ///     status tag, explicit start/end, day/week duration or an "after" dependency. Anything
    ///     else (exclusions, today markers, other date formats, hour durations, unknown tags)
    ///     returns null so the markdown renderer falls back to a plain code block.
    /// </summary>
    public static class MermaidGantt
    {
        public static MermaidGanttChart Parse(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > 128 * 1024)
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

            public MermaidGanttChart Run()
            {
                var statements = MermaidSource.Statements(_code);
                if (statements == null || statements.Count < 2 || statements.Count > 400)
                    return null;

                if (!statements[0].Equals("gantt", StringComparison.OrdinalIgnoreCase))
                    return null;

                var gantt = new MermaidGanttChart();
                var tasks = new Dictionary<string, MermaidGanttTask>(StringComparer.Ordinal);
                MermaidGanttSection current = null;

                for (var i = 1; i < statements.Count; i++)
                {
                    var statement = statements[i];
                    var keyword = MermaidSource.Keyword(statement);
                    var rest = keyword.Length == 0 ? string.Empty : statement[keyword.Length..].TrimStart();
                    var taskShaped = rest.StartsWith(":");

                    switch (keyword)
                    {
                        case "title" when !taskShaped:
                            if (rest.Length == 0 || gantt.Title != null)
                                return null;

                            gantt.Title = rest.Trim();
                            break;
                        case "dateformat" when !taskShaped:
                            if (!rest.Trim().Equals("YYYY-MM-DD", StringComparison.OrdinalIgnoreCase))
                                return null;

                            break;
                        case "axisformat" when !taskShaped:
                        {
                            var format = rest.Trim();
                            if (format.Length == 0 || MermaidGanttChart.FormatAxis(DateTime.Today, format) == null)
                                return null;

                            gantt.AxisFormat = format;
                            break;
                        }
                        case "section" when !taskShaped:
                        {
                            var name = rest.Trim();
                            if (name.Length == 0 || gantt.Sections.Count >= 40)
                                return null;

                            current = new MermaidGanttSection() { Name = name };
                            gantt.Sections.Add(current);
                            break;
                        }
                        default:
                            if (!ParseTask(gantt, statement, ref current, tasks))
                                return null;

                            break;
                    }
                }

                return gantt.Tasks.Count == 0 || SpanTooLarge(gantt) ? null : gantt;
            }

            private static bool SpanTooLarge(MermaidGanttChart gantt)
            {
                var min = DateTime.MaxValue;
                var max = DateTime.MinValue;
                foreach (var task in gantt.Tasks)
                {
                    var end = task.Milestone ? task.Start : task.End;
                    min = task.Start < min ? task.Start : min;
                    max = end > max ? end : max;
                }

                return (max - min).TotalDays > 3650;
            }

            private static bool ParseTask(MermaidGanttChart gantt, string statement, ref MermaidGanttSection current, Dictionary<string, MermaidGanttTask> tasks)
            {
                var colon = statement.IndexOf(':');
                if (colon <= 0 || gantt.Tasks.Count >= 200)
                    return false;

                var name = statement[..colon].Trim();
                if (name.Length == 0)
                    return false;

                var task = new MermaidGanttTask() { Name = name };
                DateTime? start = null;
                DateTime? end = null;
                int? duration = null;
                var tokens = statement[(colon + 1)..].Split(',');
                for (var i = 0; i < tokens.Length; i++)
                {
                    var token = tokens[i].Trim();
                    if (token.Length == 0)
                        return false;

                    if (token.Equals("done", StringComparison.OrdinalIgnoreCase))
                    {
                        task.Status = MermaidGanttStatus.Done;
                        continue;
                    }

                    if (token.Equals("active", StringComparison.OrdinalIgnoreCase))
                    {
                        task.Status = MermaidGanttStatus.Active;
                        continue;
                    }

                    if (token.Equals("crit", StringComparison.OrdinalIgnoreCase))
                    {
                        task.Status = MermaidGanttStatus.Crit;
                        continue;
                    }

                    if (token.Equals("milestone", StringComparison.OrdinalIgnoreCase))
                    {
                        task.Milestone = true;
                        continue;
                    }

                    if (token.StartsWith("after ", StringComparison.OrdinalIgnoreCase))
                    {
                        if (start != null)
                            return false;

                        var id = token[6..].Trim();
                        if (!tasks.TryGetValue(id, out var dependency))
                            return false;

                        start = dependency.End;
                        continue;
                    }

                    if (TryParseDate(token, out var date))
                    {
                        if (start == null)
                            start = date;
                        else if (end == null)
                            end = date;
                        else
                            return false;

                        continue;
                    }

                    if (TryParseDuration(token, out var days))
                    {
                        if (duration != null)
                            return false;

                        duration = days;
                        continue;
                    }

                    if (task.Id == null && start == null && end == null && duration == null)
                    {
                        task.Id = token;
                        continue;
                    }

                    return false;
                }

                if (start == null)
                    return false;

                if (task.Milestone)
                {
                    if ((end != null && end.Value != start.Value) || (duration != null && duration.Value != 0))
                        return false;

                    task.Start = start.Value;
                    task.End = start.Value;
                }
                else if (end != null)
                {
                    if (duration != null || end.Value < start.Value)
                        return false;

                    task.Start = start.Value;
                    task.End = end.Value;
                }
                else if (duration != null)
                {
                    task.Start = start.Value;
                    task.End = start.Value.AddDays(duration.Value);
                }
                else
                {
                    return false;
                }

                if (task.Id != null && !tasks.TryAdd(task.Id, task))
                    return false;

                if (current == null)
                {
                    current = new MermaidGanttSection();
                    gantt.Sections.Add(current);
                }

                task.Section = current;
                current.Tasks.Add(task);
                gantt.Tasks.Add(task);
                return true;
            }

            private static bool TryParseDate(string token, out DateTime date)
            {
                return DateTime.TryParseExact(token, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
            }

            private static bool TryParseDuration(string token, out int days)
            {
                days = 0;
                if (token.Length < 2)
                    return false;

                var suffix = char.ToLowerInvariant(token[^1]);
                if (suffix != 'd' && suffix != 'w')
                    return false;

                if (!int.TryParse(token[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > 3650)
                    return false;

                days = suffix == 'w' ? value * 7 : value;
                return true;
            }

            private readonly string _code;
        }
    }
}
