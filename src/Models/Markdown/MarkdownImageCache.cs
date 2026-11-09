using System;
using System.Collections.Generic;
using System.IO;

using Avalonia.Media.Imaging;

namespace SourceGit.Models
{
    /// <summary>
    ///     Small cache for images referenced by local markdown documents. Remote images are never
    ///     downloaded; SVG is not decoded because it would require an additional rasterizer.
    /// </summary>
    public static class MarkdownImageCache
    {
        public static Bitmap Get(string path)
        {
            if (string.IsNullOrEmpty(path) || path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 || info.Length > MaxFileSize)
                    return null;

                if (s_cache.TryGetValue(path, out var cached) &&
                    cached.LastWriteTimeUtc == info.LastWriteTimeUtc &&
                    cached.Length == info.Length)
                    return cached.Bitmap;

                using var stream = File.OpenRead(path);
                var bitmap = new Bitmap(stream);
                if (bitmap.PixelSize.Width <= 0 || bitmap.PixelSize.Height <= 0)
                    return null;

                Trim();
                s_cache[path] = new Entry()
                {
                    Bitmap = bitmap,
                    LastWriteTimeUtc = info.LastWriteTimeUtc,
                    Length = info.Length,
                };

                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private static void Trim()
        {
            if (s_cache.Count < MaxEntries)
                return;

            // Do not dispose bitmaps that may still be attached to a rendered view; dropping the
            // references is enough and the GC reclaims them once the preview is rebuilt.
            s_cache.Clear();
        }

        private class Entry
        {
            public Bitmap Bitmap { get; set; }
            public DateTime LastWriteTimeUtc { get; set; }
            public long Length { get; set; }
        }

        private static readonly Dictionary<string, Entry> s_cache = new(StringComparer.OrdinalIgnoreCase);
        private const long MaxFileSize = 32 * 1024 * 1024;
        private const int MaxEntries = 64;
    }
}
