using System.Globalization;

namespace NotepadApp;

internal static class NoteSearch
{
    private const string Ellipsis = "…";

    public static bool Matches(Note note, string query) =>
        query.Length == 0 || Find(note.Title, query, 0, out _) >= 0 || Find(note.Content, query, 0, out _) >= 0;

    public static string GetSnippet(string value, string query, int limit)
    {
        string text = value.Trim();
        int start = 0;
        if (query.Length > 0)
        {
            text = text.ReplaceLineEndings(" ");
            int match = Find(text, query, 0, out int matchLength);
            if (match >= 0)
            {
                start = Math.Max(0, match - 22);
                limit = Math.Max(limit, matchLength + 22);
            }
        }

        int length = Math.Min(text.Length - start, limit);
        string snippet = text.Substring(start, length);
        if (start > 0) snippet = Ellipsis + snippet;
        if (start + length < text.Length) snippet += Ellipsis;
        return snippet;
    }

    public static int Find(string text, string query, int start, out int matchLength)
    {
        int index = CultureInfo.CurrentCulture.CompareInfo.IndexOf(
            text.AsSpan(start), query.AsSpan(), CompareOptions.IgnoreCase, out matchLength);
        return index >= 0 && matchLength > 0 ? start + index : -1;
    }
}
