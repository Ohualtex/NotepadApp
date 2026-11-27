using System.Runtime.InteropServices;

namespace NotepadApp;

internal sealed class NoteEditorForm : ResizableNoteWindow
{
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
    private const int HeaderHeight = 40;
    private const int TopResizeGrip = 5;
    private const int HeaderStep = 5;
    private int visibleHeaderHeight;
    private readonly System.Windows.Forms.Timer headerTimer = new() { Interval = 15 };
    private readonly Panel headerViewport = new() { Visible = false, Margin = Padding.Empty };
    private readonly TableLayoutPanel header = new() { ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
    private readonly Label contentHint = new() { Text = "Write a note…", AutoSize = true, Location = new Point(12, 6), Visible = false };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true, Visible = false };
    private readonly Panel writingArea = new() { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(12, 6, 9, 4) };
    private readonly NoteOptionsPanel drawer;
    private readonly NoteButton newNote = new() { AccessibleName = "New note", Dock = DockStyle.Fill };
    private readonly NoteButton options = new() { AccessibleName = "Note options", Dock = DockStyle.Fill };
    private readonly NoteButton close = new() { AccessibleName = "Close note", Dock = DockStyle.Fill };
    private readonly ToolTip formatToolTip = new();
    private readonly Dictionary<FontStyle, CheckBox> styleButtons = [];
    private readonly NoteToggleButton bullets = CreateFormatButton("", "Bulleted list");
    private readonly NoteButton insertImage = new() { AccessibleName = "Insert image" };
    private const int FormatToolWidth = 40;
    private const int FormatToolbarHeight = 38;
    private readonly FlowLayoutPanel formatTools = new() { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };

    public NoteEditorForm(Note note, NotesTheme theme, Action<Note, string, string?> edit, Func<bool> saveNotes,
        Action<NoteEditorForm, Note> deleteNote, Action<NoteEditorForm, Note> exportNote, Action<Note, string> changeAccent,
        Action addNote, Action showNotes)
    {
        this.note = note;
        this.theme = theme;
        Text = note.DisplayTitle;
        ClientSize = new Size(390, 350);
        MinimumSize = new Size(305, 240);
        StartPosition = note.WindowPlacement is { Width: > 0, Height: > 0 }
            ? FormStartPosition.Manual
            : FormStartPosition.WindowsDefaultLocation;
        Load += (_, _) => RestoreWindowPlacement(note.WindowPlacement);
        Font = new Font("Segoe UI", 10);

        drawer = new NoteOptionsPanel(
            accent =>
            {
                changeAccent(note, accent);
                ApplyTheme(this.theme);
            },
            () =>
            {
                CloseDrawer();
                showNotes();
            },
            () =>
            {
                CloseDrawer();
                saveNotes();
            },
            () =>
            {
                CloseDrawer();
                exportNote(this, note);
            },
            () =>
            {
                CloseDrawer();
                deleteNote(this, note);
            })
        {
            Visible = false
        };
        drawer.SetDates(note);
        Controls.Add(BuildWorkspace());
        headerViewport.Controls.Add(header);
        Controls.Add(headerViewport);
        headerViewport.BringToFront();
        Resize += (_, _) => PositionHeader();
        headerTimer.Tick += (_, _) => AdvanceHeader();
        PositionHeader();
        // Adding the editor to the form applies its inherited font before RTF is loaded.
        LoadContent(note);
        ApplyTheme(theme);
        SetChromeVisible(false);
        ActiveControl = content;
        Shown += (_, _) => content.Focus();
        Activated += (_, _) => SetChromeVisible(true);
        Deactivate += (_, _) => SetChromeVisible(false);
        contentHint.Click += (_, _) => content.Focus();

        void UpdateNote(string plainText, string? richText)
        {
            edit(note, plainText, richText);
            Text = note.DisplayTitle;
            drawer.SetDates(note);
        }

        void UpdateContent()
        {
            string? richText = content.Rtf;
            bool hasImage = NoteImage.ContainsPicture(richText);
            string plainText = NoteRichContent.PlainText(content, hasImage);
            UpdateNote(plainText, plainText.Length > 0 || hasImage ? richText : null);
        }

        content.TextChanged += (_, _) =>
        {
            UpdateContentHint();
            if (!changingAppearance) UpdateContent();
        };
        content.SelectionChanged += (_, _) => UpdateFormatState();
        ConfigureFormatTools(UpdateContent);
        insertImage.Click += (_, _) => ChooseImage(UpdateContent);
        options.Click += (_, _) => ToggleDrawer();
        content.MouseDown += (_, _) => CloseDrawer();
        contentHint.MouseDown += (_, _) => CloseDrawer();
        writingArea.Resize += (_, _) =>
        {
            if (drawer.Visible) drawer.FitTo(writingArea.ClientSize);
        };
        newNote.Click += (_, _) => addNote();
        close.Click += (_, _) => Close();
        ApplyTheme(theme);
        Disposed += (_, _) =>
        {
            headerTimer.Dispose();
            formatToolTip.Dispose();
        };
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Escape && drawer.Visible)
        {
            CloseDrawer();
            return true;
        }

        return base.ProcessCmdKey(ref message, keyData);
    }

    private void ToggleDrawer()
    {
        if (drawer.Visible)
        {
            CloseDrawer();
            return;
        }

        drawer.FitTo(writingArea.ClientSize);
        drawer.Show();
        drawer.BringToFront();
        drawer.FocusSelection();
    }

    private void CloseDrawer(bool restoreFocus = true)
    {
        if (!drawer.Visible) return;
        drawer.Hide();
        if (restoreFocus) content.Focus();
    }

    private void SetChromeVisible(bool visible)
    {
        chromeVisible = visible;
        formatTools.Visible = visible;
        if (!visible) CloseDrawer(restoreFocus: false);
        if (visible) headerViewport.Show();
        if (visibleHeaderHeight != (visible ? HeaderHeight : 0)) headerTimer.Start();
        UpdateContentHint();
        PaintChrome();
    }

    private void AdvanceHeader()
    {
        int target = chromeVisible ? HeaderHeight : 0;
        int distance = target - visibleHeaderHeight;
        visibleHeaderHeight += Math.Clamp(distance, -HeaderStep, HeaderStep);
        PositionHeader();
        if (visibleHeaderHeight == target)
        {
            headerTimer.Stop();
            if (!chromeVisible) headerViewport.Hide();
        }
    }

    private void PositionHeader()
    {
        headerViewport.Bounds = new Rectangle(0, 0, ClientSize.Width, HeaderHeight);
        header.Bounds = new Rectangle(0, visibleHeaderHeight - HeaderHeight, headerViewport.Width, HeaderHeight);
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
        status.ForeColor = contentHint.ForeColor = theme.MutedInk;

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

        drawer.Apply(theme, note.Accent);
        PaintChrome();
    }

    private void PaintChrome()
    {
        headerViewport.BackColor = paper;
        header.BackColor = chrome;
        formatTools.BackColor = chromeVisible ? chrome : paper;
        Color contrast = chrome.GetBrightness() < 0.5f ? Color.White : Color.Black;
        Color hover = NoteButtonSurface.Blend(chrome, contrast, 10);
        Color pressed = NoteButtonSurface.Blend(chrome, contrast, 17);
        Color selected = NoteButtonSurface.Blend(chrome, contrast, 24);
        foreach (ButtonBase button in formatTools.Controls)
        {
            button.BackColor = chrome;
            button.ForeColor = theme.Ink;
            button.FlatAppearance.MouseOverBackColor = hover;
            button.FlatAppearance.MouseDownBackColor = pressed;
            button.FlatAppearance.CheckedBackColor = selected;
        }

        foreach (NoteButton button in new[] { newNote, options, close })
        {
            button.BackColor = chrome;
            button.ForeColor = theme.Ink;
            button.FlatAppearance.MouseOverBackColor = hover;
            button.FlatAppearance.MouseDownBackColor = pressed;
        }
    }

    private void LoadContent(Note note)
    {
        if (!NoteRichContent.TryLoad(content, note, note.RichText)) content.Text = note.Content;
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

    private static NoteToggleButton CreateFormatButton(string label, string accessibleName) => new()
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
        foreach (NoteToggleButton button in formatTools.Controls)
        {
            ConfigureToolCell(button);
            button.FillCell = true;
        }

        bullets.SetCenteredIcon((graphics, ink) =>
        {
            using var brush = new SolidBrush(ink);
            using var pen = new Pen(ink, 1.2f);
            for (int y = -5; y <= 5; y += 5)
            {
                graphics.FillEllipse(brush, -8, y - 1, 2, 2);
                graphics.DrawLine(pen, -3, y, 8, y);
            }
        });

        ConfigureToolCell(insertImage);
        insertImage.FillCell = true;
        insertImage.SetCenteredIcon((graphics, ink) =>
        {
            using var pen = new Pen(ink, 1.2f);
            graphics.DrawRectangle(pen, -8, -6, 16, 12);
            graphics.DrawEllipse(pen, 2, -4, 2, 2);
            graphics.DrawLines(pen,
            [
                new Point(-7, 4), new Point(-3, -1), new Point(1, 3), new Point(4, 0), new Point(7, 4)
            ]);
        });
        formatTools.Controls.Add(insertImage);
        UpdateFormatState();
    }

    private void ConfigureToolCell(ButtonBase button)
    {
        NotesForm.StyleButton(button);
        button.AutoSize = false;
        button.Size = new Size(FormatToolWidth, FormatToolbarHeight);
        button.Margin = Padding.Empty;
        button.Padding = Padding.Empty;
        formatToolTip.SetToolTip(button, button.AccessibleName);
    }

    private void UpdateFormatState()
    {
        FontStyle activeStyles = RichTextFormatting.GetActiveStyles(content);
        foreach (var (style, button) in styleButtons)
            button.Checked = (activeStyles & style) != 0;

        bullets.Checked = content.SelectionBullet;
    }

    private Control BuildWorkspace()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderHeight - TopResizeGrip));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, FormatToolbarHeight));

        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, HeaderHeight));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, HeaderHeight));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, HeaderHeight));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var caption = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        ConfigureCaption(caption, topResizeGrip: TopResizeGrip);
        newNote.SetCenteredIcon(NoteButtonIcons.Add);
        options.SetCenteredIcon(NoteButtonIcons.More);
        close.SetCenteredIcon(NoteButtonIcons.Close);
        header.Controls.Add(newNote, 0, 0);
        header.Controls.Add(caption, 1, 0);
        header.Controls.Add(options, 2, 0);
        header.Controls.Add(close, 3, 0);
        foreach (NoteButton button in new[] { newNote, options, close })
        {
            NotesForm.StyleButton(button);
            button.Margin = Padding.Empty;
            button.Padding = Padding.Empty;
            button.FillCell = true;
        }


        content.Margin = Padding.Empty;
        writingArea.Controls.Add(content);
        writingArea.Controls.Add(contentHint);
        contentHint.BringToFront();
        writingArea.Controls.Add(drawer);
        layout.Controls.Add(writingArea, 0, 1);
        layout.Controls.Add(status, 0, 2);
        layout.Controls.Add(formatTools, 0, 3);
        return layout;
    }

    public void SetAvailability(bool storageReady, bool dirty)
    {
        content.Enabled = newNote.Enabled = formatTools.Enabled = storageReady;
        drawer.SetAvailability(storageReady, dirty);
    }

    public void SetStatus(string message)
    {
        status.Text = message;
        status.Visible = message.StartsWith("Could not ", StringComparison.Ordinal);
    }
}
