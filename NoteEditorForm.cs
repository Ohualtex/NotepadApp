using System.Runtime.InteropServices;

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
        HideSelection = false,
        BorderStyle = BorderStyle.None
    };
    private readonly Label dates = new() { Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.Silver };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.Silver };
    private readonly Button save = new() { Text = "Save all", AutoSize = true };
    private readonly Button options = new() { Text = "Options", AccessibleName = "Note options", AutoSize = true };
    private readonly Panel accentBar = new() { Dock = DockStyle.Fill, Height = 4, Margin = new Padding(3, 0, 3, 8), AccessibleRole = AccessibleRole.None };
    private readonly ContextMenuStrip noteMenu = new();
    private readonly ToolTip formatToolTip = new();
    private readonly Dictionary<FontStyle, CheckBox> styleButtons = [];
    private readonly CheckBox bullets = CreateFormatButton("• List", "Bulleted list");
    private readonly Button insertImage = new() { Text = "Image", AccessibleName = "Insert image", AutoSize = true };
    private readonly FlowLayoutPanel formatTools = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly Button export = new() { Text = "Export .txt", AutoSize = true };

    public NoteEditorForm(Note note, Action<Note, string, string, string?> edit, Func<bool> saveNotes,
        Action<NoteEditorForm, Note> deleteNote, Action<NoteEditorForm, Note> exportNote, Action<Note, string> changeAccent)
    {
        Text = note.DisplayTitle;
        ClientSize = new Size(390, 350);
        MinimumSize = new Size(305, 240);
        RestoreWindowPlacement(note.WindowPlacement);
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(30, 33, 38);
        ForeColor = Color.WhiteSmoke;
        title.BackColor = content.BackColor = Color.FromArgb(41, 45, 54);
        title.ForeColor = content.ForeColor = ForeColor;
        title.Text = note.Title;
        UpdateDates(note);
        Controls.Add(BuildWorkspace());
        // Adding the editor to the form applies its inherited font before RTF is loaded.
        LoadContent(note);
        ActiveControl = note.Title.Length == 0 ? title : content;

        void UpdateNote(string plainText, string? richText)
        {
            edit(note, title.Text, plainText, richText);
            Text = note.DisplayTitle;
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
        content.TextChanged += (_, _) => UpdateContent();
        content.SelectionChanged += (_, _) => UpdateFormatState();
        ConfigureFormatTools(UpdateContent);
        insertImage.Click += (_, _) => ChooseImage(UpdateContent);
        save.Click += (_, _) => saveNotes();
        ConfigureNoteMenu(note, deleteNote, changeAccent);
        options.Click += (_, _) => noteMenu.Show(options, Point.Empty, ToolStripDropDownDirection.AboveRight);
        Disposed += (_, _) =>
        {
            noteMenu.Dispose();
            formatToolTip.Dispose();
        };
        export.Click += (_, _) => exportNote(this, note);
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
            var font = new Font(Font, style);
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
            button.FlatAppearance.CheckedBackColor = Color.FromArgb(74, 87, 115);
            formatToolTip.SetToolTip(button, button.AccessibleName);
        }

        NotesForm.StyleButton(insertImage);
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
        StartPosition = FormStartPosition.WindowsDefaultLocation;
        if (placement is not { Width: > 0, Height: > 0 }) return;

        Rectangle area = Screen.FromPoint(new Point(placement.X, placement.Y)).WorkingArea;
        int width = Math.Clamp(placement.Width, Math.Min(MinimumSize.Width, area.Width), area.Width);
        int height = Math.Clamp(placement.Height, Math.Min(MinimumSize.Height, area.Height), area.Height);
        int left = Math.Clamp(placement.X, area.Left, area.Right - width);
        int top = Math.Clamp(placement.Y, area.Top, area.Bottom - height);
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(left, top, width, height);
    }

    private void ConfigureNoteMenu(Note note, Action<NoteEditorForm, Note> deleteNote, Action<Note, string> changeAccent)
    {
        noteMenu.BackColor = Color.FromArgb(41, 45, 54);
        noteMenu.ForeColor = ForeColor;
        noteMenu.ShowImageMargin = false;
        noteMenu.ShowCheckMargin = true;
        noteMenu.Renderer = new ToolStripProfessionalRenderer(new NoteMenuColors());

        var current = NotePalette.Resolve(note.Accent);
        accentBar.BackColor = current.Color;
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
                accentBar.BackColor = accent.Color;
            };

            noteMenu.Items.Add(item);
        }

        noteMenu.Items.Add(new ToolStripSeparator());
        var delete = new ToolStripMenuItem("Delete note") { ForeColor = ForeColor };
        delete.Click += (_, _) => deleteNote(this, note);
        noteMenu.Items.Add(delete);
    }

    private Control BuildWorkspace()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        actions.Controls.AddRange([save, export, options]);
        foreach (Button button in actions.Controls) NotesForm.StyleButton(button);
        layout.Controls.Add(accentBar, 0, 0);
        layout.Controls.Add(title, 0, 1);
        layout.Controls.Add(content, 0, 2);
        layout.Controls.Add(formatTools, 0, 3);
        layout.Controls.Add(dates, 0, 4);
        layout.Controls.Add(actions, 0, 5);
        layout.Controls.Add(status, 0, 6);
        return layout;
    }

    private void UpdateDates(Note note)
    {
        dates.Text = $"Created {note.CreatedAt.ToLocalTime():g} · Updated {note.UpdatedAt.ToLocalTime():g}";
    }

    public void SetAvailability(bool storageReady, bool dirty)
    {
        title.Enabled = content.Enabled = options.Enabled = formatTools.Enabled = storageReady;
        save.Enabled = storageReady && dirty;
    }

    public void SetStatus(string message) => status.Text = message;

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
