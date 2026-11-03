using System;
using System.Collections.Generic;

using Avalonia.Media;

using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace SourceGit.Models
{
    public class MarkdownCodeSegment
    {
        public string Text { get; set; } = string.Empty;
        public IBrush Foreground { get; set; }
    }

    public class MarkdownCodeLine
    {
        public List<MarkdownCodeSegment> Segments { get; } = [];
    }

    /// <summary>
    ///     Tokenizes fenced code blocks with the same TextMate grammars and color scheme the editor
    ///     uses, so markdown previews and the source view stay visually consistent.
    /// </summary>
    public static class MarkdownCodeHighlighter
    {
        public static List<MarkdownCodeLine> Highlight(string code, string language, bool isDark)
        {
            var lines = new List<MarkdownCodeLine>();
            if (string.IsNullOrEmpty(code))
            {
                lines.Add(new MarkdownCodeLine());
                return lines;
            }

            var raw = code.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var grammar = LoadGrammar(language, isDark, out var theme);
            if (grammar == null || theme == null)
            {
                foreach (var line in raw)
                    lines.Add(CreatePlainLine(line));

                return lines;
            }

            IStateStack stack = null;
            foreach (var line in raw)
            {
                var result = grammar.TokenizeLine(new LineText(line), stack, TimeSpan.Zero);
                stack = result.RuleStack;

                var output = new MarkdownCodeLine();
                for (var i = 0; i < result.Tokens.Length; i++)
                {
                    var start = result.Tokens[i].StartIndex;
                    var end = i + 1 < result.Tokens.Length ? result.Tokens[i + 1].StartIndex : line.Length;
                    if (start < 0 || start >= end || start >= line.Length)
                        continue;

                    end = Math.Min(end, line.Length);
                    var text = line[start..end];
                    if (text.Length == 0)
                        continue;

                    var color = ResolveForeground(theme, result.Tokens[i].Scopes);
                    output.Segments.Add(new MarkdownCodeSegment() { Text = text, Foreground = color });
                }

                if (output.Segments.Count == 0)
                    output.Segments.Add(new MarkdownCodeSegment() { Text = line });

                lines.Add(output);
            }

            return lines;
        }

        private static MarkdownCodeLine CreatePlainLine(string text)
        {
            var line = new MarkdownCodeLine();
            line.Segments.Add(new MarkdownCodeSegment() { Text = text });
            return line;
        }

        private static IGrammar LoadGrammar(string language, bool isDark, out Theme theme)
        {
            EnsureRegistry(isDark);
            theme = s_theme;

            var extension = ToExtension(language);
            if (extension == null)
                return null;

            var scope = s_options.GetScope(extension);
            if (string.IsNullOrEmpty(scope))
                return null;

            try
            {
                return s_registry.LoadGrammar(scope);
            }
            catch
            {
                return null;
            }
        }

        private static IBrush ResolveForeground(Theme theme, IList<string> scopes)
        {
            var id = 0;
            foreach (var rule in theme.Match(scopes))
            {
                if (id == 0 && rule.foreground > 0)
                    id = rule.foreground;
            }

            if (id == 0)
                return null;

            var color = theme.GetColor(id);
            if (string.IsNullOrEmpty(color))
                return null;

            if (s_brushes.TryGetValue(color, out var cached))
                return cached;

            try
            {
                var brush = new SolidColorBrush(Color.Parse(NormalizeColor(color)));
                s_brushes.Add(color, brush);
                return brush;
            }
            catch
            {
                return null;
            }
        }

        private static void EnsureRegistry(bool isDark)
        {
            if (s_registry != null && s_isDark == isDark)
                return;

            s_isDark = isDark;
            s_options = new RegistryOptionsWrapper(isDark ? ThemeName.DarkPlus : ThemeName.LightPlus);
            s_registry = new Registry(s_options);
            s_theme = s_registry.GetTheme();
            s_brushes.Clear();
        }

        /// <summary>Maps common fenced code info strings to a file extension the registry knows.</summary>
        private static string ToExtension(string language)
        {
            if (string.IsNullOrWhiteSpace(language))
                return null;

            var value = language.Trim().ToLowerInvariant();
            var cut = value.IndexOfAny([' ', '\t', ',', ';', '{']);
            if (cut > 0)
                value = value[..cut];

            if (value.StartsWith("language-", StringComparison.Ordinal))
                value = value[9..];

            return value switch
            {
                "c#" or "csharp" => ".cs",
                "c++" or "cpp" => ".cpp",
                "objc" or "objective-c" => ".m",
                "js" or "javascript" or "jsx" or "node" => ".js",
                "ts" or "typescript" or "tsx" => ".ts",
                "py" or "python" => ".py",
                "rb" or "ruby" => ".rb",
                "rs" or "rust" => ".rs",
                "golang" => ".go",
                "sh" or "bash" or "shell" or "zsh" => ".sh",
                "ps1" or "powershell" or "pwsh" => ".ps1",
                "yml" or "yaml" => ".yaml",
                "md" or "markdown" => ".md",
                "html" or "htm" => ".html",
                "svg" => ".svg",
                "kt" or "kotlin" => ".kt",
                "dockerfile" => ".dockerfile",
                "makefile" or "make" => ".make",
                "text" or "plain" or "txt" or "mermaid" or "none" => null,
                _ => "." + value,
            };
        }

        /// <summary>TextMate colors use #RRGGBBAA, Avalonia expects #AARRGGBB.</summary>
        private static string NormalizeColor(string color)
        {
            if (color.Length == 9 && color[0] == '#')
                return $"#{color[7..9]}{color[1..7]}";

            return color;
        }

        private static readonly Dictionary<string, IBrush> s_brushes = new(StringComparer.Ordinal);
        private static RegistryOptionsWrapper s_options = null;
        private static Registry s_registry = null;
        private static Theme s_theme = null;
        private static bool s_isDark = false;
    }
}
