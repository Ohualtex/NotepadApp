using System.Runtime.InteropServices;

namespace NotepadApp;

internal static class RichTextFormatting
{
    private const uint GetCharacterFormat = 0x043A;
    private const uint SetCharacterFormat = 0x0444;
    private const int Selection = 1;

    public static FontStyle GetActiveStyles(RichTextBox editor)
    {
        var format = CreateFormat();
        SendMessage(editor.Handle, GetCharacterFormat, Selection, ref format);

        // Rich Edit marks attributes that are uniform across the entire selection.
        return (FontStyle)(format.Mask & format.Effects & 0xF);
    }

    public static void ToggleStyle(RichTextBox editor, FontStyle style)
    {
        // FontStyle's four effect bits match Rich Edit's bold, italic, underline and strikeout flags.
        var format = CreateFormat();
        format.Mask = (uint)style;
        format.Effects = (GetActiveStyles(editor) & style) == 0 ? (uint)style : 0;
        SendMessage(editor.Handle, SetCharacterFormat, Selection, ref format);
    }

    private static CharacterFormat CreateFormat() => new()
    {
        Size = (uint)Marshal.SizeOf<CharacterFormat>(),
        FaceName = ""
    };

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint window, uint message, nint wParam, ref CharacterFormat format);

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
