using System.Runtime.InteropServices;

namespace NotepadApp;

internal static class RichTextFormatting
{
    private const uint GetCharacterFormat = 0x043A;
    private const uint GetEventMask = 0x043B;
    private const uint GetOleInterface = 0x043C;
    private const uint SetCharacterFormat = 0x0444;
    private const uint SetEventMask = 0x0445;
    private const int Selection = 1;
    private const int SelectionChangeNotification = 0x00080000;
    // ITextDocument starts after the IUnknown and IDispatch slots.
    private const int BeginEditCollectionSlot = 20;
    private const int EndEditCollectionSlot = 21;
    private static readonly Guid TextDocumentId = new("8CC497C0-A1DF-11CE-8098-00AA0047BE5D");

    public static FontStyle GetActiveStyles(RichTextBox editor)
    {
        FontStyle styles = ReadStyles(editor);
        if ((styles & FontStyle.Strikeout) != 0 || !ContainsParagraphBreak(editor)) return styles;
        if (WithPreservedSelection(editor, () => AllTextIsStruckOut(editor, GetSelectionRuns(editor))))
            styles |= FontStyle.Strikeout;
        return styles;
    }

    public static void ToggleStyle(RichTextBox editor, FontStyle style)
    {
        if (style == FontStyle.Strikeout && ContainsParagraphBreak(editor))
        {
            WithPreservedSelection(editor, () =>
            {
                var runs = GetSelectionRuns(editor);
                bool enable = !AllTextIsStruckOut(editor, runs);
                ApplyStrikeoutRuns(editor, runs, enable);
                return 0;
            });
            return;
        }

        SetStyle(editor, style, (ReadStyles(editor) & style) == 0);
    }

    private static void ApplyStrikeoutRuns(RichTextBox editor, List<SelectionRun> runs, bool enabled)
    {
        nint document = GetTextDocument(editor.Handle);
        bool grouped = document != 0 && InvokeEditCollection(document, BeginEditCollectionSlot) >= 0;
        try
        {
            foreach (var run in runs)
            {
                editor.Select(run.Start, run.Length);
                // Rich Edit uses paragraph marks to draw list markers.
                SetStyle(editor, FontStyle.Strikeout, enabled && !run.IsParagraphBreak);
            }
        }
        finally
        {
            if (document != 0)
            {
                if (grouped) InvokeEditCollection(document, EndEditCollectionSlot);
                Marshal.Release(document);
            }
        }
    }

    private static nint GetTextDocument(nint window)
    {
        SendMessage(window, GetOleInterface, 0, out nint owner);
        if (owner == 0) return 0;

        try
        {
            Guid interfaceId = TextDocumentId;
            return Marshal.QueryInterface(owner, in interfaceId, out nint document) >= 0 ? document : 0;
        }
        finally { Marshal.Release(owner); }
    }

    private static int InvokeEditCollection(nint document, int slot)
    {
        nint table = Marshal.ReadIntPtr(document);
        nint method = Marshal.ReadIntPtr(table, slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<EditCollectionMethod>(method)(document);
    }

    private static FontStyle ReadStyles(RichTextBox editor)
    {
        var format = CreateFormat();
        SendMessage(editor.Handle, GetCharacterFormat, Selection, ref format);

        // Rich Edit marks attributes that are uniform across the entire selection.
        return (FontStyle)(format.Mask & format.Effects & 0xF);
    }

    private static void SetStyle(RichTextBox editor, FontStyle style, bool enabled)
    {
        // FontStyle's four effect bits match Rich Edit's bold, italic, underline and strikeout flags.
        var format = CreateFormat();
        format.Mask = (uint)style;
        format.Effects = enabled ? (uint)style : 0;
        SendMessage(editor.Handle, SetCharacterFormat, Selection, ref format);
    }

    private static bool ContainsParagraphBreak(RichTextBox editor) =>
        editor.SelectionLength > 0 && editor.SelectedText.AsSpan().IndexOfAny('\r', '\n') >= 0;

    private static List<SelectionRun> GetSelectionRuns(RichTextBox editor)
    {
        int start = editor.SelectionStart;
        string text = editor.SelectedText;
        List<SelectionRun> runs = [];
        for (int offset = 0; offset < text.Length;)
        {
            bool isBreak = text[offset] is '\r' or '\n';
            int end = offset + 1;
            while (end < text.Length && (text[end] is '\r' or '\n') == isBreak) end++;
            runs.Add(new SelectionRun(start + offset, end - offset, isBreak));
            offset = end;
        }
        return runs;
    }

    private static bool AllTextIsStruckOut(RichTextBox editor, List<SelectionRun> runs)
    {
        bool hasText = false;
        foreach (var run in runs)
        {
            if (run.IsParagraphBreak) continue;
            hasText = true;
            editor.Select(run.Start, run.Length);
            if ((ReadStyles(editor) & FontStyle.Strikeout) == 0) return false;
        }
        return hasText;
    }

    private static T WithPreservedSelection<T>(RichTextBox editor, Func<T> action)
    {
        int start = editor.SelectionStart;
        int length = editor.SelectionLength;
        nint window = editor.Handle;
        nint mask = SendMessage(window, GetEventMask, 0, 0);
        SendMessage(window, SetEventMask, 0, mask & ~SelectionChangeNotification);
        try { return action(); }
        finally
        {
            editor.Select(start, length);
            SendMessage(window, SetEventMask, 0, mask);
        }
    }

    private readonly record struct SelectionRun(int Start, int Length, bool IsParagraphBreak);

    private static CharacterFormat CreateFormat() => new()
    {
        Size = (uint)Marshal.SizeOf<CharacterFormat>(),
        FaceName = ""
    };

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint window, uint message, nint wParam, ref CharacterFormat format);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern nint SendMessage(nint window, uint message, nint wParam, out nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EditCollectionMethod(nint document);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CharacterFormat
    {
        public uint Size;
        public uint Mask;
        public uint Effects;
        public int Height;
        public int Offset;
        public int TextColor;
        public byte CharacterSet;
        public byte PitchAndFamily;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FaceName;
    }
}
