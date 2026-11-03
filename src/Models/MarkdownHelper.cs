using System;
using System.IO;
using System.Text;

namespace SourceGit.Models
{
    public enum MarkdownLinkKind
    {
        External,
        File,
        Anchor,
        Blocked,
    }

    /// <summary>
    ///     A link extracted from a rendered markdown document. Relative targets are already resolved
    ///     against the repository root so the views only have to execute the action.
    /// </summary>
    public class MarkdownLink
    {
        public MarkdownLinkKind Kind { get; set; } = MarkdownLinkKind.Blocked;
        public string Url { get; set; } = string.Empty;
        public string ExternalUrl { get; set; } = string.Empty;
        public string Anchor { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;

        public string DisplayUrl => Kind == MarkdownLinkKind.Anchor ? "#" + Anchor : Url;
    }

    public static class MarkdownHelper
    {
        public static bool IsMarkdownFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            return Path.GetExtension(path).ToLowerInvariant() is ".md" or ".markdown" or ".mdown" or ".mkd";
        }

        /// <summary>GitHub style heading anchor used by in-document links.</summary>
        public static string Slugify(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var builder = new StringBuilder(text.Length);
            var pendingDash = false;

            foreach (var c in text.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    if (pendingDash && builder.Length > 0)
                        builder.Append('-');

                    pendingDash = false;
                    builder.Append(c);
                }
                else if (c == '-' || char.IsWhiteSpace(c))
                {
                    pendingDash = builder.Length > 0;
                }
            }

            return builder.ToString();
        }

        /// <summary>
        ///     Resolves a markdown link target. The result never points outside the repository; unsafe
        ///     schemes such as javascript:, data: and file: are rejected.
        /// </summary>
        public static MarkdownLink Classify(string url, string repositoryPath, string markdownFileName)
        {
            var link = new MarkdownLink { Url = url ?? string.Empty };
            if (string.IsNullOrWhiteSpace(url))
                return link;

            url = url.Trim();

            if (url.StartsWith("//", StringComparison.Ordinal))
            {
                link.Kind = MarkdownLinkKind.External;
                link.ExternalUrl = "https:" + url;
                return link;
            }

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Scheme))
            {
                switch (uri.Scheme.ToLowerInvariant())
                {
                    case "http":
                    case "https":
                    case "mailto":
                        link.Kind = MarkdownLinkKind.External;
                        link.ExternalUrl = url;
                        return link;
                    default:
                        // javascript:, data:, file:, vbscript: and anything unknown is not executable
                        // from a repository document.
                        link.Kind = MarkdownLinkKind.Blocked;
                        return link;
                }
            }

            var target = url;
            var anchorIndex = target.IndexOf('#');
            if (anchorIndex >= 0)
            {
                link.Anchor = Uri.UnescapeDataString(target[(anchorIndex + 1)..]);
                target = target[..anchorIndex];
            }

            var queryIndex = target.IndexOf('?');
            if (queryIndex >= 0)
                target = target[..queryIndex];

            if (string.IsNullOrWhiteSpace(target))
            {
                link.Kind = link.Anchor.Length > 0 ? MarkdownLinkKind.Anchor : MarkdownLinkKind.Blocked;
                return link;
            }

            target = Uri.UnescapeDataString(target.Replace('\\', '/'));
            var root = string.IsNullOrEmpty(repositoryPath) ? string.Empty : Path.GetFullPath(repositoryPath);

            string full;
            try
            {
                if (target.StartsWith('/'))
                {
                    if (root.Length == 0)
                        return link;

                    full = Path.GetFullPath(Path.Combine(root, target.TrimStart('/')));
                }
                else
                {
                    var baseDir = root;
                    var markdownDir = Path.GetDirectoryName((markdownFileName ?? string.Empty).Replace('/', Path.DirectorySeparatorChar));
                    if (root.Length > 0 && !string.IsNullOrEmpty(markdownDir))
                        baseDir = Path.GetFullPath(Path.Combine(root, markdownDir));

                    if (baseDir.Length == 0)
                        return link;

                    full = Path.GetFullPath(Path.Combine(baseDir, target.Replace('/', Path.DirectorySeparatorChar)));
                }
            }
            catch
            {
                return link;
            }

            if (root.Length > 0 && !IsUnder(root, full))
            {
                link.Kind = MarkdownLinkKind.Blocked;
                return link;
            }

            link.Kind = MarkdownLinkKind.File;
            link.FullPath = full;
            link.RelativePath = root.Length > 0
                ? Path.GetRelativePath(root, full).Replace('\\', '/')
                : full.Replace('\\', '/');
            return link;
        }

        private static bool IsUnder(string root, string path)
        {
            var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return path.StartsWith(normalizedRoot, comparison) ||
                   string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), comparison);
        }
    }
}
