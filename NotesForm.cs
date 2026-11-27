using System.Drawing.Drawing2D;
using System.Text.Json;
using Microsoft.Win32;

namespace NotepadApp;

internal sealed class NotesForm : ResizableNoteWindow
{
    private AppPreferences preferences = new();
    private NotesTheme theme = NotesTheme.Resolve(ThemePreference.Light);
    private readonly Control workspace;
    private readonly NotesSettingsView settings;
    private readonly NoteButton settingsButton = new() { AccessibleName = "Settings", Dock = DockStyle.Fill };
    private readonly NoteButton closeButton = new() { AccessibleName = "Close NotepadApp", Dock = DockStyle.Fill };
    private readonly TableLayoutPanel searchRow = new() { Height = 38, Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(7, 4, 7, 4) };
    private readonly Label searchHint = new() { Text = "Search notes", Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft };
    private readonly Font listHeading = new("Segoe UI", 14, FontStyle.Bold);
    private readonly Font cardTitle = new("Segoe UI", 10, FontStyle.Bold);
    private readonly Font cardDate = new("Segoe UI", 8);
    private readonly ContextMenuStrip cardMenu = new() { ShowImageMargin = false };
    private readonly ToolStripMenuItem openCardAction = new("Open note");
    private readonly ToolStripMenuItem deleteCardAction = new("Delete note");
    private Note? cardMenuNote;
    private readonly NoteStore store;
    private readonly NotePreviewRenderer previewRenderer = new();
    private readonly System.Windows.Forms.Timer autoSave = new() { Interval = 700 };
    private readonly Dictionary<Guid, NoteEditorForm> editors = [];
    private readonly BufferedNoteList noteList = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        IntegralHeight = false,
        DrawMode = DrawMode.OwnerDrawVariable,
        DisplayMember = nameof(Note.DisplayTitle),
        AccessibleName = "Notes"
    };
    private readonly NoteButton add = new() { AccessibleName = "New note", Dock = DockStyle.Fill };
    private readonly NoteButton save = new() { Text = "Save all", AutoSize = true };
    private readonly NoteButton reload = new() { Text = "Reload", AutoSize = true };
    private readonly TextBox search = new()
    {
        Dock = DockStyle.Fill,
        AccessibleName = "Search notes",
        BorderStyle = BorderStyle.None
    };
    private readonly NoteButton clearSearch = new() { Text = "×", AccessibleName = "Clear search", AutoSize = true, Visible = false };
    private readonly Label noResults = new() { Text = "No matching notes.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Visible = false };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 8, 0, 0), Visible = false };
    private List<Note> notes = [];
    private bool dirty;
    private bool storageReady;
    private bool closingEditors;
    private string searchQuery = "";
    private int hoveredCard = -1;
    private bool cardPressed;
    private bool measuringCards;

    public NotesForm(NoteStore store)
    {
        this.store = store;
        Text = "NotepadApp";
        Font = new Font("Segoe UI", 10);

        Size = new Size(420, 570);
        MinimumSize = new Size(320, 360);
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(5, 0, 5, 5);
        workspace = BuildWorkspace();
        settings = new NotesSettingsView(ShowNotes, Close, save, reload,
            mode => ChangePreferences(preferences with { Theme = mode }),
            confirm => ChangePreferences(preferences with { ConfirmBeforeDeleting = confirm }))
        {
            Dock = DockStyle.Fill,
            Visible = false
        };
        Controls.Add(workspace);
        Controls.Add(settings);
        ConfigureCaption(settings.Caption);
        add.SetCenteredIcon(NoteButtonIcons.Add);
        settingsButton.SetCenteredIcon(PaintSettingsIcon);
        closeButton.SetCenteredIcon(NoteButtonIcons.Close);
        clearSearch.SetCenteredIcon(NoteButtonIcons.Close);
        settingsButton.Click += (_, _) => ShowSettings();
        closeButton.Click += (_, _) => Close();
        cardMenu.Items.AddRange([openCardAction, deleteCardAction]);
        openCardAction.Click += (_, _) =>
        {
            if (openCardAction.Tag is Note note && notes.Contains(note)) OpenEditor(note);
        };
        deleteCardAction.Click += (_, _) =>
        {
            if (deleteCardAction.Tag is Note note) DeleteNote(this, note);
        };
        cardMenu.Closed += (_, _) =>
        {
            if (cardMenuNote is not Note note) return;
            cardMenuNote = null;
            if (!noteList.IsDisposed) InvalidateCard(noteList.Items.IndexOf(note));
        };
        LoadPreferences();

        noteList.ClientSizeChanged += (_, _) => UpdateCardHeights();
        noteList.HandleCreated += (_, _) => UpdateCardHeights();
        noteList.DrawItem += DrawNoteCard;
        noteList.MouseMove += (_, args) => SetCardHover(noteList.IndexFromPoint(args.Location), (args.Button & MouseButtons.Left) != 0);
        noteList.MouseDown += (_, args) => SetCardHover(noteList.IndexFromPoint(args.Location), args.Button == MouseButtons.Left);
        noteList.MouseUp += (_, args) => SetCardHover(noteList.IndexFromPoint(args.Location), pressed: false);
        noteList.MouseLeave += (_, _) => SetCardHover(-1, pressed: false);
        noteList.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left) OpenCardAt(args.Location);
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
            SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
            autoSave.Dispose();
            listHeading.Dispose();
            cardTitle.Dispose();
            previewRenderer.Dispose();
            cardMenu.Dispose();
            cardDate.Dispose();
        };
        add.Click += (_, _) => AddNote();
        save.Click += (_, _) =>
        {
            ShowNotes();
            SaveNotes();
        };
        reload.Click += (_, _) =>
        {
            ShowNotes();
            if (ResolveChanges() && RememberWindowPlacements(notes, this)) LoadNotes();
        };
        FormClosing += (_, args) =>
        {
            if (args.CloseReason == CloseReason.UserClosing && Visible && editors.Count > 0)
            {
                args.Cancel = true;
                Hide();
                return;
            }

            args.Cancel = !ResolveChanges() || !RememberWindowPlacements(notes, this);
        };
        FormClosed += (_, _) => CloseEditors();
        SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
        LoadNotes();
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F))
        {
            if (settings.Visible) ShowNotes();
            search.Focus();
            search.SelectAll();
            return true;
        }

        if (keyData == Keys.Escape && settings.Visible)
        {
            ShowNotes();
            return true;
        }

        if (keyData == Keys.Escape && search.Focused && search.TextLength > 0)
        {
            search.Clear();
            return true;
        }

        return base.ProcessCmdKey(ref message, keyData);
    }

    private static void PaintSettingsIcon(Graphics graphics, Color ink)
    {
        const int teeth = 8;
        var outline = new PointF[teeth * 4];

        for (int tooth = 0; tooth < teeth; tooth++)
        {
            float angle = tooth * 45 - 90;
            int point = tooth * 4;
            outline[point] = At(angle - 15, 7);
            outline[point + 1] = At(angle - 10, 9.4f);
            outline[point + 2] = At(angle + 10, 9.4f);
            outline[point + 3] = At(angle + 15, 7);
        }

        using var pen = new Pen(ink, NoteButtonIcons.OutlineWidth) { LineJoin = LineJoin.Round };
        graphics.DrawPolygon(pen, outline);
        const float opening = 3.3f;
        graphics.DrawEllipse(pen, -opening, -opening, opening * 2, opening * 2);

        static PointF At(float degrees, float radius)
        {
            float radians = degrees * MathF.PI / 180;
            return new PointF(MathF.Cos(radians) * radius, MathF.Sin(radians) * radius);
        }
    }


    private Control BuildWorkspace()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var heading = new Label { Text = "Notes", Font = listHeading, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(10, 0, 10, 0) };
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(add, 0, 0);
        var caption = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        header.Controls.Add(caption, 1, 0);
        header.Controls.Add(settingsButton, 2, 0);
        header.Controls.Add(closeButton, 3, 0);
        ConfigureCaption(caption, topResizeGrip: 5);
        foreach (NoteButton button in new[] { add, settingsButton, closeButton })
        {
            button.Margin = Padding.Empty;
            button.FillCell = true;
        }
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(heading, 0, 1);

        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var magnifier = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        magnifier.Paint += (_, args) =>
        {
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(theme.MutedInk, 1.5f);
            int y = Math.Max(0, (magnifier.Height - 14) / 2);
            args.Graphics.DrawEllipse(pen, 4, y, 9, 9);
            args.Graphics.DrawLine(pen, 12, y + 8, 17, y + 13);
        };
        magnifier.Click += (_, _) => search.Focus();
        var searchInput = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(2, 6, 4, 0) };
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
        searchRow.Margin = new Padding(10, 0, 10, 10);
        layout.Controls.Add(searchRow, 0, 2);

        var results = new Panel { Dock = DockStyle.Fill, Margin = new Padding(10, 0, 10, 0) };
        results.Controls.Add(noteList);
        results.Controls.Add(noResults);
        layout.Controls.Add(results, 0, 3);
        status.Margin = new Padding(10, 0, 10, 10);
        layout.Controls.Add(status, 0, 4);
        return layout;
    }

    private void ShowSettings()
    {
        workspace.Hide();
        Padding = new Padding(5);
        settings.Show();
        settings.BringToFront();
        settings.FocusBack();
    }

    private void ShowNotes()
    {
        settings.Hide();
        Padding = new Padding(5, 0, 5, 5);
        workspace.Show();
        settingsButton.Focus();
    }

    private void LoadPreferences()
    {
        try
        {
            preferences = store.LoadPreferences();
        }
        catch (Exception error) when (IsStorageError(error))
        {
            settings.SetNotice($"Could not load settings. {error.Message}");
        }

        ApplyTheme();
    }

    private void ChangePreferences(AppPreferences updated)
    {
        if (preferences == updated) return;
        settings.SetNotice(null);
        try
        {
            store.SavePreferences(updated);
            preferences = updated;
        }
        catch (Exception error) when (IsStorageError(error))
        {
            settings.SetNotice($"Could not save settings. {error.Message}");
        }

        ApplyTheme();
    }

    private void SystemPreferenceChanged(object? sender, UserPreferenceChangedEventArgs args)
    {
        if (!IsHandleCreated || Disposing || IsDisposed) return;
        BeginInvoke(() =>
        {
            if (!IsDisposed && preferences.Theme == ThemePreference.System) ApplyTheme();
        });
    }

    private void ApplyTheme()
    {
        theme = NotesTheme.Resolve(preferences.Theme);
        BackColor = workspace.BackColor = noteList.BackColor = theme.Surface;
        ForeColor = workspace.ForeColor = noteList.ForeColor = search.ForeColor = theme.Ink;
        search.BackColor = searchRow.BackColor = theme.Control;
        searchHint.ForeColor = theme.MutedInk;
        cardMenu.Renderer = new ToolStripProfessionalRenderer(new CardMenuColors(theme)) { RoundedEdges = false };
        cardMenu.BackColor = theme.Surface;
        openCardAction.ForeColor = theme.Surface.GetBrightness() < 0.5f ? theme.Ink : Color.Black;
        deleteCardAction.ForeColor = theme.Surface.GetBrightness() < 0.5f
            ? Color.FromArgb(251, 177, 180)
            : Color.FromArgb(151, 49, 54);
        foreach (NoteButton button in new[] { add, settingsButton, closeButton })
            StyleHeaderButton(button, theme);
        StyleHeaderButton(clearSearch, theme);
        clearSearch.BackColor = theme.Control;
        clearSearch.Padding = new Padding(4, 0, 4, 0);
        searchRow.Invalidate(true);
        noteList.Invalidate();
        settings.Apply(theme, preferences);
        foreach (var editor in editors.Values) editor.ApplyTheme(theme);
    }

    internal static void StyleHeaderButton(NoteButton button, NotesTheme theme)
    {
        StyleButton(button);
        button.BackColor = theme.Surface;
        button.ForeColor = theme.Ink;
        button.FlatAppearance.MouseOverBackColor = theme.HeaderHover;
        button.FlatAppearance.MouseDownBackColor = theme.HeaderPressed;
        button.Padding = Padding.Empty;
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

    private void SetCardHover(int index, bool pressed)
    {
        if (hoveredCard == index && cardPressed == pressed) return;
        int previous = hoveredCard;
        hoveredCard = index;
        cardPressed = pressed;
        InvalidateCard(previous);
        if (index != previous) InvalidateCard(index);
    }

    private void InvalidateCard(int index)
    {
        if (index >= 0 && index < noteList.Items.Count)
            noteList.Invalidate(noteList.GetItemRectangle(index));
    }

    private void OpenCardAt(Point point)
    {
        int index = noteList.IndexFromPoint(point);
        if (index < 0) return;

        var note = (Note)noteList.Items[index];
        Rectangle row = noteList.GetItemRectangle(index);
        using Graphics graphics = noteList.CreateGraphics();
        Rectangle actions = GetCardActionsBounds(row, GetLineHeight(cardDate, graphics));
        if (!actions.Contains(point))
        {
            OpenEditor(note);
            return;
        }

        cardMenuNote = note;
        openCardAction.Tag = deleteCardAction.Tag = note;
        deleteCardAction.Enabled = storageReady;
        InvalidateCard(index);
        cardMenu.Show(noteList, new Point(actions.Right, actions.Bottom), ToolStripDropDownDirection.BelowLeft);
    }

    private static Rectangle GetCardActionsBounds(Rectangle row, int metadataHeight)
    {
        const int rightInset = 14;
        const int topInset = 12;
        int width = Math.Min(row.Width - 2 * rightInset, Math.Max(30, metadataHeight * 2));
        return new Rectangle(row.Right - rightInset - width, row.Top + topInset, width, metadataHeight);
    }

    private sealed class CardMenuColors(NotesTheme theme) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => theme.Surface;
        public override Color MenuBorder => theme.HeaderPressed;
        public override Color MenuItemBorder => theme.HeaderHover;
        public override Color MenuItemSelected => theme.HeaderHover;
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
        Color paper = theme.NotePaper(accent, focused);
        if (hoveredCard == args.Index)
            paper = NoteButtonSurface.Blend(theme.NotePaper(accent, false), theme.NotePaper(accent, true), cardPressed || focused ? 100 : 45);
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
        int metadataHeight = GetLineHeight(cardDate, args.Graphics);
        const TextFormatFlags dateFlags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Right;
        if (hoveredCard == args.Index || ReferenceEquals(cardMenuNote, note))
        {
            TextRenderer.DrawText(args.Graphics, "⋯", Font, GetCardActionsBounds(args.Bounds, metadataHeight),
                theme.MutedInk, TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        else
        {
            TextRenderer.DrawText(args.Graphics, GetCardTimestamp(note.CreatedAt, DateTimeOffset.UtcNow), cardDate,
                new Rectangle(text.Left, text.Top, text.Width, metadataHeight), theme.MutedInk, dateFlags);
        }

        var (title, preview, placeholder) = GetCardText(note);
        int previewTop = text.Top + metadataHeight + 4;
        if (title.Length > 0)
        {
            int titleHeight = GetLineHeight(cardTitle, args.Graphics);
            DrawSearchText(args.Graphics, title,
                new Rectangle(text.Left, previewTop, text.Width, titleHeight), cardTitle, theme.Ink, singleLine: true);
            previewTop += titleHeight;
        }

        if (preview.Length == 0 || previewTop >= text.Bottom) return;
        var previewBounds = Rectangle.FromLTRB(text.Left, previewTop, text.Right, text.Bottom);
        if (placeholder || !previewRenderer.Draw(args.Graphics, previewBounds, note, searchQuery, Font, theme, paper))
            DrawSearchText(args.Graphics, preview, previewBounds, Font, placeholder ? theme.MutedInk : theme.Ink, singleLine: placeholder);
    }

    private const int MaxCardLines = 5;
    private const int CardItemVerticalPadding = 8 + 16 + 4;
    private const int CardItemHorizontalPadding = 4 + 24;

    private static int GetLineHeight(Font font, Graphics graphics) => (int)Math.Ceiling(font.GetHeight(graphics));

    private static string GetCardTimestamp(DateTimeOffset created, DateTimeOffset now)
    {
        TimeSpan age = now - created;
        return created.ToLocalTime().ToString(age >= TimeSpan.Zero && age < TimeSpan.FromHours(24) ? "HH:mm" : "MMM d");
    }

    private (string Title, string Preview, bool Placeholder) GetCardText(Note note)
    {
        string title = NoteSearch.GetSnippet(note.Title, searchQuery, 110);
        string preview = NoteSearch.GetSnippet(note.Content, searchQuery, 1200);
        if (preview.Length == 0 && NoteImage.ContainsPicture(note.RichText)) preview = "Image";
        bool placeholder = title.Length == 0 && preview.Length == 0;
        return (title, placeholder ? "Write a note…" : preview, placeholder);
    }

    private int MeasureCardHeight(Graphics graphics, Note note, int width)
    {
        var (title, preview, placeholder) = GetCardText(note);
        int height = CardItemVerticalPadding + GetLineHeight(cardDate, graphics);
        int availableLines = MaxCardLines;
        if (title.Length > 0)
        {
            height += GetLineHeight(cardTitle, graphics);
            availableLines--;
        }

        if (preview.Length == 0) return height;
        int lineHeight = GetLineHeight(Font, graphics);
        int textWidth = Math.Max(1, width - CardItemHorizontalPadding);
        if (!placeholder)
        {
            int richHeight = previewRenderer.MeasureHeight(graphics, note, searchQuery, Font, textWidth, lineHeight, availableLines);
            if (richHeight > 0) return height + richHeight;

            using var format = new StringFormat(StringFormatFlags.LineLimit) { Trimming = StringTrimming.EllipsisCharacter };
            graphics.MeasureString(preview, Font, new SizeF(textWidth, lineHeight * availableLines), format, out _, out int lines);
            availableLines = Math.Clamp(lines, 1, availableLines);
        }
        else availableLines = 1;

        return height + lineHeight * availableLines;
    }

    private void UpdateCardHeights()
    {
        if (!noteList.IsHandleCreated || noteList.Items.Count == 0 || measuringCards) return;
        measuringCards = true;
        try
        {
            int top = noteList.TopIndex;
            noteList.BeginUpdate();
            try
            {
                using Graphics graphics = noteList.CreateGraphics();
                int width;
                do
                {
                    width = noteList.ClientSize.Width;
                    for (int index = 0; index < noteList.Items.Count; index++)
                        noteList.SetRowHeight(index, MeasureCardHeight(graphics, (Note)noteList.Items[index], width));
                    // A newly visible native scrollbar can reduce the client width.
                } while (noteList.ClientSize.Width < width);

                noteList.TopIndex = top;
            }
            finally { noteList.EndUpdate(); }
        }
        finally { measuringCards = false; }
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

            using var highlight = new SolidBrush(theme.Highlight);
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
        cardMenu.Close();
        int previousTop = resetScroll ? 0 : noteList.TopIndex;
        SetCardHover(-1, pressed: false);
        var visibleNotes = notes.Where(note => NoteSearch.Matches(note, searchQuery))
            .OrderByDescending(note => note.UpdatedAt).ToArray();
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
        UpdateCardHeights();
    }

    private void OpenEditor(Note note)
    {
        if (editors.TryGetValue(note.Id, out var existing))
        {
            if (existing.WindowState == FormWindowState.Minimized) existing.WindowState = FormWindowState.Normal;
            existing.Activate();
            return;
        }

        var editor = new NoteEditorForm(note, theme, EditNote, SaveNotes, DeleteNote, ExportNote, ChangeNoteAccent, AddNote, BringUpNotes);
        editors.Add(note.Id, editor);
        editor.FormClosing += (_, args) =>
        {
            if (!closingEditors && notes.Contains(note))
                args.Cancel = !RememberWindowPlacements([note], editor);
        };
        editor.FormClosed += (_, _) => EditorClosed(note.Id);
        editor.SetAvailability(storageReady, dirty);
        editor.SetStatus(status.Text);
        editor.Show();
    }

    private void EditorClosed(Guid id)
    {
        editors.Remove(id);
        if (closingEditors || Visible || editors.Count > 0) return;

        // Wait until any action that closed the editor, such as deleting its note, has finished.
        BeginInvoke(() =>
        {
            if (IsDisposed || Visible || editors.Count > 0) return;

            if (SaveNotes())
            {
                Close();
            }
            else
            {
                BringUpNotes();
            }
        });
    }

    private void BringUpNotes()
    {
        ShowNotes();
        Show();
        Activate();
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
        else if (index >= 0)
        {
            if (index > 0 && ((Note)noteList.Items[index - 1]).UpdatedAt < note.UpdatedAt)
            {
                RefreshList(noteList.SelectedItem as Note);
            }
            else
            {
                if (titleChanged) noteList.Items[index] = note;
                using Graphics graphics = noteList.CreateGraphics();
                noteList.SetRowHeight(index, MeasureCardHeight(graphics, note, noteList.ClientSize.Width));
                noteList.Invalidate();
            }
        }
        SetDirty();
    }

    private void ChangeNoteAccent(Note note, string accentId)
    {
        if (NotePalette.Resolve(note.Accent).Id == accentId) return;

        note.Accent = accentId == NotePalette.DefaultId ? null : accentId;
        noteList.Invalidate();
        SetDirty();
    }

    private void DeleteNote(IWin32Window owner, Note note)
    {
        int index = notes.IndexOf(note);
        if (index < 0 || !storageReady) return;
        if (preferences.ConfirmBeforeDeleting && MessageBox.Show(owner, $"Delete “{note.DisplayTitle}”?", "Delete note", MessageBoxButtons.YesNo,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        notes.RemoveAt(index);
        if (editors.TryGetValue(note.Id, out var editor)) editor.Close();
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
