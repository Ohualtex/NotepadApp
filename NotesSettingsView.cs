using System.Drawing.Drawing2D;

namespace NotepadApp;

internal sealed class NotesSettingsView : UserControl
{
    private readonly NoteButton back = new() { AccessibleName = "Back to notes", Dock = DockStyle.Fill };
    private readonly NoteButton close = new() { AccessibleName = "Close NotepadApp", Dock = DockStyle.Fill };
    private readonly Label heading = new() { Text = "Settings", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(7, 0, 0, 0) };
    private readonly Button export;
    private readonly List<Panel> sectionDividers = [];
    private readonly TableLayoutPanel themeOptions = new() { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Top, Margin = Padding.Empty };
    private readonly Dictionary<ThemePreference, NoteRadioButton> themeButtons = [];
    private readonly PreferenceToggle confirmDelete = new()
    {
        AccessibleName = "Confirm before deleting",
        Margin = Padding.Empty
    };
    private readonly Label confirmDeleteState = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 0, 0, 0) };
    private readonly Font headingFont = new("Segoe UI", 14, FontStyle.Bold);
    private readonly Font sectionFont = new("Segoe UI", 12);
    private readonly Label notice = new() { AutoSize = true, Visible = false, MaximumSize = new Size(275, 0) };
    private bool updating;

    public NotesSettingsView(Action goBack, Action closeApplication, Button export,
        Action<ThemePreference> changeTheme, Action<bool> changeConfirmation)
    {
        this.export = export;
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(14) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var header = new TableLayoutPanel { Height = 48, Dock = DockStyle.Top, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heading.Font = headingFont;
        back.SetCenteredIcon(NoteButtonIcons.Back);
        close.SetCenteredIcon(NoteButtonIcons.Close);
        back.Margin = close.Margin = Padding.Empty;
        back.FillCell = close.FillCell = true;
        header.Controls.Add(back, 0, 0);
        header.Controls.Add(heading, 1, 0);
        header.Controls.Add(close, 2, 0);
        int row = 0;
        void AddRow(Control control) => layout.Controls.Add(control, 0, row++);

        AddRow(SectionTitle("General"));
        AddRow(new Label { Text = "Confirm before deleting", AutoSize = true, Margin = new Padding(0, 0, 0, 6) });
        var confirmation = new TableLayoutPanel
        {
            AutoSize = true, ColumnCount = 2, RowCount = 1, Dock = DockStyle.Top, Margin = Padding.Empty
        };
        confirmation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        confirmation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        confirmation.Controls.Add(confirmDelete, 0, 0);
        confirmation.Controls.Add(confirmDeleteState, 1, 0);
        AddRow(confirmation);

        AddRow(SectionDivider());
        AddRow(SectionTitle("Color"));
        themeOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var themeModes = Enum.GetValues<ThemePreference>();
        themeOptions.RowCount = themeModes.Length;
        foreach (ThemePreference mode in themeModes)
        {
            int index = themeButtons.Count;
            themeOptions.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            var button = new NoteRadioButton
            {
                Text = mode == ThemePreference.System ? "Use Windows theme" : mode.ToString(),
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
            button.CheckedChanged += (_, _) =>
            {
                if (!updating && button.Checked) changeTheme(mode);
            };
            themeButtons.Add(mode, button);
            themeOptions.Controls.Add(button, 0, index);
        }
        AddRow(themeOptions);
        AddRow(SectionDivider());
        AddRow(SectionTitle("Other"));
        export.Anchor = AnchorStyles.Left;
        export.Margin = Padding.Empty;
        AddRow(export);
        AddRow(notice);

        back.Click += (_, _) => goBack();
        close.Click += (_, _) => closeApplication();
        confirmDelete.CheckedChanged += (_, _) =>
        {
            confirmDeleteState.Text = confirmDelete.Checked ? "On" : "Off";
            if (!updating) changeConfirmation(confirmDelete.Checked);
        };

        panel.Controls.Add(layout);
        Controls.Add(panel);
        Controls.Add(header);
        Disposed += (_, _) =>
        {
            headingFont.Dispose();
            sectionFont.Dispose();
        };
    }

    public Control Caption => heading;

    public void FocusBack() => back.Focus();

    public void Apply(NotesTheme theme, AppPreferences preferences)
    {
        updating = true;
        try
        {
            BackColor = theme.Surface;
            ForeColor = theme.Ink;
            NotesForm.StyleHeaderButton(back, theme);
            NotesForm.StyleHeaderButton(close, theme);
            StyleAction(export, theme);
            foreach (Panel divider in sectionDividers)
                divider.BackColor = NoteButtonSurface.Blend(theme.Surface, theme.MutedInk, 28);
            foreach (var (mode, button) in themeButtons)
            {
                button.Checked = mode == preferences.Theme;
                button.ApplyTheme(theme);
            }

            confirmDelete.Checked = preferences.ConfirmBeforeDeleting;
            confirmDeleteState.Text = confirmDelete.Checked ? "On" : "Off";
            confirmDelete.ApplyTheme(theme);
        }
        finally
        {
            updating = false;
        }
    }

    public void SetNotice(string? message)
    {
        notice.Text = message;
        notice.Visible = message is not null;
    }

    private Label SectionTitle(string text) => new()
    {
        Text = text, Font = sectionFont, AutoSize = true, Margin = new Padding(0, 0, 0, 10)
    };

    private Panel SectionDivider()
    {
        var divider = new Panel { Height = 1, Dock = DockStyle.Top, Margin = new Padding(0, 14, 0, 12) };
        sectionDividers.Add(divider);
        return divider;
    }

    private static void StyleAction(ButtonBase button, NotesTheme theme)
    {
        NotesForm.StyleButton(button);
        button.BackColor = theme.Control;
        button.ForeColor = theme.Ink;
        button.FlatAppearance.MouseOverBackColor = theme.Hover;
        button.FlatAppearance.MouseDownBackColor = theme.Pressed;
    }

    private sealed class PreferenceToggle : CheckBox
    {
        private NotesTheme currentTheme = NotesTheme.Resolve(ThemePreference.Light);

        public PreferenceToggle()
        {
            Size = new Size(48, 26);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public void ApplyTheme(NotesTheme theme)
        {
            BackColor = theme.Surface;
            ForeColor = theme.Ink;
            currentTheme = theme;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            NotesTheme theme = currentTheme;
            args.Graphics.Clear(BackColor);
            args.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var track = Rectangle.Inflate(ClientRectangle, -2, -3);
            int diameter = track.Height;
            using var fill = new SolidBrush(Checked ? theme.Primary : theme.Control);
            args.Graphics.FillEllipse(fill, track.Left, track.Top, diameter, diameter);
            args.Graphics.FillRectangle(fill, track.Left + diameter / 2, track.Top, track.Width - diameter, diameter);
            args.Graphics.FillEllipse(fill, track.Right - diameter, track.Top, diameter, diameter);

            int knob = diameter - 6;
            using var pin = new SolidBrush(Checked ? NotesTheme.PrimaryInk : theme.MutedInk);
            int x = Checked ? track.Right - knob - 3 : track.Left + 3;
            args.Graphics.FillEllipse(pin, x, track.Top + 3, knob, knob);

            if (Focused && ShowFocusCues) NoteButtonSurface.DrawFocusMarker(args.Graphics, ClientSize, theme.MutedInk);
        }
    }
}
