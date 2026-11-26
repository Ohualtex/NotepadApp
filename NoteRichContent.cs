namespace NotepadApp;

internal static class NoteRichContent
{
    public static bool TryLoad(RichTextBox editor, Note note, string? richText)
    {
        if (string.IsNullOrEmpty(richText)) return false;

        try
        {
            editor.Rtf = richText;
            return PlainText(editor, NoteImage.ContainsPicture(richText)).ReplaceLineEndings("\n") ==
                note.Content.ReplaceLineEndings("\n");
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static string PlainText(RichTextBox editor, bool hasImage)
    {
        // Rich Edit returns inline images as spaces in its plain-text representation.
        string text = editor.Text;
        return hasImage && string.IsNullOrWhiteSpace(text) ? "" : text;
    }
}
