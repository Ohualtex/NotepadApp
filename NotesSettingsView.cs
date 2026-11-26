using System.Drawing.Drawing2D;

namespace NotepadApp;

internal sealed class NotesSettingsView : UserControl
{
    private readonly Button back = new() { Text = "←", AccessibleName = "Back to notes", Size = new Size(36, 32) };
    private readonly TableLayoutPanel themeOptions = new() { AutoSize = true, ColumnCount = 3, RowCount = 1, Dock = DockStyle.Top };
    private readonly Dictionary<ThemePreference, RadioButton> themeButtons = [];
    private readonly PreferenceToggle confirmDelete = new()
    {
        AccessibleName = "Confirm before deleting",
        Anchor = AnchorStyles.Right
    };
    private readonly Font headingFont = new("Segoe UI", 14, FontStyle.Bold);
    private readonly Label storage = new() { Text = "Preferences are stored on this device.", AutoSize = true, Margin = new Padding(3, 18, 3, 0) };
    private readonly Label notice = new() { AutoSize = true, Visible = false, MaximumSize = new Size(275, 0) };
    private bool updating;

    public NotesSettingsView(Action goBack, Action<ThemePreference> changeTheme, Action<bool> changeConfirmation)
    {
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(14) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var header = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 14) };
        header.Controls.Add(back);
        header.Controls.Add(new Label { Text = "Settings", Font = headingFont, AutoSize = true, Margin = new Padding(8, 6, 0, 0) });
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(new Label { Text = "Theme", AutoSize = true, Margin = new Padding(3, 0, 3, 6) }, 0, 1);
        foreach (ThemePreference mode in Enum.GetValues<ThemePreference>())
        {
            int index = themeButtons.Count;
            themeOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            var button = new RadioButton
            {
                Text = mode.ToString(),
                Appearance = Appearance.Button,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                AutoSize = true,
                Margin = new Padding(1)
            };
            button.CheckedChanged += (_, _) =>
            {
                if (!updating && button.Checked) changeTheme(mode);
            };
            themeButtons.Add(mode, button);
            themeOptions.Controls.Add(button, index, 0);
        }
        layout.Controls.Add(themeOptions, 0, 2);
        layout.Controls.Add(new Label { Text = "Notes", AutoSize = true, Margin = new Padding(3, 22, 3, 4) }, 0, 3);

        var deleteRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 1, Dock = DockStyle.Top };
        deleteRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        deleteRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        deleteRow.Controls.Add(new Label { Text = "Confirm before deleting", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoSize = true }, 0, 0);
        deleteRow.Controls.Add(confirmDelete, 1, 0);
        layout.Controls.Add(deleteRow, 0, 4);
        layout.Controls.Add(storage, 0, 5);
        layout.Controls.Add(notice, 0, 6);

        back.Click += (_, _) => goBack();
        confirmDelete.CheckedChanged += (_, _) =>
        {
            if (!updating) changeConfirmation(confirmDelete.Checked);
        };

        panel.Controls.Add(layout);
        Controls.Add(panel);
        Disposed += (_, _) => headingFont.Dispose();
    }

    public void FocusBack() => back.Focus();

    public void Apply(NotesTheme theme, AppPreferences preferences)
    {
        updating = true;
        try
        {
            BackColor = theme.Surface;
            ForeColor = theme.Ink;
            storage.ForeColor = theme.MutedInk;
            StyleAction(back, theme);
            foreach (var (mode, button) in themeButtons)
            {
                button.Checked = mode == preferences.Theme;
                StyleAction(button, theme, button.Checked);
            }

            confirmDelete.Checked = preferences.ConfirmBeforeDeleting;
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

    private static void StyleAction(ButtonBase button, NotesTheme theme, bool selected = false)
    {
        NotesForm.StyleButton(button);
        button.BackColor = selected ? theme.Primary : theme.Control;
        button.ForeColor = selected ? NotesTheme.PrimaryInk : theme.Ink;
        button.FlatAppearance.CheckedBackColor = theme.Primary;
        button.FlatAppearance.MouseOverBackColor = selected ? theme.Primary : theme.Hover;
        button.FlatAppearance.MouseDownBackColor = selected ? theme.Primary : theme.Pressed;
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

            if (Focused && ShowFocusCues)
            {
                using var focus = new Pen(theme.MutedInk);
                args.Graphics.DrawRectangle(focus, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}
