using System.Drawing.Drawing2D;
using System.Text.Json;

namespace NotepadApp;

internal sealed class NotesForm : Form
{
    private static readonly Color ListSurface = Color.FromArgb(246, 242, 237);
    private static readonly Color Ink = Color.FromArgb(40, 35, 43);
    private static readonly Color MutedInk = Color.FromArgb(87, 78, 86);
    private static readonly Color SearchSurface = Color.FromArgb(235, 229, 224);
    private readonly Font listHeading = new("Segoe UI", 14, FontStyle.Bold);
    private readonly Font cardTitle = new("Segoe UI", 10, FontStyle.Bold);
    private readonly Font cardDate = new("Segoe UI", 8);
    private readonly NoteStore store;
    private readonly System.Windows.Forms.Timer autoSave = new() { Interval = 700 };
    private readonly Dictionary<Guid, NoteEditorForm> editors = [];
    private readonly ListBox noteList = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        IntegralHeight = false,
        DrawMode = DrawMode.OwnerDrawFixed,
        DisplayMember = nameof(Note.DisplayTitle),
        AccessibleName = "Notes",
        BackColor = ListSurface,
        ForeColor = Ink
    };
    private readonly Button add = new() { Text = "+ New note", AutoSize = true };
    private readonly Button save = new() { Text = "Save all", AutoSize = true };
    private readonly Button reload = new() { Text = "Reload", AutoSize = true };
    private readonly TextBox search = new()
    {
        Dock = DockStyle.Fill,
        AccessibleName = "Search notes",
        BorderStyle = BorderStyle.None,
        BackColor = SearchSurface,
        ForeColor = Ink
    };
    private readonly Button clearSearch = new() { Text = "×", AccessibleName = "Clear search", AutoSize = true, Visible = false };
    private readonly Label noResults = new() { Text = "No matching notes.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Visible = false };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 8, 0, 0), Visible = false };
    private List<Note> notes = [];
    private bool dirty;
    private bool storageReady;
    private bool closingEditors;
    private string searchQuery = "";

    public NotesForm(NoteStore store)
    {
        this.store = store;
        Text = "NotepadApp";
        Font = new Font("Segoe UI", 10);
        BackColor = noteList.BackColor;
        ForeColor = noteList.ForeColor;
        ClientSize = new Size(420, 570);
        MinimumSize = new Size(320, 360);
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(BuildWorkspace());

        noteList.ItemHeight = TextRenderer.MeasureText("Ag", Font).Height * 3 + 26;
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
        search.TextChanged += (_, _) =>
        {
            searchQuery = search.Text.Trim();
            clearSearch.Visible = search.TextLength > 0;
            RefreshList(noteList.SelectedItem as Note, resetScroll: true);
        };
        clearSearch.Click += (_, _) =>
        {
            search.Clear();
            search.Focus();
        };
        autoSave.Tick += (_, _) => SaveNotes();
        Disposed += (_, _) =>
        {
            autoSave.Dispose();
            listHeading.Dispose();
            cardTitle.Dispose();
            cardDate.Dispose();
        };
        add.Click += (_, _) => AddNote();
        save.Click += (_, _) => SaveNotes();
        reload.Click += (_, _) =>
        {
            if (ResolveChanges() && RememberWindowPlacements(notes, this)) LoadNotes();
        };
        FormClosing += (_, args) => args.Cancel = !ResolveChanges() || !RememberWindowPlacements(notes, this);
        FormClosed += (_, _) => CloseEditors();
        LoadNotes();
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F))
        {
            search.Focus();
            search.SelectAll();
            return true;
        }

        if (keyData == Keys.Escape && search.Focused && search.TextLength > 0)
        {
            search.Clear();
            return true;
        }

        return base.ProcessCmdKey(ref message, keyData);
    }

    private Control BuildWorkspace()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(14) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var heading = new Label { Text = "Notes", Font = listHeading, AutoSize = true, Margin = new Padding(3, 0, 3, 8) };
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
        actions.Controls.AddRange([add, save, reload]);
        foreach (Button button in actions.Controls) StyleButton(button);
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(actions, 0, 1);
        add.BackColor = Color.FromArgb(211, 198, 228);
        add.ForeColor = Ink;
        add.FlatAppearance.MouseOverBackColor = Color.FromArgb(222, 211, 237);
        add.FlatAppearance.MouseDownBackColor = Color.FromArgb(195, 180, 214);
        foreach (Button button in new[] { save, reload, clearSearch })
        {
            StyleButton(button);
            button.BackColor = SearchSurface;
            button.ForeColor = Ink;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(225, 217, 221);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(215, 206, 217);
        }

        var searchRow = new TableLayoutPanel { Height = 38, BackColor = SearchSurface, Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(7, 4, 7, 4) };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var magnifier = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        magnifier.Paint += (_, args) =>
        {
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(MutedInk, 1.5f);
            int y = Math.Max(0, (magnifier.Height - 14) / 2);
            args.Graphics.DrawEllipse(pen, 4, y, 9, 9);
            args.Graphics.DrawLine(pen, 12, y + 8, 17, y + 13);
        };
        magnifier.Click += (_, _) => search.Focus();
        var searchInput = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(2, 6, 4, 0) };
        var searchHint = new Label { Text = "Search notes", ForeColor = MutedInk, Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft };
        searchInput.Controls.Add(search);
        searchInput.Controls.Add(searchHint);
        searchHint.BringToFront();
        void UpdateSearchHint() => searchHint.Visible = search.TextLength == 0 && !search.Focused;
        search.TextChanged += (_, _) => UpdateSearchHint();
        search.GotFocus += (_, _) => UpdateSearchHint();
        search.LostFocus += (_, _) => UpdateSearchHint();
        searchHint.Click += (_, _) => search.Focus();
        clearSearch.Margin = Padding.Empty;
        clearSearch.Padding = new Padding(4, 0, 4, 0);
        searchRow.Controls.Add(magnifier, 0, 0);
        searchRow.Controls.Add(searchInput, 1, 0);
        searchRow.Controls.Add(clearSearch, 2, 0);
        layout.Controls.Add(searchRow, 0, 2);

        var results = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        results.Controls.Add(noteList);
        results.Controls.Add(noResults);
        layout.Controls.Add(results, 0, 3);
        layout.Controls.Add(status, 0, 4);
        return layout;
    }

    internal static void StyleButton(ButtonBase button)
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
        if (card.Width < 16 || card.Height < 16) return;

        bool focused = noteList.Focused && args.State.HasFlag(DrawItemState.Selected);
        Color accent = NotePalette.Resolve(note.Accent).Color;
        int weight = focused ? 50 : 35;
        Color paper = Color.FromArgb(
            (accent.R * weight + 255 * (100 - weight)) / 100,
            (accent.G * weight + 255 * (100 - weight)) / 100,
            (accent.B * weight + 255 * (100 - weight)) / 100);
        using var background = new SolidBrush(paper);
        using var shape = new GraphicsPath();
        const int diameter = 16;
        shape.AddArc(card.Left, card.Top, diameter, diameter, 180, 90);
        shape.AddArc(card.Right - diameter, card.Top, diameter, diameter, 270, 90);
        shape.AddArc(card.Right - diameter, card.Bottom - diameter, diameter, diameter, 0, 90);
        shape.AddArc(card.Left, card.Bottom - diameter, diameter, diameter, 90, 90);
        shape.CloseFigure();
        SmoothingMode previousSmoothing = args.Graphics.SmoothingMode;
        args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        args.Graphics.FillPath(background, shape);
        args.Graphics.SmoothingMode = previousSmoothing;

        Rectangle text = Rectangle.Inflate(card, -12, -8);
        int line = TextRenderer.MeasureText("Ag", Font).Height;
        string date = note.UpdatedAt.LocalDateTime.ToString("M");
        const TextFormatFlags singleLine = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        int dateWidth = Math.Min(text.Width / 2, TextRenderer.MeasureText(date, cardDate).Width);
        TextRenderer.DrawText(args.Graphics, date, cardDate,
            new Rectangle(text.Right - dateWidth, text.Top + 2, dateWidth, line), MutedInk, singleLine | TextFormatFlags.Right);
        int firstRowWidth = Math.Max(0, text.Width - dateWidth - 8);
        bool hasTitle = !string.IsNullOrWhiteSpace(note.Title);
        if (hasTitle)
        {
            DrawSearchText(args.Graphics, NoteSearch.GetSnippet(note.Title, searchQuery, 110),
                new Rectangle(text.Left, text.Top, firstRowWidth, line), cardTitle, Ink, singleLine: true);
        }

        string preview = note.Content.Length == 0 && NoteImage.ContainsPicture(note.RichText) ? "Image" : note.Content;
        int previewTop = hasTitle ? text.Top + line + 2 : text.Top;
        int previewWidth = hasTitle ? text.Width : firstRowWidth;
        DrawSearchText(args.Graphics, NoteSearch.GetSnippet(preview, searchQuery, 220),
            new Rectangle(text.Left, previewTop, previewWidth, text.Bottom - previewTop), Font, Ink, singleLine: false);
    }

    private void DrawSearchText(Graphics graphics, string text, Rectangle bounds, Font font, Color foreground, bool singleLine)
    {
        if (text.Length == 0 || bounds.Width <= 0) return;

        using var format = new StringFormat(StringFormatFlags.LineLimit | (singleLine ? StringFormatFlags.NoWrap : 0))
        {
            Trimming = StringTrimming.EllipsisCharacter
        };

        if (searchQuery.Length > 0)
        {
            List<CharacterRange> matches = [];
            int start = 0;
            while (start < text.Length)
            {
                int index = NoteSearch.Find(text, searchQuery, start, out int length);
                if (index < 0) break;
                matches.Add(new CharacterRange(index, length));
                start = index + length;
            }

            using var highlight = new SolidBrush(Color.FromArgb(247, 214, 128));
            // GDI+ accepts at most 32 measured character ranges at a time.
            foreach (var batch in matches.Chunk(32))
            {
                format.SetMeasurableCharacterRanges(batch);
                foreach (var region in graphics.MeasureCharacterRanges(text, font, bounds, format))
                {
                    using var measured = region;
                    measured.Intersect(bounds);
                    graphics.FillRegion(highlight, measured);
                }
            }
        }

        using var textBrush = new SolidBrush(foreground);
        graphics.DrawString(text, font, textBrush, bounds, format);
    }

    private void LoadNotes()
    {
        autoSave.Stop();
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
            SetStatus($"Could not load notes. {error.Message} Fix the file and select Reload.", showNotice: true);
        }
    }

    private void RefreshList(Note? selection, bool resetScroll = false)
    {
        int previousTop = resetScroll ? 0 : noteList.TopIndex;
        var visibleNotes = notes.Where(note => NoteSearch.Matches(note, searchQuery)).ToArray();
        noteList.BeginUpdate();
        try
        {
            noteList.Items.Clear();
            noteList.Items.AddRange(visibleNotes);
            if (visibleNotes.Length > 0)
            {
                noteList.SelectedItem = visibleNotes.Contains(selection) ? selection : visibleNotes[0];
                noteList.TopIndex = Math.Clamp(previousTop, 0, visibleNotes.Length - 1);
            }

            noteList.Visible = visibleNotes.Length > 0;
            noResults.Text = searchQuery.Length == 0 ? "No notes yet." : "No matching notes.";
            noResults.Visible = visibleNotes.Length == 0;
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

        var editor = new NoteEditorForm(note, EditNote, SaveNotes, DeleteNote, ExportNote, ChangeNoteAccent);
        editors.Add(note.Id, editor);
        editor.FormClosing += (_, args) =>
        {
            if (!closingEditors && notes.Contains(note))
                args.Cancel = !RememberWindowPlacements([note], editor);
        };
        editor.FormClosed += (_, _) => editors.Remove(note.Id);
        editor.SetAvailability(storageReady, dirty);
        editor.SetStatus(status.Text);
        editor.Show();
    }

    private void CloseEditors()
    {
        // The list saves the open windows together before reload or exit.
        closingEditors = true;
        try
        {
            foreach (var editor in editors.Values.ToArray()) editor.Close();
        }
        finally
        {
            closingEditors = false;
        }
    }

    private bool RememberWindowPlacements(IEnumerable<Note> candidates, IWin32Window owner)
    {
        var placements = new Dictionary<Guid, NoteWindowPlacement>();
        foreach (var note in candidates)
        {
            if (!editors.TryGetValue(note.Id, out var editor)) continue;
            NoteWindowPlacement placement = editor.GetWindowPlacement();
            if (placement != note.WindowPlacement) placements.Add(note.Id, placement);
        }

        if (placements.Count == 0) return true;

        try
        {
            if (storageReady) store.SaveWindowPlacements(placements);
            foreach (var note in candidates)
            {
                if (placements.TryGetValue(note.Id, out var placement)) note.WindowPlacement = placement;
            }

            return true;
        }
        catch (Exception error) when (IsStorageError(error))
        {
            return MessageBox.Show(owner, $"Could not save note window positions. {error.Message}\n\nContinue without saving the positions?",
                "Window positions", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
    }

    private void AddNote()
    {
        search.Clear();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var note = new Note { CreatedAt = now, UpdatedAt = now };
        notes.Insert(0, note);
        RefreshList(note);
        noteList.TopIndex = 0;
        SetDirty();
        OpenEditor(note);
    }

    private void EditNote(Note note, string title, string content, string? richText)
    {
        if (note.Title == title && note.Content == content && note.RichText == richText) return;
        bool titleChanged = note.Title != title;
        note.Title = title;
        note.Content = content;
        note.RichText = richText;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        note.UpdatedAt = now < note.CreatedAt ? note.CreatedAt : now;
        int index = noteList.Items.IndexOf(note);
        if ((index >= 0) != NoteSearch.Matches(note, searchQuery))
        {
            RefreshList(noteList.SelectedItem as Note);
        }
        else if (titleChanged && index >= 0)
        {
            noteList.Items[index] = note;
        }
        noteList.Invalidate();
        SetDirty();
    }

    private void ChangeNoteAccent(Note note, string accentId)
    {
        if (NotePalette.Resolve(note.Accent).Id == accentId) return;

        note.Accent = accentId == NotePalette.DefaultId ? null : accentId;
        noteList.Invalidate();
        SetDirty();
    }

    private void DeleteNote(NoteEditorForm editor, Note note)
    {
        if (MessageBox.Show(editor, $"Delete “{note.DisplayTitle}”?", "Delete note", MessageBoxButtons.YesNo,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        int index = notes.IndexOf(note);
        notes.RemoveAt(index);
        editor.Close();
        RefreshList(notes.Count == 0 ? null : notes[Math.Min(index, notes.Count - 1)]);
        SetDirty();
    }

    private void SetDirty()
    {
        dirty = true;
        autoSave.Stop();
        if (storageReady) autoSave.Start();
        SetStatus("Unsaved changes");
    }

    private void SetStatus(string message, bool showNotice = false)
    {
        status.Text = message;
        status.Visible = showNotice;
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
        autoSave.Stop();
        if (!dirty) return true;
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
            SetStatus($"Could not save notes. {error.Message}", showNotice: true);
            return false;
        }
    }

    private bool ResolveChanges()
    {
        if (SaveNotes()) return true;

        return MessageBox.Show(this, "The changes could not be saved. Continue without saving?",
            "Unsaved changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
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
