using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace NotepadApp;

internal sealed class NotePreviewRenderer : IDisposable
{
    private const uint FormatRangeMessage = 0x0439;
    private const int TwipsPerInch = 1440;
    private const int MaxCachedNotes = 24;
    private readonly Dictionary<Guid, CacheEntry> cache = [];
    private RichTextBox? editor;

    public bool Draw(Graphics target, Rectangle bounds, Note note, string query, Font font, NotesTheme theme, Color paper)
    {
        if (string.IsNullOrEmpty(note.RichText) || bounds.Width <= 0 || bounds.Height <= 0) return false;

        var key = new PreviewKey(note.Content, note.RichText, query, bounds.Size, target.DpiX, target.DpiY, font, theme.Ink, theme.Highlight, paper);
        if (!cache.TryGetValue(note.Id, out var entry) || entry.Key != key)
        {
            if (entry is not null)
            {
                cache.Remove(note.Id);
                entry.Image?.Dispose();
            }
            else if (cache.Count >= MaxCachedNotes) ClearCache();

            entry = new CacheEntry(key, Render(note, key));
            cache[note.Id] = entry;
        }

        if (entry.Image is null) return false;
        target.DrawImageUnscaled(entry.Image, bounds.Location);
        return true;
    }

    private Bitmap? Render(Note note, PreviewKey key)
    {
        editor ??= new RichTextBox { BorderStyle = BorderStyle.None, DetectUrls = false };
        editor.Font = key.Font;
        editor.ForeColor = key.Ink;
        editor.BackColor = key.Paper;

        int widthTwips = ToTwips(key.Size.Width, key.DpiX);
        int heightTwips = ToTwips(key.Size.Height, key.DpiY);
        string rtf = NoteImage.FitPreview(key.RichText, widthTwips, heightTwips);
        if (!NoteRichContent.TryLoad(editor, note, rtf)) return null;

        string text = editor.Text;
        editor.SelectAll();
        editor.SelectionColor = key.Ink;
        int start = ApplySearchHighlight(editor, text, key.Query, key.Highlight);
        int length = editor.TextLength;
        if (length == 0) return null;

        var bitmap = new Bitmap(key.Size.Width, key.Size.Height, PixelFormat.Format32bppRgb);
        try
        {
            bitmap.SetResolution(key.DpiX, key.DpiY);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(key.Paper);
            int indent = start > 0 ? TextRenderer.MeasureText("…", key.Font).Width : 0;
            var page = new NativeRectangle(0, 0, widthTwips, heightTwips);
            var area = new NativeRectangle(ToTwips(indent, key.DpiX), 0, widthTwips, heightTwips);
            int drawnUntil = RenderRange(editor, graphics, page, area, start, length);
            if (start > 0)
                DrawEllipsis(graphics, key, left: true);
            if (drawnUntil < length)
                DrawEllipsis(graphics, key, left: false);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static int ApplySearchHighlight(RichTextBox control, string text, string query, Color highlight)
    {
        if (query.Length == 0) return 0;

        int first = NoteSearch.Find(text, query, 0, out int length);
        if (first < 0) return 0;

        int start = Math.Max(0, first - 22);
        int lineStart = first > 0 ? text.LastIndexOf('\n', first - 1) + 1 : 0;
        start = Math.Max(start, lineStart);

        // Only paint matches near the portion that can appear in a card.
        int end = Math.Min(text.Length, first + Math.Max(320, length));
        for (int index = first; index >= 0 && index < end; index = NoteSearch.Find(text, query, index + length, out length))
        {
            control.Select(index, length);
            control.SelectionBackColor = highlight;
        }

        return start;
    }

    private static void DrawEllipsis(Graphics graphics, PreviewKey key, bool left)
    {
        const string text = "…";
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        Size size = TextRenderer.MeasureText(text, key.Font, Size.Empty, flags);
        var bounds = new Rectangle(left ? 0 : Math.Max(0, key.Size.Width - size.Width - 1),
            left ? 0 : Math.Max(0, key.Size.Height - size.Height), size.Width + 1, size.Height);
        using var background = new SolidBrush(key.Paper);
        graphics.FillRectangle(background, bounds);
        TextRenderer.DrawText(graphics, text, key.Font, bounds, key.Ink, flags);
    }

    private static int RenderRange(RichTextBox control, Graphics graphics, NativeRectangle page, NativeRectangle area, int start, int end)
    {
        nint window = control.Handle;
        nint device = graphics.GetHdc();
        try
        {
            var range = new NativeFormatRange
            {
                Device = device,
                Target = device,
                Area = area,
                Page = page,
                Start = start,
                End = end
            };
            return (int)SendMessage(window, FormatRangeMessage, 1, ref range);
        }
        finally
        {
            SendMessage(window, FormatRangeMessage, 0, 0);
            graphics.ReleaseHdc(device);
        }
    }

    private static int ToTwips(int pixels, float dpi) => (int)Math.Round(pixels * (double)TwipsPerInch / dpi);

    private void ClearCache()
    {
        foreach (var entry in cache.Values) entry.Image?.Dispose();
        cache.Clear();
    }

    public void Dispose()
    {
        ClearCache();
        editor?.Dispose();
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern nint SendMessage(nint window, uint message, nint wParam, ref NativeFormatRange range);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRectangle(int left, int top, int right, int bottom)
    {
        public readonly int Left = left;
        public readonly int Top = top;
        public readonly int Right = right;
        public readonly int Bottom = bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFormatRange
    {
        public nint Device;
        public nint Target;
        public NativeRectangle Area;
        public NativeRectangle Page;
        public int Start;
        public int End;
    }

    private sealed record PreviewKey(string PlainText, string RichText, string Query, Size Size, float DpiX, float DpiY, Font Font, Color Ink, Color Highlight, Color Paper);
    private sealed record CacheEntry(PreviewKey Key, Bitmap? Image);
}
