using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    public enum TextFileLoadStatus
    {
        Success,
        Binary,
        TooLarge,
        NotFound,
        Failed,
    }

    /// <summary>
    ///     Working tree text file with enough metadata to save it back without surprising the user:
    ///     encoding, BOM and line ending style are detected on load and preserved on save.
    /// </summary>
    public class EditableTextFile
    {
        /// <summary>Refuse to open anything bigger than this to keep the editor responsive.</summary>
        public const long MaxSize = 8 * 1024 * 1024;

        static EditableTextFile()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public TextFileLoadStatus Status { get; private set; } = TextFileLoadStatus.Success;
        public string Error { get; private set; } = string.Empty;
        public string FullPath { get; private set; } = string.Empty;
        public string RelativePath { get; private set; } = string.Empty;
        public string Text { get; private set; } = string.Empty;
        public string LineEnding { get; private set; } = Environment.NewLine;
        public Encoding Encoding { get; private set; } = new UTF8Encoding(false);
        public bool HasBom { get; private set; } = false;
        public bool IsReadOnly { get; private set; } = false;
        public long Size { get; private set; } = 0;

        public string MetaText
        {
            get
            {
                var eb = Encoding.WebName.ToUpperInvariant();
                var bom = HasBom ? " BOM" : string.Empty;
                var eol = LineEnding switch
                {
                    "\r\n" => "CRLF",
                    "\n" => "LF",
                    _ => "CR",
                };

                return $"{eb}{bom} · {eol} · {FormatSize(Size)}";
            }
        }

        public static async Task<EditableTextFile> LoadAsync(string repository, string relativePath)
        {
            var file = new EditableTextFile
            {
                RelativePath = relativePath,
                FullPath = Path.Combine(repository, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            };

            try
            {
                var info = new FileInfo(file.FullPath);
                if (!info.Exists)
                {
                    file.Status = TextFileLoadStatus.NotFound;
                    file.Error = "File does not exist.";
                    return file;
                }

                if (info.Length > MaxSize)
                {
                    file.Status = TextFileLoadStatus.TooLarge;
                    file.Size = info.Length;
                    return file;
                }

                file.IsReadOnly = (info.Attributes & FileAttributes.ReadOnly) != 0;

                var bytes = await File.ReadAllBytesAsync(file.FullPath).ConfigureAwait(false);
                if (!TryDecode(bytes, out var text, out var encoding, out var hasBom, out var bom))
                {
                    file.Status = TextFileLoadStatus.Binary;
                    file.Size = info.Length;
                    return file;
                }

                file.Text = text;
                file.Encoding = encoding;
                file.HasBom = hasBom;
                file._bom = bom;
                file.LineEnding = DetectLineEnding(text);
                file.Size = info.Length;
                file._lastWriteTimeUtc = info.LastWriteTimeUtc;
            }
            catch (Exception e)
            {
                file.Status = TextFileLoadStatus.Failed;
                file.Error = e.Message;
            }

            return file;
        }

        /// <summary>True when the file was modified by something else since it was loaded or saved.</summary>
        public bool HasChangedOnDisk()
        {
            try
            {
                var info = new FileInfo(FullPath);
                if (!info.Exists)
                    return true;

                return info.Length != Size || info.LastWriteTimeUtc != _lastWriteTimeUtc;
            }
            catch
            {
                return true;
            }
        }

        public async Task SaveAsync(string text)
        {
            if (IsReadOnly)
                throw new InvalidOperationException("File is read-only.");

            var normalized = NormalizeLineEndings(text, LineEnding);
            var body = Encoding.GetBytes(normalized);
            var bom = _bom;
            var attributes = FileAttributes.Normal;
            var exists = File.Exists(FullPath);

            if (exists)
            {
                attributes = File.GetAttributes(FullPath);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    throw new InvalidOperationException("File is read-only.");
            }

            var tmp = FullPath + ".ciel-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
                {
                    if (bom.Length > 0)
                        await stream.WriteAsync(bom).ConfigureAwait(false);

                    await stream.WriteAsync(body).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }

                if (exists)
                    File.SetAttributes(tmp, attributes);

                File.Move(tmp, FullPath, true);
            }
            finally
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }

            Text = normalized;
            RefreshBaseline();
        }

        private void RefreshBaseline()
        {
            var info = new FileInfo(FullPath);
            Size = info.Exists ? info.Length : 0;
            _lastWriteTimeUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
            IsReadOnly = info.Exists && (info.Attributes & FileAttributes.ReadOnly) != 0;
        }

        private static bool TryDecode(byte[] bytes, out string text, out Encoding encoding, out bool hasBom, out byte[] bom)
        {
            text = string.Empty;
            encoding = new UTF8Encoding(false);
            bom = Array.Empty<byte>();
            hasBom = false;

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                encoding = new UTF8Encoding(false);
                hasBom = true;
                bom = [0xEF, 0xBB, 0xBF];
                text = encoding.GetString(bytes, 3, bytes.Length - 3);
                return true;
            }

            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
            {
                encoding = new UTF32Encoding(false, true);
                hasBom = true;
                bom = [0xFF, 0xFE, 0x00, 0x00];
                text = encoding.GetString(bytes, 4, bytes.Length - 4);
                return true;
            }

            if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
            {
                encoding = new UTF32Encoding(true, true);
                hasBom = true;
                bom = [0x00, 0x00, 0xFE, 0xFF];
                text = encoding.GetString(bytes, 4, bytes.Length - 4);
                return true;
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                encoding = new UnicodeEncoding(false, true);
                hasBom = true;
                bom = [0xFF, 0xFE];
                text = encoding.GetString(bytes, 2, bytes.Length - 2);
                return true;
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                encoding = new UnicodeEncoding(true, true);
                hasBom = true;
                bom = [0xFE, 0xFF];
                text = encoding.GetString(bytes, 2, bytes.Length - 2);
                return true;
            }

            // No BOM: an embedded NUL is the classic sign of a binary file (UTF-16 without
            // BOM is deliberately treated as binary, since guessing its endianness is unsafe).
            if (Array.IndexOf(bytes, (byte)0) >= 0)
                return false;

            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes);
                encoding = new UTF8Encoding(false);
                return true;
            }
            catch (DecoderFallbackException)
            {
                // Not valid UTF-8: fall through to a legacy single byte encoding.
            }

            var ansi = GetAnsiEncoding();
            if (ansi != null)
            {
                var decoded = ansi.GetString(bytes);
                if (ansi.GetBytes(decoded).AsSpan().SequenceEqual(bytes))
                {
                    text = decoded;
                    encoding = ansi;
                    return true;
                }
            }

            // Latin-1 maps every byte to a character and back, so it is guaranteed to round-trip.
            text = Encoding.Latin1.GetString(bytes);
            encoding = Encoding.Latin1;
            return true;
        }

        private static Encoding GetAnsiEncoding()
        {
            try
            {
                var codePage = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
                if (codePage <= 0 || codePage == 65001)
                    return null;

                return Encoding.GetEncoding(codePage);
            }
            catch
            {
                return null;
            }
        }

        private static string DetectLineEnding(string text)
        {
            var crlf = 0;
            var lf = 0;
            var cr = 0;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        crlf++;
                        i++;
                    }
                    else
                    {
                        cr++;
                    }
                }
                else if (c == '\n')
                {
                    lf++;
                }
            }

            if (crlf == 0 && lf == 0 && cr == 0)
                return Environment.NewLine;

            if (crlf >= lf && crlf >= cr)
                return "\r\n";

            return lf >= cr ? "\n" : "\r";
        }

        public static string NormalizeLineEndings(string text, string newLine)
        {
            var builder = new StringBuilder(text.Length + 64);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                        i++;

                    builder.Append(newLine);
                }
                else if (c == '\n')
                {
                    builder.Append(newLine);
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        private static string FormatSize(long size)
        {
            if (size < 1024)
                return $"{size} B";

            if (size < 1024 * 1024)
                return $"{size / 1024.0:0.#} KB";

            return $"{size / (1024.0 * 1024.0):0.#} MB";
        }

        private DateTime _lastWriteTimeUtc = DateTime.MinValue;
        private byte[] _bom = Array.Empty<byte>();
    }
}
