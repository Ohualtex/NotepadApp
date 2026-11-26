namespace NotepadApp;

internal static class NotePalette
{
    public const string DefaultId = "lavender";

    public static IReadOnlyList<(string Id, string Label, Color Color)> Colors { get; } =
    [
        (DefaultId, "Lavender", Color.FromArgb(173, 186, 228)),
        ("sage", "Sage", Color.FromArgb(155, 198, 170)),
        ("apricot", "Apricot", Color.FromArgb(235, 184, 143)),
        ("sky", "Sky", Color.FromArgb(144, 199, 221)),
        ("rose", "Rose", Color.FromArgb(228, 166, 183)),
        ("slate", "Slate", Color.FromArgb(173, 183, 198))
    ];

    public static (string Id, string Label, Color Color) Resolve(string? id)
    {
        foreach (var accent in Colors)
        {
            if (accent.Id == id) return accent;
        }

        return Colors[0];
    }
}
