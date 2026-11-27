using System.Text.Json.Serialization;

namespace NotepadApp;

[JsonConverter(typeof(JsonStringEnumConverter<ThemePreference>))]
internal enum ThemePreference { Light, Dark, System }

internal sealed record AppPreferences(
    ThemePreference Theme = ThemePreference.Light,
    bool ConfirmBeforeDeleting = true,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    NoteWindowPlacement? ListWindowPlacement = null);
