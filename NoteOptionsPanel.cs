using System.Drawing.Drawing2D;

namespace NotepadApp;

internal sealed class NoteOptionsPanel : UserControl
{
    private readonly TableLayoutPanel rows = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(8, 6, 8, 6) };
    private readonly Dictionary<string, ColorChoice> choices = [];
    private readonly Button notesList = CreateAction("Notes list");
    private readonly Button save = CreateAction("Save all");
    private readonly Button export = CreateAction("Export .txt");
    private readonly Button delete = CreateAction("Delete note");
    private readonly Label dates = new() { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(8, 6, 8, 3) };
    private string selectedAccent = NotePalette.DefaultId;
    private bool updatingSelection;

    public NoteOptionsPanel(Action<string> changeAccent, Action showNotes, Action saveNote, Action exportNote, Action deleteNote)
    {
        AutoScroll = true;
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var palette = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Height = 39,
            RowCount = 1,
            ColumnCount = NotePalette.Colors.Count,
            Margin = new Padding(0, 0, 0, 4)
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

        rows.Controls.Add(palette, 0, 0);
        rows.Controls.Add(notesList, 0, 1);
        rows.Controls.Add(save, 0, 2);
        rows.Controls.Add(export, 0, 3);
        rows.Controls.Add(delete, 0, 4);
        rows.Controls.Add(dates, 0, 5);
        Controls.Add(rows);

        notesList.Click += (_, _) => showNotes();
        save.Click += (_, _) => saveNote();
        export.Click += (_, _) => exportNote();
        delete.Click += (_, _) => deleteNote();
    }

    public void FitTo(Size available)
    {
        int preferredHeight = rows.GetPreferredSize(new Size(available.Width, 0)).Height;
        Bounds = new Rectangle(0, 0, available.Width, Math.Min(available.Height, preferredHeight));
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

        foreach (var button in new[] { notesList, save, export, delete })
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

    public void SetAvailability(bool storageReady, bool dirty)
    {
        foreach (var choice in choices.Values) choice.Enabled = storageReady;
        delete.Enabled = storageReady;
        save.Enabled = storageReady && dirty;
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
            Height = 30,
            Margin = Padding.Empty
        };
        NotesForm.StyleButton(button);
        return button;
    }

    private sealed class ColorChoice : RadioButton
    {
        private readonly Color swatch;
        private Color hover;

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
            hover = theme.Control;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            var graphics = args.Graphics;
            graphics.Clear(Enabled && ClientRectangle.Contains(PointToClient(Cursor.Position)) ? hover : BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int size = Math.Min(24, Math.Min(Width, Height) - 6);
            if (size < 8) return;

            int x = (Width - size) / 2;
            int y = (Height - size) / 2;
            using var fill = new SolidBrush(Enabled ? swatch : hover);
            graphics.FillEllipse(fill, x, y, size, size);

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
                graphics.DrawEllipse(focus, x - 2, y - 2, size + 4, size + 4);
            }
        }
    }
}
