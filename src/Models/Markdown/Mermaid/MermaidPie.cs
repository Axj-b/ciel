using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SourceGit.Models
{
    public class MermaidPieSlice
    {
        public string Label { get; set; }
        public double Value { get; set; }
    }

    public class MermaidPieChart
    {
        public string Title { get; set; }
        public bool ShowData { get; set; }
        public List<MermaidPieSlice> Slices { get; } = new();
    }

    /// <summary>
    ///     Strict parser for mermaid pie charts. Anything outside the supported subset (other
    ///     directives, malformed rows, negative values, an empty chart) returns null so the
    ///     markdown renderer falls back to a plain code block.
    /// </summary>
    public static class MermaidPie
    {
        private static readonly Regex HeaderRegex = new(
            @"^pie(?:\s+(?<rest>.*))?$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex SliceRegex = new(
            @"^(?:""(?<quoted>[^""]*)""|(?<plain>[^:]+?))\s*:\s*(?<value>-?\d+(?:\.\d+)?)$",
            RegexOptions.Singleline);

        public static MermaidPieChart Parse(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > 64 * 1024)
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

            public MermaidPieChart Run()
            {
                var statements = MermaidSource.Statements(_code);
                // A header plus at most 20 slices: beyond that the outside labels overlap.
                if (statements == null || statements.Count == 0 || statements.Count > 21)
                    return null;

                var header = HeaderRegex.Match(statements[0]);
                if (!header.Success)
                    return null;

                var pie = new MermaidPieChart();
                if (!ParseHeader(pie, header.Groups["rest"].Value))
                    return null;

                var total = 0.0;
                for (var i = 1; i < statements.Count; i++)
                {
                    var match = SliceRegex.Match(statements[i]);
                    if (!match.Success)
                        return null;

                    var label = match.Groups["quoted"].Success
                        ? match.Groups["quoted"].Value
                        : match.Groups["plain"].Value.Trim();
                    if (label.Length == 0)
                        return null;

                    if (!double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                        return null;

                    if (value < 0)
                        return null;

                    pie.Slices.Add(new MermaidPieSlice() { Label = label, Value = value });
                    total += value;
                }

                if (pie.Slices.Count == 0 || total <= 0)
                    return null;

                return pie;
            }

            private static bool ParseHeader(MermaidPieChart pie, string rest)
            {
                rest = rest.Trim();
                if (rest.Length == 0)
                    return true;

                if (rest.StartsWith("showData", StringComparison.OrdinalIgnoreCase) &&
                    (rest.Length == 8 || char.IsWhiteSpace(rest[8])))
                {
                    pie.ShowData = true;
                    rest = rest[8..].Trim();
                }

                if (rest.Length == 0)
                    return true;

                if (!rest.StartsWith("title", StringComparison.OrdinalIgnoreCase) ||
                    (rest.Length > 5 && !char.IsWhiteSpace(rest[5])))
                    return false;

                pie.Title = rest[5..].Trim();
                return pie.Title.Length > 0;
            }

            private readonly string _code;
        }
    }
}
