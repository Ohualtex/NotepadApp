using System.Text;
using System.Text.Json;

namespace NotepadApp;

internal sealed class NoteStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

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

    public void Save(List<Note> notes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string pendingPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(pendingPath, FileMode.CreateNew))
            {
                JsonSerializer.Serialize(stream, notes, Options);
                stream.Flush(flushToDisk: true);
            }

            File.Move(pendingPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(pendingPath)) File.Delete(pendingPath);
        }
    }

    public static void Export(Note note, string destination)
    {
        if (!Path.GetExtension(destination).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a file name ending in .txt.");
        File.WriteAllText(destination, note.DisplayTitle + Environment.NewLine + Environment.NewLine + note.Content, Encoding.UTF8);
    }
}
