namespace NotepadApp;

internal class ResizableNoteWindow : Form
{
    private const int SizingFrame = 0x00040000;
    private const int CalculateClientArea = 0x0083;
    private const int HitTest = 0x0084;

    protected ResizableNoteWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        MinimizeBox = false;
        MaximizeBox = false;
        Padding = new Padding(5);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.Style |= SizingFrame;
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == CalculateClientArea && message.WParam != 0)
        {
            message.Result = 0;
            return;
        }

        base.WndProc(ref message);
        if (message.Msg != HitTest || message.Result != 1) return;

        long coordinates = message.LParam.ToInt64();
        var point = PointToClient(new Point(unchecked((short)coordinates), unchecked((short)(coordinates >> 16))));
        int edge = Padding.Left;
        int horizontal = point.X < edge ? -1 : point.X >= ClientSize.Width - edge ? 1 : 0;
        int vertical = point.Y < edge ? -1 : point.Y >= ClientSize.Height - edge ? 1 : 0;

        message.Result = (horizontal, vertical) switch
        {
            (-1, -1) => 13,
            (1, -1) => 14,
            (-1, 1) => 16,
            (1, 1) => 17,
            (-1, 0) => 10,
            (1, 0) => 11,
            (0, -1) => 12,
            (0, 1) => 15,
            _ => 1
        };
    }

    protected void ConfigureCaption(Control control, Action? clickCaption = null)
    {
        Point pointerStart = Point.Empty;
        Point windowStart = Point.Empty;
        bool dragging = false;
        bool pressed = false;

        control.MouseDown += (_, args) =>
        {
            if (args.Button != MouseButtons.Left) return;
            pointerStart = Cursor.Position;
            windowStart = Location;
            pressed = true;
            dragging = false;
            control.Capture = true;
        };

        control.MouseMove += (_, args) =>
        {
            if (!control.Capture || (args.Button & MouseButtons.Left) == 0) return;
            Point pointer = Cursor.Position;
            int dx = pointer.X - pointerStart.X;
            int dy = pointer.Y - pointerStart.Y;
            if (!dragging && Math.Abs(dx) < SystemInformation.DragSize.Width / 2 &&
                Math.Abs(dy) < SystemInformation.DragSize.Height / 2) return;

            dragging = true;
            Location = new Point(windowStart.X + dx, windowStart.Y + dy);
        };

        control.MouseUp += (_, args) =>
        {
            if (args.Button != MouseButtons.Left || !pressed) return;
            pressed = false;
            control.Capture = false;
            if (!dragging) clickCaption?.Invoke();
        };
    }
}
