namespace SourceGit.Models
{
    /// <summary>
    ///     Source range of a top level markdown block. Attached to the rendered control so the
    ///     preview can map a click back to the exact text that produced it.
    /// </summary>
    public sealed class MarkdownBlockSource
    {
        public MarkdownBlockSource(int start, int length)
        {
            Start = start;
            Length = length;
        }

        public int Start { get; }

        public int Length { get; }
    }
}
