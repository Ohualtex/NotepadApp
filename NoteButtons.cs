using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace NotepadApp;

internal sealed class NoteButton : Button
{
    private readonly NoteButtonSurface surface;

    public NoteButton()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        surface = new NoteButtonSurface(this);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<Graphics, Color>? PaintIcon { get; set; }

    internal void SetCenteredIcon(Action<Graphics, Color> draw)
    {
        PaintIcon = (graphics, ink) =>
        {
            GraphicsState state = graphics.Save();
            try
            {
                float scale = DeviceDpi / 96f;
                graphics.TranslateTransform((ClientSize.Width - 1) / 2f, (ClientSize.Height - 1) / 2f);
                graphics.ScaleTransform(scale, scale);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                draw(graphics, ink);
            }
            finally
            {
                graphics.Restore(state);
            }
        };
    }

    protected override void OnPaint(PaintEventArgs args) => surface.Paint(args.Graphics, false, ShowFocusCues, PaintIcon);
}

internal sealed class NoteToggleButton : CheckBox
{
    private readonly NoteButtonSurface surface;

    public NoteToggleButton()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        surface = new NoteButtonSurface(this);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<Graphics, Color>? PaintIcon { get; set; }

    protected override void OnPaint(PaintEventArgs args) => surface.Paint(args.Graphics, Checked, ShowFocusCues, PaintIcon);
}

internal sealed class NoteRadioButton : RadioButton
{
    private readonly NoteButtonSurface surface;

    public NoteRadioButton()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        surface = new NoteButtonSurface(this);
    }

    protected override void OnPaint(PaintEventArgs args) => surface.Paint(args.Graphics, Checked, ShowFocusCues, null);
}

internal sealed class NoteButtonSurface
{
    private readonly ButtonBase button;
    private bool hovered;
    private bool pressed;

    public NoteButtonSurface(ButtonBase button)
    {
        this.button = button;
        button.MouseEnter += (_, _) => SetHovered(true);
        button.MouseLeave += (_, _) => SetHovered(false);
        button.MouseDown += (_, args) => { if (args.Button == MouseButtons.Left) SetPressed(true); };
        button.MouseUp += (_, _) => SetPressed(false);
        button.MouseCaptureChanged += (_, _) => { if (!button.Capture) SetPressed(false); };
        button.KeyDown += (_, args) => { if (args.KeyCode == Keys.Space) SetPressed(true); };
        button.KeyUp += (_, args) => { if (args.KeyCode == Keys.Space) SetPressed(false); };
        button.GotFocus += (_, _) => button.Invalidate();
        button.LostFocus += (_, _) => SetPressed(false);
    }

    public void Paint(Graphics graphics, bool selected, bool showFocus, Action<Graphics, Color>? paintIcon)
    {
        Color fill = button.BackColor;
        if (button.Enabled)
        {
            if (pressed && (hovered || button.Focused)) fill = Fallback(button.FlatAppearance.MouseDownBackColor, fill);
            else if (hovered) fill = Fallback(button.FlatAppearance.MouseOverBackColor, fill);
            else if (selected) fill = Fallback(button.FlatAppearance.CheckedBackColor, fill);
        }

        Color ink = button.Enabled ? button.ForeColor : Blend(fill, button.ForeColor, 57);
        graphics.Clear(button.Parent?.BackColor ?? button.BackColor);
        var rectangle = new Rectangle(0, 0, button.Width - 1, button.Height - 1);
        if (rectangle.Width <= 0 || rectangle.Height <= 0) return;
        using var background = new SolidBrush(fill);
        if (rectangle.Width >= 10 && rectangle.Height >= 10)
        {
            using var shape = new GraphicsPath();
            const int diameter = 10;
            shape.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            shape.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            shape.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            shape.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            shape.CloseFigure();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.FillPath(background, shape);
        }
        else graphics.FillRectangle(background, rectangle);

        if (paintIcon is not null)
        {
            paintIcon(graphics, ink);
        }
        else
        {
            var bounds = Rectangle.FromLTRB(button.Padding.Left, button.Padding.Top,
                button.Width - button.Padding.Right, button.Height - button.Padding.Bottom);
            var flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            if (button.TextAlign != ContentAlignment.MiddleLeft) flags |= TextFormatFlags.HorizontalCenter;
            TextRenderer.DrawText(graphics, button.Text, button.Font, bounds, ink, flags);
        }

        if (button.Focused && showFocus) DrawFocusMarker(graphics, button.ClientSize, ink);
    }

    public static void DrawFocusMarker(Graphics graphics, Size size, Color ink)
    {
        using var pen = new Pen(ink, 1.5f);
        int center = size.Width / 2;
        graphics.DrawLine(pen, center - 6, size.Height - 3, center + 6, size.Height - 3);
    }

    public static Color Blend(Color start, Color end, int weight) => Color.FromArgb(
        (start.R * (100 - weight) + end.R * weight) / 100,
        (start.G * (100 - weight) + end.G * weight) / 100,
        (start.B * (100 - weight) + end.B * weight) / 100);

    private static Color Fallback(Color requested, Color fill) => requested.IsEmpty ? fill : requested;

    private void SetHovered(bool value)
    {
        hovered = value;
        button.Invalidate();
    }

    private void SetPressed(bool value)
    {
        pressed = value;
        button.Invalidate();
    }
}

internal static class NoteButtonIcons
{
    internal static void Add(Graphics graphics, Color ink)
    {
        using var pen = new Pen(ink, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLine(pen, -10, 0, 10, 0);
        graphics.DrawLine(pen, 0, -10, 0, 10);
    }

    internal static void Close(Graphics graphics, Color ink)
    {
        using var pen = new Pen(ink, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLine(pen, -6.5f, -6.5f, 6.5f, 6.5f);
        graphics.DrawLine(pen, -6.5f, 6.5f, 6.5f, -6.5f);
    }

    internal static void More(Graphics graphics, Color ink)
    {
        using var brush = new SolidBrush(ink);
        for (int x = -6; x <= 6; x += 6)
            graphics.FillEllipse(brush, x - 1.25f, -1.25f, 2.5f, 2.5f);
    }

    internal static void Back(Graphics graphics, Color ink)
    {
        using var pen = new Pen(ink, 1.5f)
        {
            StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round
        };
        graphics.DrawLine(pen, -8, 0, 8, 0);
        graphics.DrawLines(pen, [new PointF(-2, -6), new PointF(-8, 0), new PointF(-2, 6)]);
    }
}
