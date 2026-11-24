using System.Collections.Generic;

namespace SourceGit.Models
{
    /// <summary>Shared line handling for the mermaid parsers.</summary>
    internal static class MermaidSource
    {
        public static string Normalize(string code)
        {
            return code.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        /// <summary>Splits the diagram body into trimmed, comment-free, non-empty statements.</summary>
        public static List<string> Statements(string code)
        {
            var lines = Normalize(code).Split('\n');
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

            return statements;
        }

        /// <summary>
        ///     The leading keyword of a statement, lower-cased. Returns an empty string when the
        ///     leading word runs into another character, so an id such as "state1" or "loop1" is never
        ///     mistaken for the statement keyword.
        /// </summary>
        public static string Keyword(string statement)
        {
            var end = 0;
            while (end < statement.Length && char.IsLetter(statement[end]))
                end++;

            if (end == 0 || (end < statement.Length && !char.IsWhiteSpace(statement[end])))
                return string.Empty;

            return statement[..end].ToLowerInvariant();
        }

        public static string StripComment(string line)
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

        public static int SkipFrontmatter(string[] lines)
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
    }
}
