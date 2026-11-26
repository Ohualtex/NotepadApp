using System.Text.Json;

namespace NotepadApp;

internal sealed class NotesForm : Form
{
    private readonly NoteStore store;
    private readonly Dictionary<Guid, NoteEditorForm> editors = [];
    private readonly ListBox noteList = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        IntegralHeight = false,
        DrawMode = DrawMode.OwnerDrawFixed,
        DisplayMember = nameof(Note.DisplayTitle),
        AccessibleName = "Notes",
        BackColor = Color.FromArgb(26, 28, 32),
        ForeColor = Color.WhiteSmoke
    };
    private readonly Button add = new() { Text = "+ New note", AutoSize = true };
    private readonly Button save = new() { Text = "Save all", AutoSize = true };
    private readonly Button reload = new() { Text = "Reload", AutoSize = true };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
    private List<Note> notes = [];
    private bool dirty;
    private bool storageReady;

    public NotesForm(NoteStore store)
    {
        this.store = store;
        Text = "NotepadApp";
        Font = new Font("Segoe UI", 10);
        BackColor = noteList.BackColor;
        ForeColor = noteList.ForeColor;
        ClientSize = new Size(420, 570);
        MinimumSize = new Size(320, 360);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(BuildWorkspace());

        noteList.ItemHeight = TextRenderer.MeasureText("Ag", Font).Height * 4 + 28;
        noteList.DrawItem += DrawNoteCard;
        noteList.MouseClick += (_, args) =>
        {
            int index = noteList.IndexFromPoint(args.Location);
            if (args.Button == MouseButtons.Left && index >= 0)
                OpenEditor((Note)noteList.Items[index]);
        };
        noteList.KeyDown += (_, args) =>
        {
            if (args.KeyCode != Keys.Enter || noteList.SelectedItem is not Note note) return;
            args.SuppressKeyPress = true;
            OpenEditor(note);
        };
        add.Click += (_, _) => AddNote();
        save.Click += (_, _) => SaveNotes();
        reload.Click += (_, _) => { if (ResolveChanges()) LoadNotes(); };
        FormClosing += (_, args) => args.Cancel = !ResolveChanges();
        FormClosed += (_, _) => CloseEditors();
        LoadNotes();
    }

    private Control BuildWorkspace()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var heading = new Label { Text = "Notes", AutoSize = true, Margin = new Padding(3, 0, 3, 8) };
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
        actions.Controls.AddRange([add, save, reload]);
        foreach (Button button in actions.Controls) StyleButton(button);
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(actions, 0, 1);
        layout.Controls.Add(noteList, 0, 2);
        layout.Controls.Add(status, 0, 3);
        return layout;
    }

    internal static void StyleButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(67, 72, 83);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(40, 44, 52);
        button.BackColor = Color.FromArgb(50, 55, 65);
        button.ForeColor = Color.WhiteSmoke;
        button.Padding = new Padding(8, 4, 8, 4);
    }

    private void DrawNoteCard(object? sender, DrawItemEventArgs args)
    {
        using var listBackground = new SolidBrush(noteList.BackColor);
        args.Graphics.FillRectangle(listBackground, args.Bounds);
        if (args.Index < 0 || args.Index >= noteList.Items.Count) return;
        var note = (Note)noteList.Items[args.Index];
        Rectangle card = Rectangle.Inflate(args.Bounds, -2, -4);
        bool selected = args.State.HasFlag(DrawItemState.Selected);
        using var background = new SolidBrush(selected ? Color.FromArgb(54, 60, 71) : Color.FromArgb(37, 41, 48));
        using var accent = new SolidBrush(Color.FromArgb(173, 186, 228));
        args.Graphics.FillRectangle(background, card);
        args.Graphics.FillRectangle(accent, card.Right - 24, card.Top + 12, 12, 4);

        Rectangle text = Rectangle.Inflate(card, -12, -8);
        int line = TextRenderer.MeasureText("Ag", Font).Height;
        const TextFormatFlags singleLine = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(args.Graphics, note.DisplayTitle, Font,
            new Rectangle(text.Left, text.Top, Math.Max(0, text.Width - 24), line), ForeColor, singleLine);
        TextRenderer.DrawText(args.Graphics, note.Content.Trim(), Font,
            new Rectangle(text.Left, text.Top + line + 2, text.Width, line * 2), Color.Gainsboro,
            TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(args.Graphics, note.UpdatedAt.LocalDateTime.ToString("d"), Font,
            new Rectangle(text.Left, text.Bottom - line, text.Width, line), Color.Silver, singleLine | TextFormatFlags.Right);
        if (noteList.Focused && args.State.HasFlag(DrawItemState.Focus))
        {
            using var outline = new Pen(Color.FromArgb(173, 186, 228));
            args.Graphics.DrawRectangle(outline, card.Left, card.Top, card.Width - 1, card.Height - 1);
        }
    }

    private void LoadNotes()
    {
        try
        {
            var loaded = store.Load();
            CloseEditors();
            notes = loaded;
            storageReady = true;
            dirty = false;
            RefreshList(notes.FirstOrDefault());
            SetStatus(notes.Count == 0 ? "No saved notes. Select New note to begin." : $"Loaded {notes.Count} notes.");
        }
        catch (Exception error) when (IsStorageError(error))
        {
            storageReady = false;
            SetStatus($"Could not load notes. {error.Message} Fix the file and select Reload.");
        }
    }

    private void RefreshList(Note? selection)
    {
        int previousTop = noteList.TopIndex;
        noteList.BeginUpdate();
        try
        {
            noteList.Items.Clear();
            noteList.Items.AddRange(notes.ToArray());
            noteList.SelectedItem = selection;
            if (noteList.Items.Count > 0) noteList.TopIndex = Math.Clamp(previousTop, 0, noteList.Items.Count - 1);
        }
        finally
        {
            noteList.EndUpdate();
        }
    }

    private void OpenEditor(Note note)
    {
        if (editors.TryGetValue(note.Id, out var existing))
        {
            if (existing.WindowState == FormWindowState.Minimized) existing.WindowState = FormWindowState.Normal;
            existing.Activate();
            return;
        }

        var editor = new NoteEditorForm(note, EditNote, SaveNotes, DeleteNote, ExportNote);
        editors.Add(note.Id, editor);
        editor.FormClosed += (_, _) => editors.Remove(note.Id);
        editor.SetAvailability(storageReady, dirty);
        editor.SetStatus(status.Text);
        editor.Show();
    }

    private void CloseEditors()
    {
        // Closing an editor removes it from the dictionary through FormClosed.
        foreach (var editor in editors.Values.ToArray()) editor.Close();
    }

    private void AddNote()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var note = new Note { CreatedAt = now, UpdatedAt = now };
        notes.Insert(0, note);
        RefreshList(note);
        noteList.TopIndex = 0;
        SetDirty();
        OpenEditor(note);
    }

    private void EditNote(Note note, string title, string content)
    {
        if (note.Title == title && note.Content == content) return;
        bool titleChanged = note.Title != title;
        note.Title = title;
        note.Content = content;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        note.UpdatedAt = now < note.CreatedAt ? note.CreatedAt : now;
        if (titleChanged)
        {
            int index = noteList.Items.IndexOf(note);
            if (index >= 0) noteList.Items[index] = note;
        }
        noteList.Invalidate();
        SetDirty();
    }

    private void DeleteNote(NoteEditorForm editor, Note note)
    {
        if (MessageBox.Show(editor, $"Delete “{note.DisplayTitle}”?", "Delete note", MessageBoxButtons.YesNo,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        int index = notes.IndexOf(note);
        editor.Close();
        notes.RemoveAt(index);
        RefreshList(notes.Count == 0 ? null : notes[Math.Min(index, notes.Count - 1)]);
        SetDirty();
    }

    private void SetDirty()
    {
        dirty = true;
        SetStatus("Unsaved changes");
    }

    private void SetStatus(string message)
    {
        status.Text = message;
        add.Enabled = storageReady;
        save.Enabled = storageReady && dirty;
        foreach (var editor in editors.Values)
        {
            editor.SetAvailability(storageReady, dirty);
            editor.SetStatus(message);
        }
    }

    private bool SaveNotes()
    {
        if (!storageReady) return false;
        try
        {
            store.Save(notes);
            dirty = false;
            SetStatus($"Saved {notes.Count} notes.");
            return true;
        }
        catch (Exception error) when (IsStorageError(error))
        {
            SetStatus($"Could not save notes. {error.Message}");
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

    private void ExportNote(NoteEditorForm editor, Note note)
    {
        using var dialog = new SaveFileDialog { Title = "Export note", Filter = "Text files (*.txt)|*.txt", DefaultExt = "txt", AddExtension = true };
        if (dialog.ShowDialog(editor) != DialogResult.OK) return;
        try
        {
            NoteStore.Export(note, dialog.FileName);
            editor.SetStatus($"Exported {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception error) when (IsStorageError(error))
        {
            editor.SetStatus($"Could not export the note. {error.Message}");
        }
    }

    private static bool IsStorageError(Exception error) =>
        error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException;
}
