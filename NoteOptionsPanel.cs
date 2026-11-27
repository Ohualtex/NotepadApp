using System.Drawing.Drawing2D;

namespace NotepadApp;

internal sealed class NoteOptionsPanel : UserControl
{
    private const int PaletteHeight = 46;
    private readonly TableLayoutPanel rows = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
    private readonly Dictionary<string, ColorChoice> choices = [];
    private readonly Button notesList = CreateAction("Notes list");
    private readonly Button export = CreateAction("Export .txt");
    private readonly Button delete = CreateAction("Delete note");
    private readonly Label dates = new() { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(14, 6, 14, 8) };
    private string selectedAccent = NotePalette.DefaultId;
    private bool updatingSelection;

    public NoteOptionsPanel(Action<string> changeAccent, Action showNotes, Action exportNote, Action deleteNote)
    {
        var actions = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var palette = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = PaletteHeight,
            RowCount = 1,
            ColumnCount = NotePalette.Colors.Count,
            Margin = Padding.Empty
        };
        palette.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (var accent in NotePalette.Colors)
        {
            int index = choices.Count;
            palette.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / palette.ColumnCount));
            var choice = new ColorChoice(accent.Label, accent.Color);
            choice.CheckedChanged += (_, _) =>
            {
                if (updatingSelection || !choice.Checked) return;
                selectedAccent = accent.Id;
                changeAccent(accent.Id);
            };
            choices.Add(accent.Id, choice);
            palette.Controls.Add(choice, index, 0);
        }

        rows.Controls.Add(notesList, 0, 0);
        rows.Controls.Add(export, 0, 1);
        rows.Controls.Add(delete, 0, 2);
        rows.Controls.Add(dates, 0, 3);
        actions.Controls.Add(rows);
        Controls.Add(actions);
        Controls.Add(palette);

        notesList.Click += (_, _) => showNotes();
        export.Click += (_, _) => exportNote();
        delete.Click += (_, _) => deleteNote();
    }

    public void FitTo(Size available)
    {
        int preferredHeight = PaletteHeight + rows.GetPreferredSize(new Size(available.Width, 0)).Height;
        Size = new Size(available.Width, Math.Min(available.Height, preferredHeight));
    }

    public void FocusSelection()
    {
        if (!choices[selectedAccent].Focus()) notesList.Focus();
    }

    public void Apply(NotesTheme theme, string? accent)
    {
        BackColor = theme.Surface;
        ForeColor = theme.Ink;
        dates.ForeColor = theme.MutedInk;
        selectedAccent = NotePalette.Resolve(accent).Id;
        updatingSelection = true;
        try
        {
            foreach (var (id, choice) in choices)
            {
                choice.ApplyTheme(theme);
                choice.Checked = id == selectedAccent;
            }
        }
        finally
        {
            updatingSelection = false;
        }

        foreach (var button in new[] { notesList, export, delete })
        {
            button.BackColor = theme.Surface;
            button.ForeColor = theme.Ink;
            button.FlatAppearance.MouseOverBackColor = theme.Control;
            button.FlatAppearance.MouseDownBackColor = theme.Pressed;
        }

        delete.ForeColor = theme.Surface.GetBrightness() < 0.5f
            ? Color.FromArgb(251, 177, 180)
            : Color.FromArgb(151, 49, 54);
    }

    public void SetAvailability(bool storageReady)
    {
        foreach (var choice in choices.Values) choice.Enabled = storageReady;
        delete.Enabled = storageReady;
    }

    public void SetDates(Note note)
    {
        dates.Text = $"Created {note.CreatedAt.ToLocalTime():g}{Environment.NewLine}Updated {note.UpdatedAt.ToLocalTime():g}";
    }

    private static Button CreateAction(string label)
    {
        var button = new NoteButton
        {
            Text = label,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            Height = 40,
            Margin = Padding.Empty,
            FillCell = true
        };
        NotesForm.StyleButton(button);
        button.Padding = new Padding(14, 0, 14, 0);
        return button;
    }

    private sealed class ColorChoice : RadioButton
    {
        private readonly Color swatch;
        private bool hovered;

        public ColorChoice(string name, Color swatch)
        {
            AccessibleName = name;
            Dock = DockStyle.Fill;
            Appearance = Appearance.Button;
            FlatStyle = FlatStyle.Flat;
            Margin = Padding.Empty;
            this.swatch = swatch;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public void ApplyTheme(NotesTheme theme)
        {
            BackColor = theme.Surface;
            ForeColor = theme.Ink;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs args)
        {
            base.OnMouseEnter(args);
            hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs args)
        {
            base.OnMouseLeave(args);
            hovered = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            var graphics = args.Graphics;
            Color fill = Enabled
                ? hovered ? NoteButtonSurface.Blend(swatch, Color.Black, 8) : swatch
                : NoteButtonSurface.Blend(swatch, BackColor, 45);
            graphics.Clear(fill);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            if (Checked)
            {
                using var check = new Pen(NotesTheme.PrimaryInk, 1.7f);
                int cx = Width / 2;
                int cy = Height / 2;
                graphics.DrawLines(check, [new Point(cx - 5, cy), new Point(cx - 1, cy + 4), new Point(cx + 6, cy - 4)]);
            }

            if (Focused && ShowFocusCues)
            {
                using var focus = new Pen(ForeColor);
                graphics.DrawRectangle(focus, Rectangle.Inflate(ClientRectangle, -3, -3));
            }
        }
    }
}
