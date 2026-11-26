using System.Text.Json.Serialization;

namespace NotepadApp;

internal sealed class Note
{
    [JsonRequired]
    public Guid Id { get; init; } = Guid.NewGuid();
    [JsonRequired]
    public string Title { get; set; } = "";
    [JsonRequired]
    public string Content { get; set; } = "";
    [JsonRequired]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    [JsonRequired]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Untitled note" : Title;
}
