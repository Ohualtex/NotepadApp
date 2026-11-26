using System.Text.Json;

namespace NotepadApp;

internal sealed class NotesForm : Form
{
    private readonly NoteStore store;
    private readonly ListBox noteList = new() { Dock = DockStyle.Fill, IntegralHeight = false, DisplayMember = nameof(Note.DisplayTitle) };
    private readonly TextBox title = new() { Dock = DockStyle.Fill, AccessibleName = "Note title" };
    private readonly RichTextBox content = new() { Dock = DockStyle.Fill, AccessibleName = "Note content", DetectUrls = false };
    private readonly Label dates = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly Button add = new() { Text = "New note", AutoSize = true };
    private readonly Button delete = new() { Text = "Delete", AutoSize = true };
    private readonly Button save = new() { Text = "Save", AutoSize = true };
    private readonly Button reload = new() { Text = "Reload", AutoSize = true };
    private readonly Button export = new() { Text = "Export .txt", AutoSize = true };
    private List<Note> notes = [];
    private bool binding;
    private bool dirty;
    private bool storageReady;

    public NotesForm(NoteStore store)
    {
        this.store = store;
        Text = "Notepad";
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(960, 640);
        MinimumSize = new Size(660, 420);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(BuildWorkspace());

        noteList.SelectedIndexChanged += (_, _) => ShowSelection();
        title.TextChanged += (_, _) => UpdateSelection();
        content.TextChanged += (_, _) => UpdateSelection();
        add.Click += (_, _) => AddNote();
        delete.Click += (_, _) => DeleteNote();
        save.Click += (_, _) => SaveNotes();
        reload.Click += (_, _) => { if (ResolveChanges()) LoadNotes(); };
        export.Click += (_, _) => ExportNote();
        FormClosing += (_, args) => args.Cancel = !ResolveChanges();

        LoadNotes();
    }

    private Control BuildWorkspace()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(16) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        actions.Controls.AddRange([add, save, reload, delete, export]);
        root.Controls.Add(actions, 0, 0);
        root.SetColumnSpan(actions, 2);
        root.Controls.Add(noteList, 0, 1);

        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8, 0, 0, 0) };
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        title.PlaceholderText = "Title";
        dates.Padding = new Padding(0, 6, 0, 0);
        editor.Controls.Add(title, 0, 0);
        editor.Controls.Add(content, 0, 1);
        editor.Controls.Add(dates, 0, 2);
        root.Controls.Add(editor, 1, 1);
        status.Padding = new Padding(0, 8, 0, 0);
        root.Controls.Add(status, 0, 2);
        root.SetColumnSpan(status, 2);
        return root;
    }

    private void LoadNotes()
    {
        try
        {
            notes = store.Load();
            storageReady = true;
            dirty = false;
            RefreshList(notes.FirstOrDefault());
            status.Text = notes.Count == 0 ? "No saved notes. Select New note to begin." : $"Loaded {notes.Count} notes.";
        }
        catch (Exception error) when (IsStorageError(error))
        {
            storageReady = false;
            ShowSelection();
            status.Text = $"Could not load notes. {error.Message} Fix the file and select Reload.";
        }
    }

    private void RefreshList(Note? selection)
    {
        binding = true;
        try
        {
            noteList.Items.Clear();
            noteList.Items.AddRange(notes.ToArray());
            noteList.SelectedItem = selection;
        }
        finally
        {
            binding = false;
        }

        ShowSelection();
    }

    private void ShowSelection()
    {
        if (binding) return;
        var selected = noteList.SelectedItem as Note;
        binding = true;
        try
        {
            title.Text = selected?.Title ?? "";
            content.Text = selected?.Content ?? "";
            UpdateDates(selected);
        }
        finally
        {
            binding = false;
        }

        title.Enabled = content.Enabled = delete.Enabled = storageReady && selected is not null;
        add.Enabled = storageReady;
        save.Enabled = storageReady && dirty;
        export.Enabled = selected is not null;
    }

    private void UpdateSelection()
    {
        if (binding || noteList.SelectedItem is not Note note) return;
        bool titleChanged = note.Title != title.Text;
        if (!titleChanged && note.Content == content.Text) return;

        note.Title = title.Text;
        note.Content = content.Text;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        note.UpdatedAt = now < note.CreatedAt ? note.CreatedAt : now;
        if (titleChanged)
        {
            binding = true;
            try { noteList.Items[noteList.SelectedIndex] = note; }
            finally { binding = false; }
        }

        UpdateDates(note);
        MarkDirty();
    }

    private void UpdateDates(Note? note) => dates.Text = note is null ? "" :
        $"Created {note.CreatedAt.ToLocalTime():g}   ·   Updated {note.UpdatedAt.ToLocalTime():g}";

    private void AddNote()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var note = new Note { CreatedAt = now, UpdatedAt = now };
        notes.Add(note);
        MarkDirty();
        RefreshList(note);
        title.Focus();
    }

    private void DeleteNote()
    {
        if (noteList.SelectedItem is not Note note) return;
        if (MessageBox.Show(this, $"Delete “{note.DisplayTitle}”?", "Delete note", MessageBoxButtons.YesNo,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        int index = noteList.SelectedIndex;
        notes.Remove(note);
        MarkDirty();
        RefreshList(notes.Count == 0 ? null : notes[Math.Min(index, notes.Count - 1)]);
    }

    private void MarkDirty()
    {
        dirty = true;
        save.Enabled = storageReady;
        status.Text = "Unsaved changes";
    }

    private bool SaveNotes()
    {
        if (!storageReady) return false;
        try
        {
            store.Save(notes);
            dirty = false;
            save.Enabled = false;
            status.Text = $"Saved {notes.Count} notes.";
            return true;
        }
        catch (Exception error) when (IsStorageError(error))
        {
            status.Text = $"Could not save notes. {error.Message}";
            return false;
        }
    }

    private bool ResolveChanges()
    {
        if (!dirty) return true;
        return MessageBox.Show(this, "Save your changes?", "Unsaved changes", MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) switch
        {
            DialogResult.Yes => SaveNotes(),
            DialogResult.No => true,
            _ => false
        };
    }

    private void ExportNote()
    {
        if (noteList.SelectedItem is not Note note) return;
        using var dialog = new SaveFileDialog { Title = "Export note", Filter = "Text files (*.txt)|*.txt", DefaultExt = "txt", AddExtension = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            NoteStore.Export(note, dialog.FileName);
            status.Text = $"Exported {Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception error) when (IsStorageError(error))
        {
            status.Text = $"Could not export the note. {error.Message}";
        }
    }

    private static bool IsStorageError(Exception error) =>
        error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException;
}
