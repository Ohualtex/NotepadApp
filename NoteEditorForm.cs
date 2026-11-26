using System.Runtime.InteropServices;

namespace NotepadApp;

internal sealed class NoteEditorForm : ResizableNoteWindow
{
    private readonly TextBox title = new()
    {
        Dock = DockStyle.Fill,
        AccessibleName = "Note title",
        BorderStyle = BorderStyle.None,
        Visible = false
    };
    private readonly RichTextBox content = new()
    {
        Dock = DockStyle.Fill,
        AccessibleName = "Note content",
        DetectUrls = false,
        HideSelection = false,
        BorderStyle = BorderStyle.None
    };
    private readonly Note note;
    private NotesTheme theme;
    private bool changingAppearance;
    private bool chromeVisible;
    private Color paper;
    private Color chrome;
    private readonly Label titleCaption = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
    private readonly TableLayoutPanel header = new() { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
    private readonly Label contentHint = new() { Text = "Write a note…", AutoSize = true, Location = new Point(12, 6), Visible = false };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true, Visible = false };
    private readonly ToolStripMenuItem dates = new() { Enabled = false };
    private readonly ToolStripMenuItem save = new("Save all");
    private readonly Button newNote = new() { Text = "+", AccessibleName = "New note", Dock = DockStyle.Fill };
    private readonly Button options = new() { Text = "⋯", AccessibleName = "Note options", Dock = DockStyle.Fill };
    private readonly Button close = new() { Text = "×", AccessibleName = "Close note", Dock = DockStyle.Fill };
    private readonly ContextMenuStrip noteMenu = new();
    private readonly ToolTip formatToolTip = new();
    private readonly Dictionary<FontStyle, CheckBox> styleButtons = [];
    private readonly CheckBox bullets = CreateFormatButton("", "Bulleted list");
    private readonly Button insertImage = new() { AccessibleName = "Insert image" };
    private readonly FlowLayoutPanel formatTools = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly ToolStripMenuItem export = new("Export .txt");

    public NoteEditorForm(Note note, NotesTheme theme, Action<Note, string, string, string?> edit, Func<bool> saveNotes,
        Action<NoteEditorForm, Note> deleteNote, Action<NoteEditorForm, Note> exportNote, Action<Note, string> changeAccent,
        Action addNote, Action showNotes)
    {
        this.note = note;
        this.theme = theme;
        Text = note.DisplayTitle;
        titleCaption.Text = note.Title;
        ClientSize = new Size(390, 350);
        MinimumSize = new Size(305, 240);
        StartPosition = note.WindowPlacement is { Width: > 0, Height: > 0 }
            ? FormStartPosition.Manual
            : FormStartPosition.WindowsDefaultLocation;
        Load += (_, _) => RestoreWindowPlacement(note.WindowPlacement);
        Font = new Font("Segoe UI", 10);

        title.Text = note.Title;
        UpdateDates(note);
        Controls.Add(BuildWorkspace());
        // Adding the editor to the form applies its inherited font before RTF is loaded.
        LoadContent(note);
        ApplyTheme(theme);
        SetChromeVisible(false);
        ActiveControl = content;
        Shown += (_, _) => content.Focus();
        Activated += (_, _) => SetChromeVisible(true);
        Deactivate += (_, _) => SetChromeVisible(false);
        ConfigureCaption(titleCaption, BeginTitleEdit);
        contentHint.Click += (_, _) => content.Focus();
        title.LostFocus += (_, _) => EndTitleEdit();
        title.KeyDown += (_, args) =>
        {
            if (args.KeyCode != Keys.Enter && args.KeyCode != Keys.Escape) return;
            args.SuppressKeyPress = true;
            content.Focus();
        };

        void UpdateNote(string plainText, string? richText)
        {
            edit(note, title.Text, plainText, richText);
            Text = note.DisplayTitle;
            titleCaption.Text = note.Title;
            UpdateDates(note);
        }

        void UpdateContent()
        {
            string? richText = content.Rtf;
            bool hasImage = NoteImage.ContainsPicture(richText);
            string plainText = GetPlainContent(hasImage);
            UpdateNote(plainText, plainText.Length > 0 || hasImage ? richText : null);
        }

        title.TextChanged += (_, _) => UpdateNote(note.Content, note.RichText);
        content.TextChanged += (_, _) =>
        {
            UpdateContentHint();
            if (!changingAppearance) UpdateContent();
        };
        content.SelectionChanged += (_, _) => UpdateFormatState();
        ConfigureFormatTools(UpdateContent);
        insertImage.Click += (_, _) => ChooseImage(UpdateContent);
        save.Click += (_, _) => saveNotes();
        ConfigureNoteMenu(deleteNote, changeAccent, showNotes);
        options.Click += (_, _) => noteMenu.Show(options, new Point(0, options.Height), ToolStripDropDownDirection.BelowLeft);
        newNote.Click += (_, _) => addNote();
        close.Click += (_, _) => Close();
        ApplyTheme(theme);
        Disposed += (_, _) =>
        {
            noteMenu.Dispose();
            formatToolTip.Dispose();
        };
        export.Click += (_, _) => exportNote(this, note);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.F2 && chromeVisible && title.Enabled)
        {
            BeginTitleEdit();
            return true;
        }

        return base.ProcessCmdKey(ref message, keyData);
    }

    private void BeginTitleEdit()
    {
        if (!title.Enabled) return;
        titleCaption.Hide();
        title.Show();
        title.Focus();
        title.SelectAll();
    }

    private void EndTitleEdit()
    {
        title.Hide();
        titleCaption.Show();
    }

    private void SetChromeVisible(bool visible)
    {
        chromeVisible = visible;
        header.Visible = formatTools.Visible = visible;
        if (!visible) EndTitleEdit();
        UpdateContentHint();
        PaintChrome();
    }

    private void UpdateContentHint() => contentHint.Visible = chromeVisible && content.TextLength == 0;

    public void ApplyTheme(NotesTheme updatedTheme)
    {
        bool inkChanged = content.ForeColor != updatedTheme.Ink;
        theme = updatedTheme;
        Color accent = NotePalette.Resolve(note.Accent).Color;
        paper = theme.NotePaper(accent, selected: false);
        chrome = theme.NotePaper(accent, selected: true);
        BackColor = content.BackColor = paper;
        title.ForeColor = titleCaption.ForeColor = theme.Ink;
        status.ForeColor = contentHint.ForeColor = theme.MutedInk;
        title.BackColor = chrome;

        if (inkChanged)
        {
            changingAppearance = true;
            try
            {
                int start = content.SelectionStart;
                int length = content.SelectionLength;
                content.ForeColor = theme.Ink;
                content.SelectAll();
                content.SelectionColor = theme.Ink;
                content.Select(start, length);
            }
            finally
            {
                changingAppearance = false;
            }
        }

        PaintChrome();
    }

    private void PaintChrome()
    {
        header.BackColor = formatTools.BackColor = chromeVisible ? chrome : paper;
        foreach (ButtonBase button in formatTools.Controls)
        {
            button.BackColor = chrome;
            button.ForeColor = theme.Ink;
            button.FlatAppearance.MouseOverBackColor = theme.Hover;
            button.FlatAppearance.MouseDownBackColor = theme.Pressed;
            button.FlatAppearance.CheckedBackColor = theme.Control;
        }

        foreach (Button button in new[] { newNote, options, close })
        {
            button.BackColor = chrome;
            button.ForeColor = theme.Ink;
            button.FlatAppearance.MouseOverBackColor = theme.Hover;
            button.FlatAppearance.MouseDownBackColor = theme.Pressed;
        }
    }

    private void LoadContent(Note note)
    {
        if (!string.IsNullOrEmpty(note.RichText))
        {
            try
            {
                content.Rtf = note.RichText;
                string plainText = GetPlainContent(NoteImage.ContainsPicture(note.RichText));
                if (plainText.ReplaceLineEndings("\n") == note.Content.ReplaceLineEndings("\n")) return;
            }
            catch (ArgumentException)
            {
                // Plain content is still available if the optional RTF is invalid.
            }
        }

        content.Text = note.Content;
    }

    private string GetPlainContent(bool hasImage)
    {
        // Rich Edit represents each inline image as a space when it returns plain text.
        string text = content.Text;
        return hasImage && string.IsNullOrWhiteSpace(text) ? "" : text;
    }

    private void ChooseImage(Action updateContent)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Insert image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var file = dialog.OpenFile();
            InsertImage(file);
            updateContent();
            UpdateFormatState();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or ExternalException or OutOfMemoryException)
        {
            SetStatus($"Could not insert image. {error.Message}");
        }
    }

    private void InsertImage(Stream image)
    {
        var available = new Size(Math.Max(1, content.ClientSize.Width - 16), Math.Max(1, content.ClientSize.Height - 16));
        string picture = NoteImage.CreateRtf(image, available, content.DeviceDpi);
        content.Focus();
        content.SelectedRtf = picture;
    }

    private static CheckBox CreateFormatButton(string label, string accessibleName) => new()
    {
        Text = label,
        AccessibleName = accessibleName,
        Appearance = Appearance.Button,
        AutoCheck = false,
        AutoSize = true,
        TextAlign = ContentAlignment.MiddleCenter
    };

    private void ConfigureFormatTools(Action updateContent)
    {
        var styles = new[] {
            (FontStyle.Bold, "B", "Bold"), (FontStyle.Italic, "I", "Italic"),
            (FontStyle.Underline, "U", "Underline"), (FontStyle.Strikeout, "S", "Strikethrough")
        };

        foreach (var (style, label, description) in styles)
        {
            var button = CreateFormatButton(label, description);
            var font = new Font(Font.FontFamily, 11, style);
            button.Font = font;
            button.Disposed += (_, _) => font.Dispose();
            button.Click += (_, _) =>
            {
                content.Focus();
                RichTextFormatting.ToggleStyle(content, style);
                updateContent();
                UpdateFormatState();
            };
            styleButtons.Add(style, button);
            formatTools.Controls.Add(button);
        }

        bullets.Click += (_, _) =>
        {
            content.Focus();
            content.SelectionBullet = !content.SelectionBullet;
            updateContent();
            UpdateFormatState();
        };
        formatTools.Controls.Add(bullets);
        foreach (CheckBox button in formatTools.Controls)
        {
            NotesForm.StyleButton(button);
            button.AutoSize = false;
            button.Size = new Size(36, 30);
            button.Padding = Padding.Empty;
            formatToolTip.SetToolTip(button, button.AccessibleName);
        }

        bullets.Paint += (_, args) =>
        {
            using var brush = new SolidBrush(bullets.ForeColor);
            using var pen = new Pen(bullets.ForeColor);
            int x = bullets.Width / 2 - 8;
            int y = bullets.Height / 2 - 5;
            for (int row = 0; row < 3; row++)
            {
                args.Graphics.FillEllipse(brush, x, y + row * 5, 2, 2);
                args.Graphics.DrawLine(pen, x + 5, y + row * 5 + 1, x + 16, y + row * 5 + 1);
            }
        };

        NotesForm.StyleButton(insertImage);
        insertImage.Size = new Size(36, 30);
        insertImage.Padding = Padding.Empty;
        insertImage.Paint += (_, args) =>
        {
            using var pen = new Pen(insertImage.ForeColor);
            int x = insertImage.Width / 2 - 8;
            int y = insertImage.Height / 2 - 6;
            args.Graphics.DrawRectangle(pen, x, y, 16, 12);
            args.Graphics.DrawEllipse(pen, x + 10, y + 2, 2, 2);
            args.Graphics.DrawLines(pen,
            [
                new Point(x + 1, y + 10), new Point(x + 5, y + 5), new Point(x + 9, y + 9),
                new Point(x + 12, y + 6), new Point(x + 15, y + 10)
            ]);
        };
        formatToolTip.SetToolTip(insertImage, "Insert image");
        formatTools.Controls.Add(insertImage);
        UpdateFormatState();
    }

    private void UpdateFormatState()
    {
        FontStyle activeStyles = RichTextFormatting.GetActiveStyles(content);
        foreach (var (style, button) in styleButtons)
            button.Checked = (activeStyles & style) != 0;

        bullets.Checked = content.SelectionBullet;
    }

    public NoteWindowPlacement GetWindowPlacement()
    {
        Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        return new NoteWindowPlacement(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private void RestoreWindowPlacement(NoteWindowPlacement? placement)
    {
        if (placement is not { Width: > 0, Height: > 0 }) return;

        Rectangle area = Screen.FromPoint(new Point(placement.X, placement.Y)).WorkingArea;
        int width = Math.Clamp(placement.Width, Math.Min(MinimumSize.Width, area.Width), area.Width);
        int height = Math.Clamp(placement.Height, Math.Min(MinimumSize.Height, area.Height), area.Height);
        int left = Math.Clamp(placement.X, area.Left, area.Right - width);
        int top = Math.Clamp(placement.Y, area.Top, area.Bottom - height);
        Bounds = new Rectangle(left, top, width, height);
    }

    private void ConfigureNoteMenu(Action<NoteEditorForm, Note> deleteNote, Action<Note, string> changeAccent, Action showNotes)
    {
        noteMenu.BackColor = Color.FromArgb(41, 45, 54);
        noteMenu.ForeColor = ForeColor;
        noteMenu.ShowImageMargin = false;
        noteMenu.ShowCheckMargin = true;
        noteMenu.Renderer = new ToolStripProfessionalRenderer(new NoteMenuColors());

        var current = NotePalette.Resolve(note.Accent);
        ToolStripMenuItem? selected = null;
        foreach (var accent in NotePalette.Colors)
        {
            var item = new ToolStripMenuItem(accent.Label) { ForeColor = accent.Color, Checked = accent.Id == current.Id };
            if (item.Checked) selected = item;
            item.Click += (_, _) =>
            {
                if (selected is not null) selected.Checked = false;
                item.Checked = true;
                selected = item;
                changeAccent(note, accent.Id);
                ApplyTheme(theme);
            };

            noteMenu.Items.Add(item);
        }

        noteMenu.Items.Add(new ToolStripSeparator());
        var delete = new ToolStripMenuItem("Delete note") { ForeColor = Color.WhiteSmoke };
        delete.Click += (_, _) => deleteNote(this, note);
        var notesList = new ToolStripMenuItem("Notes list") { ForeColor = Color.WhiteSmoke };
        notesList.Click += (_, _) => showNotes();
        save.ForeColor = export.ForeColor = delete.ForeColor = Color.WhiteSmoke;
        noteMenu.Items.AddRange([notesList, save, export, delete, new ToolStripSeparator(), dates]);
    }

    private Control BuildWorkspace()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var titleArea = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 8, 4, 0), Margin = Padding.Empty };
        titleArea.Controls.Add(titleCaption);
        titleArea.Controls.Add(title);
        header.Controls.Add(newNote, 0, 0);
        header.Controls.Add(titleArea, 1, 0);
        header.Controls.Add(options, 2, 0);
        header.Controls.Add(close, 3, 0);
        foreach (Button button in new[] { newNote, options, close })
        {
            NotesForm.StyleButton(button);
            button.Margin = Padding.Empty;
            button.Padding = Padding.Empty;
        }

        formatTools.Margin = Padding.Empty;
        formatTools.WrapContents = false;
        layout.Controls.Add(header, 0, 0);
        var writingArea = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(12, 6, 9, 4) };
        content.Margin = Padding.Empty;
        writingArea.Controls.Add(content);
        writingArea.Controls.Add(contentHint);
        contentHint.BringToFront();
        layout.Controls.Add(writingArea, 0, 1);
        layout.Controls.Add(status, 0, 2);
        layout.Controls.Add(formatTools, 0, 3);
        return layout;
    }

    private void UpdateDates(Note note)
    {
        dates.Text = $"Created {note.CreatedAt.ToLocalTime():g} · Updated {note.UpdatedAt.ToLocalTime():g}";
    }

    public void SetAvailability(bool storageReady, bool dirty)
    {
        title.Enabled = content.Enabled = newNote.Enabled = formatTools.Enabled = storageReady;
        save.Enabled = storageReady && dirty;
    }

    public void SetStatus(string message)
    {
        status.Text = message;
        status.Visible = message.StartsWith("Could not ", StringComparison.Ordinal);
    }

    private sealed class NoteMenuColors : ProfessionalColorTable
    {
        private static readonly Color Surface = Color.FromArgb(41, 45, 54);
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuItemSelected => Color.FromArgb(58, 65, 78);
        public override Color MenuItemBorder => MenuItemSelected;
        public override Color MenuBorder => Color.FromArgb(83, 91, 109);
        public override Color CheckBackground => MenuBorder;
    }
}
