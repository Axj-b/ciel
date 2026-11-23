using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Avalonia.Media;

namespace SourceGit.Models
{
    public enum MermaidSequenceArrow
    {
        Line,
        DottedLine,
        Arrow,
        DottedArrow,
        Cross,
        DottedCross,
        Open,
        DottedOpen,
        BiArrow,
        BiDottedArrow,
    }

    public enum MermaidNotePosition
    {
        Left,
        Right,
        Over,
    }

    public enum MermaidFragmentKind
    {
        Loop,
        Alt,
        Opt,
        Par,
        Critical,
        Break,
        Rect,
    }

    public class MermaidSequenceParticipant
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public bool IsActor { get; set; }
        public MermaidSequenceBox Box { get; set; }
    }

    public class MermaidSequenceBox
    {
        public string Title { get; set; }
        public Color? Color { get; set; }
        public List<MermaidSequenceParticipant> Members { get; } = new();
    }

    public abstract class MermaidSequenceItem
    {
    }

    public class MermaidSequenceMessage : MermaidSequenceItem
    {
        public MermaidSequenceParticipant From { get; set; }
        public MermaidSequenceParticipant To { get; set; }
        public string Label { get; set; }
        public MermaidSequenceArrow Arrow { get; set; }
        public bool Activate { get; set; }
        public bool Deactivate { get; set; }
    }

    public class MermaidSequenceNote : MermaidSequenceItem
    {
        public MermaidNotePosition Position { get; set; }
        public List<MermaidSequenceParticipant> Participants { get; } = new();
        public string Text { get; set; }
    }

    public class MermaidSequenceFragmentStart : MermaidSequenceItem
    {
        public MermaidFragmentKind Kind { get; set; }
        public string Text { get; set; }
        public Color? Color { get; set; }
    }

    public class MermaidSequenceFragmentDivider : MermaidSequenceItem
    {
        public string Keyword { get; set; }
        public string Text { get; set; }
    }

    public class MermaidSequenceFragmentEnd : MermaidSequenceItem
    {
    }

    public class MermaidSequenceActivation : MermaidSequenceItem
    {
        public MermaidSequenceParticipant Participant { get; set; }
        public bool Activate { get; set; }
    }

    public class MermaidSequenceDiagram
    {
        public bool Autonumber { get; set; }
        public List<MermaidSequenceParticipant> Participants { get; } = new();
        public Dictionary<string, MermaidSequenceParticipant> ParticipantMap { get; } = new(StringComparer.Ordinal);
        public List<MermaidSequenceItem> Items { get; } = new();
        public List<MermaidSequenceBox> Boxes { get; } = new();

        public MermaidSequenceParticipant GetOrCreateParticipant(string id, MermaidSequenceBox box)
        {
            if (ParticipantMap.TryGetValue(id, out var existing))
                return existing;

            var participant = new MermaidSequenceParticipant() { Id = id, Label = id, Box = box };
            ParticipantMap[id] = participant;
            Participants.Add(participant);
            box?.Members.Add(participant);
            return participant;
        }
    }

    /// <summary>
    ///     Parser for the mermaid sequenceDiagram syntax. Anything that cannot be fully understood
    ///     returns null so the caller falls back to a plain code block instead of a wrong diagram.
    /// </summary>
    public static class MermaidSequence
    {
        private static readonly Regex MessageRegex = new(
            @"^(.+?)(<<-->>|<<->>|-->>|->>|--x|-x|--\)|-\)|-->|->)([+-]?)([^:]*?)(?::(.*))?$",
            RegexOptions.Singleline);

        private static readonly Regex NoteRegex = new(
            @"^note\s+(left of|right of|over)\s+([^:]+):(.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        public static MermaidSequenceDiagram Parse(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > 64 * 1024)
                return null;

            try
            {
                return new Parser(code.Replace("\r\n", "\n").Replace('\r', '\n')).Run();
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

            public MermaidSequenceDiagram Run()
            {
                var lines = _code.Split('\n');
                var start = SkipFrontmatter(lines);
                if (start < 0)
                    return null;

                var statements = new List<string>();
                for (var i = start; i < lines.Length; i++)
                {
                    var line = StripComment(lines[i]).Trim();
                    if (line.Length > 0)
                        statements.Add(line);
                }

                if (statements.Count == 0 || !statements[0].Equals("sequenceDiagram", StringComparison.OrdinalIgnoreCase))
                    return null;

                var diagram = new MermaidSequenceDiagram();
                var scope = new Stack<(string Kind, MermaidSequenceBox Box)>();
                for (var i = 1; i < statements.Count; i++)
                {
                    if (!ParseStatement(diagram, statements[i], scope))
                        return null;
                }

                if (scope.Count > 0 || diagram.Participants.Count == 0 || diagram.Participants.Count > 60 || diagram.Items.Count > 800)
                    return null;

                return diagram;
            }

            private bool ParseStatement(MermaidSequenceDiagram diagram, string statement, Stack<(string Kind, MermaidSequenceBox Box)> scope)
            {
                var keyword = Regex.Match(statement, @"^[A-Za-z]+").Value.ToLowerInvariant();
                var box = scope.Count > 0 ? scope.Peek().Box : null;

                switch (keyword)
                {
                    case "participant":
                    case "actor":
                        return ParseParticipant(diagram, statement, keyword == "actor", box);
                    case "activate":
                    case "deactivate":
                    {
                        var id = statement[keyword.Length..].Trim();
                        if (!diagram.ParticipantMap.TryGetValue(id, out var participant))
                            return false;

                        diagram.Items.Add(new MermaidSequenceActivation() { Participant = participant, Activate = keyword == "activate" });
                        return true;
                    }
                    case "note":
                        return ParseNote(diagram, statement);
                    case "loop":
                    case "alt":
                    case "opt":
                    case "par":
                    case "critical":
                    case "break":
                        scope.Push((keyword, null));
                        diagram.Items.Add(new MermaidSequenceFragmentStart()
                        {
                            Kind = Enum.Parse<MermaidFragmentKind>(keyword, true),
                            Text = statement[keyword.Length..].Trim(),
                        });
                        return true;
                    case "rect":
                        scope.Push(("rect", null));
                        diagram.Items.Add(new MermaidSequenceFragmentStart()
                        {
                            Kind = MermaidFragmentKind.Rect,
                            Color = ParseColor(statement[4..].Trim()),
                        });
                        return true;
                    case "else":
                    case "and":
                    case "option":
                        if (scope.Count == 0 || scope.Peek().Kind != ExpectedFragment(keyword))
                            return false;

                        diagram.Items.Add(new MermaidSequenceFragmentDivider() { Keyword = keyword, Text = statement[keyword.Length..].Trim() });
                        return true;
                    case "end":
                        if (scope.Count == 0)
                            return false;

                        var closed = scope.Pop();
                        if (closed.Kind != "box")
                            diagram.Items.Add(new MermaidSequenceFragmentEnd());
                        return true;
                    case "box":
                        return ParseBox(diagram, statement[3..].Trim(), scope);
                    case "autonumber":
                        diagram.Autonumber = true;
                        return true;
                    case "acctitle":
                    case "accdescr":
                        return true;
                }

                return ParseMessage(diagram, statement, box);
            }

            private static string ExpectedFragment(string divider) => divider switch
            {
                "else" => "alt",
                "and" => "par",
                _ => "critical",
            };

            private bool ParseParticipant(MermaidSequenceDiagram diagram, string statement, bool actor, MermaidSequenceBox box)
            {
                var rest = statement[(actor ? 5 : 11)..].Trim();
                if (rest.Length == 0)
                    return false;

                string id, label;
                var alias = Regex.Match(rest, @"^(.+?)\s+as\s+(.+)$", RegexOptions.IgnoreCase);
                if (alias.Success)
                {
                    id = alias.Groups[1].Value.Trim();
                    label = alias.Groups[2].Value.Trim();
                }
                else
                {
                    id = label = rest;
                }

                if (!IsId(id))
                    return false;

                var participant = diagram.GetOrCreateParticipant(id, box);
                participant.Label = label;
                participant.IsActor = actor;
                return true;
            }

            private bool ParseBox(MermaidSequenceDiagram diagram, string rest, Stack<(string Kind, MermaidSequenceBox Box)> scope)
            {
                Color? color = null;
                var title = rest;

                var rgb = Regex.Match(rest, @"^rgba?\([^)]*\)", RegexOptions.IgnoreCase);
                if (rgb.Success)
                {
                    color = ParseColor(rgb.Value);
                    title = rest[rgb.Length..].Trim();
                }
                else
                {
                    var space = rest.IndexOf(' ');
                    var first = space > 0 ? rest[..space] : rest;
                    var parsed = ParseColor(first);
                    if (parsed.HasValue)
                    {
                        color = parsed;
                        title = space > 0 ? rest[(space + 1)..].Trim() : string.Empty;
                    }
                }

                var box = new MermaidSequenceBox() { Title = title, Color = color };
                diagram.Boxes.Add(box);
                scope.Push(("box", box));
                return true;
            }

            private bool ParseNote(MermaidSequenceDiagram diagram, string statement)
            {
                var match = NoteRegex.Match(statement);
                if (!match.Success)
                    return false;

                var note = new MermaidSequenceNote()
                {
                    Position = match.Groups[1].Value.ToLowerInvariant() switch
                    {
                        "left of" => MermaidNotePosition.Left,
                        "right of" => MermaidNotePosition.Right,
                        _ => MermaidNotePosition.Over,
                    },
                    Text = match.Groups[3].Value.Trim(),
                };

                foreach (var id in match.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!IsId(id))
                        return false;

                    note.Participants.Add(diagram.GetOrCreateParticipant(id, null));
                }

                if (note.Participants.Count == 0 || note.Participants.Count > 2)
                    return false;

                diagram.Items.Add(note);
                return true;
            }

            private bool ParseMessage(MermaidSequenceDiagram diagram, string statement, MermaidSequenceBox box)
            {
                var match = MessageRegex.Match(statement);
                if (!match.Success)
                    return false;

                var fromId = match.Groups[1].Value.Trim();
                var toId = match.Groups[4].Value.Trim();
                if (!IsId(fromId) || !IsId(toId))
                    return false;

                var message = new MermaidSequenceMessage()
                {
                    From = diagram.GetOrCreateParticipant(fromId, box),
                    To = diagram.GetOrCreateParticipant(toId, box),
                    Label = match.Groups[5].Success ? match.Groups[5].Value.Trim() : string.Empty,
                    Arrow = match.Groups[2].Value switch
                    {
                        "->" => MermaidSequenceArrow.Line,
                        "-->" => MermaidSequenceArrow.DottedLine,
                        "->>" => MermaidSequenceArrow.Arrow,
                        "-->>" => MermaidSequenceArrow.DottedArrow,
                        "-x" => MermaidSequenceArrow.Cross,
                        "--x" => MermaidSequenceArrow.DottedCross,
                        "-)" => MermaidSequenceArrow.Open,
                        "--)" => MermaidSequenceArrow.DottedOpen,
                        "<<->>" => MermaidSequenceArrow.BiArrow,
                        _ => MermaidSequenceArrow.BiDottedArrow,
                    },
                    Activate = match.Groups[3].Value == "+",
                    Deactivate = match.Groups[3].Value == "-",
                };

                diagram.Items.Add(message);
                return true;
            }

            private static bool IsId(string id)
            {
                if (id.Length == 0)
                    return false;

                foreach (var c in id)
                {
                    if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                        return false;
                }

                return true;
            }

            private static Color? ParseColor(string text)
            {
                if (text.Length == 0)
                    return null;

                if (text.Equals("transparent", StringComparison.OrdinalIgnoreCase))
                    return Colors.Transparent;

                var rgb = Regex.Match(text, @"^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+)\s*)?\)$", RegexOptions.IgnoreCase);
                if (rgb.Success)
                {
                    var alpha = rgb.Groups[4].Success ? (byte)(Math.Clamp(double.Parse(rgb.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture), 0, 1) * 255) : (byte)255;
                    return Color.FromArgb(
                        alpha,
                        byte.Parse(rgb.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                        byte.Parse(rgb.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                        byte.Parse(rgb.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture));
                }

                return Color.TryParse(text, out var color) ? color : null;
            }

            private static string StripComment(string line)
            {
                var inQuote = false;
                for (var i = 0; i < line.Length; i++)
                {
                    var c = line[i];
                    if (c == '"')
                        inQuote = !inQuote;
                    else if (!inQuote && c == '%' && i + 1 < line.Length && line[i + 1] == '%')
                        return line[..i];
                }

                return line;
            }

            private static int SkipFrontmatter(string[] lines)
            {
                var first = -1;
                for (var i = 0; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                        continue;

                    first = i;
                    break;
                }

                if (first < 0)
                    return -1;

                if (lines[first].Trim() != "---")
                    return first;

                for (var i = first + 1; i < lines.Length; i++)
                {
                    if (lines[i].Trim() == "---")
                        return i + 1 < lines.Length ? i + 1 : -1;
                }

                return -1;
            }

            private readonly string _code;
        }
    }
}
