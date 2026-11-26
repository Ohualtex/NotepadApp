using Microsoft.Win32;

namespace NotepadApp;

internal sealed record NotesTheme(
    Color Surface, Color Ink, Color MutedInk, Color Control, Color Hover, Color Pressed,
    Color Primary, Color Highlight, Color PaperBase, int PaperTint, int SelectedPaperTint)
{
    private static readonly NotesTheme Light = new(
        Color.FromArgb(246, 242, 237), Color.FromArgb(40, 35, 43), Color.FromArgb(87, 78, 86),
        Color.FromArgb(235, 229, 224), Color.FromArgb(225, 217, 221), Color.FromArgb(215, 206, 217),
        Color.FromArgb(211, 198, 228), Color.FromArgb(247, 214, 128), Color.White, 35, 50);

    private static readonly NotesTheme Dark = new(
        Color.FromArgb(34, 31, 40), Color.FromArgb(245, 241, 248), Color.FromArgb(202, 192, 209),
        Color.FromArgb(58, 52, 67), Color.FromArgb(72, 64, 84), Color.FromArgb(52, 45, 62),
        Color.FromArgb(201, 170, 233), Color.FromArgb(118, 82, 37), Color.FromArgb(42, 36, 49), 14, 25);

    public static Color PrimaryInk { get; } = Color.FromArgb(36, 27, 46);

    public static NotesTheme Resolve(ThemePreference preference) => preference switch
    {
        ThemePreference.Dark => Dark,
        ThemePreference.System when SystemUsesDark() => Dark,
        _ => Light
    };

    public Color NotePaper(Color accent, bool selected)
    {
        int tint = selected ? SelectedPaperTint : PaperTint;
        return Color.FromArgb(
            (accent.R * tint + PaperBase.R * (100 - tint)) / 100,
            (accent.G * tint + PaperBase.G * (100 - tint)) / 100,
            (accent.B * tint + PaperBase.B * (100 - tint)) / 100);
    }

    private static bool SystemUsesDark() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0;
}
