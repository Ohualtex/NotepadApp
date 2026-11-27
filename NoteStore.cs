using System.Text;
using System.Text.Json;

namespace NotepadApp;

internal sealed class NoteStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private string PreferencesPath => Path.Combine(Path.GetDirectoryName(path)!, "settings.json");

    public AppPreferences LoadPreferences()
    {
        try
        {
            var preferences = JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(PreferencesPath), Options);
            return preferences is not null && Enum.IsDefined(preferences.Theme)
                ? preferences
                : throw new InvalidDataException("The settings file contains invalid preferences.");
        }
        catch (FileNotFoundException)
        {
            return new AppPreferences();
        }
        catch (DirectoryNotFoundException)
        {
            return new AppPreferences();
        }
    }

    public void SavePreferences(AppPreferences preferences) => WriteJson(PreferencesPath, preferences);

    public List<Note> Load()
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }

        List<Note>? notes = JsonSerializer.Deserialize<List<Note>>(json, Options);
        if (notes is null || notes.Any(note => note is null || note.Title is null || note.Content is null ||
            note.Id == Guid.Empty || note.UpdatedAt < note.CreatedAt) ||
            notes.Select(note => note.Id).Distinct().Count() != notes.Count)
        {
            throw new InvalidDataException("The notes file contains invalid entries.");
        }

        return notes;
    }

    public void SaveWindowPlacements(IReadOnlyDictionary<Guid, NoteWindowPlacement> placements)
    {
        // Read from disk so moving an editor cannot persist unsaved note content or pending deletions.
        List<Note> savedNotes = Load();
        bool changed = false;

        foreach (var note in savedNotes)
        {
            if (!placements.TryGetValue(note.Id, out var placement) || note.WindowPlacement == placement) continue;
            note.WindowPlacement = placement;
            changed = true;
        }

        if (changed) Save(savedNotes);
    }

    public void Save(List<Note> notes) => WriteJson(path, notes);

    private static void WriteJson<T>(string destination, T value) =>
        WriteFile(destination, stream => JsonSerializer.Serialize(stream, value, Options));

    private static void WriteFile(string destination, Action<FileStream> write)
    {
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string pendingPath = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(pendingPath, FileMode.CreateNew))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(pendingPath, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(pendingPath)) File.Delete(pendingPath);
        }
    }

    public static void Export(IEnumerable<Note> notes, string destination)
    {
        if (!Path.GetExtension(destination).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a file name ending in .txt.");

        WriteFile(destination, stream =>
        {
            using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
            bool first = true;
            foreach (var note in notes.OrderByDescending(note => note.UpdatedAt))
            {
                if (!first)
                {
                    writer.WriteLine();
                    writer.WriteLine();
                    writer.WriteLine("---");
                    writer.WriteLine();
                }

                writer.WriteLine(note.DisplayTitle);
                writer.WriteLine();
                writer.Write(note.Content);
                first = false;
            }
        });
    }
}
