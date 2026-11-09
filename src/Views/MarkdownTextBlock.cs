using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace SourceGit.Views
{
    /// <summary>
    ///     A selectable text block that remembers which character ranges are links. Markdown text is
    ///     rendered as one block per paragraph, so links can be hit tested against the text layout
    ///     without extra visual elements in the inline flow.
    /// </summary>
    public class MarkdownTextBlock : SelectableTextBlock
    {
        public event Action<Models.MarkdownLink> LinkActivated;

        public int InlineLength { get; private set; }

        protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

        public MarkdownTextBlock()
        {
            // The inner text presenter handles pointer input for selection, so links have to be
            // intercepted while the event travels down instead of waiting for it to bubble up.
            AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
            AddHandler(PointerMovedEvent, OnPreviewPointerMoved, RoutingStrategies.Tunnel);

            // Text blocks are otherwise transparent to hit testing, which would let clicks fall
            // through to the scroll viewer instead of activating the link below the pointer.
            Background = Brushes.Transparent;
        }

        public void AddLink(int start, int length, Models.MarkdownLink link)
        {
            if (length <= 0 || link == null)
                return;

            _links.Add(new LinkRange(start, length, link));
        }

        public void Append(Run run)
        {
            Inlines!.Add(run);
            InlineLength += run.Text?.Length ?? 0;
        }

        public void AppendLineBreak()
        {
            Inlines!.Add(new LineBreak());
            InlineLength++;
        }

        /// <summary>True when a link occupies the given position inside this block.</summary>
        public bool IsLinkAt(Point position)
        {
            return HitTestLink(position) != null;
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _hovered = null;
            Cursor = Cursor.Default;
            ToolTip.SetTip(this, null);
        }

        private void OnPreviewPointerPressed(object sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed)
                return;

            // Alt+Click is reserved for editing the block source in the inline preview.
            if ((e.KeyModifiers & KeyModifiers.Alt) != 0)
                return;

            var link = HitTestLink(e.GetPosition(this));
            if (link == null)
                return;

            e.Handled = true;
            LinkActivated?.Invoke(link);
        }

        private void OnPreviewPointerMoved(object sender, PointerEventArgs e)
        {
            var link = HitTestLink(e.GetPosition(this));
            if (ReferenceEquals(link, _hovered))
                return;

            _hovered = link;
            if (link == null)
            {
                Cursor = Cursor.Default;
                ToolTip.SetTip(this, null);
            }
            else
            {
                Cursor = new Cursor(StandardCursorType.Hand);
                ToolTip.SetTip(this, link.DisplayUrl);
            }
        }

        private Models.MarkdownLink HitTestLink(Point position)
        {
            var layout = TextLayout;
            if (layout == null || _links.Count == 0)
                return null;

            var point = position - new Point(Padding.Left, Padding.Top);
            var x = Math.Min(Math.Max(point.X, 0), Math.Max(layout.WidthIncludingTrailingWhitespace, 0));
            var y = Math.Min(Math.Max(point.Y, 0), Math.Max(layout.Height, 0));
            var pos = layout.HitTestPoint(new Point(x, y)).TextPosition;

            foreach (var range in _links)
            {
                if (pos >= range.Start && pos < range.Start + range.Length)
                    return range.Link;
            }

            return null;
        }

        private record LinkRange(int Start, int Length, Models.MarkdownLink Link);

        private readonly List<LinkRange> _links = [];
        private Models.MarkdownLink _hovered = null;
    }
}
