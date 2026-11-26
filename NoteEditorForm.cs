namespace NotepadApp;

internal sealed class NoteEditorForm : Form
{
    private readonly TextBox title = new()
    {
        Dock = DockStyle.Fill,
        AccessibleName = "Note title",
        PlaceholderText = "Title",
        BorderStyle = BorderStyle.FixedSingle
    };
    private readonly RichTextBox content = new()
    {
        Dock = DockStyle.Fill,
        AccessibleName = "Note content",
        DetectUrls = false,
        BorderStyle = BorderStyle.None
    };
    private readonly Label dates = new() { Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.Silver };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.Silver };
    private readonly Button save = new() { Text = "Save all", AutoSize = true };
    private readonly Button delete = new() { Text = "Delete", AutoSize = true };
    private readonly Button export = new() { Text = "Export .txt", AutoSize = true };

    public NoteEditorForm(Note note, Action<Note, string, string> edit, Func<bool> saveNotes,
        Action<NoteEditorForm, Note> deleteNote, Action<NoteEditorForm, Note> exportNote)
    {
        Text = note.DisplayTitle;
        ClientSize = new Size(390, 350);
        MinimumSize = new Size(305, 240);
        StartPosition = FormStartPosition.WindowsDefaultLocation;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(30, 33, 38);
        ForeColor = Color.WhiteSmoke;
        title.BackColor = content.BackColor = Color.FromArgb(41, 45, 54);
        title.ForeColor = content.ForeColor = ForeColor;
        title.Text = note.Title;
        content.Text = note.Content;
        UpdateDates(note);
        Controls.Add(BuildWorkspace());
        ActiveControl = note.Title.Length == 0 ? title : content;

        void UpdateNote()
        {
            edit(note, title.Text, content.Text);
            Text = note.DisplayTitle;
            UpdateDates(note);
        }

        title.TextChanged += (_, _) => UpdateNote();
        content.TextChanged += (_, _) => UpdateNote();
        save.Click += (_, _) => saveNotes();
        delete.Click += (_, _) => deleteNote(this, note);
        export.Click += (_, _) => exportNote(this, note);
    }

    private Control BuildWorkspace()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        actions.Controls.AddRange([save, export, delete]);
        foreach (Button button in actions.Controls) NotesForm.StyleButton(button);
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(content, 0, 1);
        layout.Controls.Add(dates, 0, 2);
        layout.Controls.Add(actions, 0, 3);
        layout.Controls.Add(status, 0, 4);
        return layout;
    }

    private void UpdateDates(Note note)
    {
        dates.Text = $"Created {note.CreatedAt.ToLocalTime():g} · Updated {note.UpdatedAt.ToLocalTime():g}";
    }

    public void SetAvailability(bool storageReady, bool dirty)
    {
        title.Enabled = content.Enabled = delete.Enabled = storageReady;
        save.Enabled = storageReady && dirty;
    }

    public void SetStatus(string message) => status.Text = message;
}
