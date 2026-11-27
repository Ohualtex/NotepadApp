namespace NotepadApp;

internal class ResizableNoteWindow : Form
{
    private const int NativeCaption = 0x00C00000;
    private const int NativeSizingFrame = 0x00040000;
    private const int NativeExtendedEdges = 0x00020301; // Dialog frame, window edge, client edge, and static edge.

    private ResizeEdge resizeEdge;
    private Point resizeOrigin;
    private Rectangle originalBounds;

    protected ResizableNoteWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        MinimizeBox = false;
        MaximizeBox = false;
        Padding = new Padding(5);
    }

    protected override void OnHandleCreated(EventArgs args)
    {
        base.OnHandleCreated(args);
        if (ShowInTaskbar) TaskbarAppearance.Apply(Handle);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.Style &= ~(NativeCaption | NativeSizingFrame);
            parameters.ExStyle &= ~NativeExtendedEdges;
            return parameters;
        }
    }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        base.OnMouseDown(args);
        if (args.Button != MouseButtons.Left) return;

        resizeEdge = EdgeAt(args.Location);
        if (resizeEdge == ResizeEdge.None) return;

        BeginResize(resizeEdge, PointToScreen(args.Location));
        Cursor = ResizeCursor(resizeEdge);
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        if (resizeEdge == ResizeEdge.None)
        {
            Cursor = ResizeCursor(EdgeAt(args.Location));
            return;
        }

        if (Capture)
        {
            Rectangle target = CalculateResizeBounds(PointToScreen(args.Location));
            if (Bounds != target) Bounds = target;
        }
    }

    protected override void OnMouseUp(MouseEventArgs args)
    {
        base.OnMouseUp(args);
        if (args.Button != MouseButtons.Left || resizeEdge == ResizeEdge.None) return;

        resizeEdge = ResizeEdge.None;
        Capture = false;
        Cursor = ResizeCursor(EdgeAt(args.Location));
    }

    protected override void OnMouseCaptureChanged(EventArgs args)
    {
        base.OnMouseCaptureChanged(args);
        if (Capture || resizeEdge == ResizeEdge.None) return;

        resizeEdge = ResizeEdge.None;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs args)
    {
        base.OnMouseLeave(args);
        if (resizeEdge == ResizeEdge.None) Cursor = Cursors.Default;
    }

    private ResizeEdge EdgeAt(Point point)
    {
        if (!ClientRectangle.Contains(point)) return ResizeEdge.None;

        ResizeEdge horizontal = point.X < Padding.Left ? ResizeEdge.Left :
            point.X >= ClientSize.Width - Padding.Right ? ResizeEdge.Right : ResizeEdge.None;
        ResizeEdge vertical = point.Y < Padding.Top ? ResizeEdge.Top :
            point.Y >= ClientSize.Height - Padding.Bottom ? ResizeEdge.Bottom : ResizeEdge.None;
        return horizontal | vertical;
    }

    private void BeginResize(ResizeEdge edge, Point pointer)
    {
        resizeEdge = edge;
        resizeOrigin = pointer;
        originalBounds = Bounds;
    }

    private Rectangle CalculateResizeBounds(Point pointer)
    {
        int dx = pointer.X - resizeOrigin.X;
        int dy = pointer.Y - resizeOrigin.Y;
        int width = originalBounds.Width;
        int height = originalBounds.Height;

        if (resizeEdge.HasFlag(ResizeEdge.Left)) width -= dx;
        if (resizeEdge.HasFlag(ResizeEdge.Right)) width += dx;
        if (resizeEdge.HasFlag(ResizeEdge.Top)) height -= dy;
        if (resizeEdge.HasFlag(ResizeEdge.Bottom)) height += dy;

        width = Math.Clamp(width, Math.Max(1, MinimumSize.Width), MaximumSize.Width > 0 ? MaximumSize.Width : int.MaxValue);
        height = Math.Clamp(height, Math.Max(1, MinimumSize.Height), MaximumSize.Height > 0 ? MaximumSize.Height : int.MaxValue);

        int left = resizeEdge.HasFlag(ResizeEdge.Left) ? originalBounds.Right - width : originalBounds.Left;
        int top = resizeEdge.HasFlag(ResizeEdge.Top) ? originalBounds.Bottom - height : originalBounds.Top;
        return new Rectangle(left, top, width, height);
    }

    private static Cursor ResizeCursor(ResizeEdge edge) => edge switch
    {
        ResizeEdge.Left or ResizeEdge.Right => Cursors.SizeWE,
        ResizeEdge.Top or ResizeEdge.Bottom => Cursors.SizeNS,
        (ResizeEdge.Left | ResizeEdge.Top) or (ResizeEdge.Right | ResizeEdge.Bottom) => Cursors.SizeNWSE,
        (ResizeEdge.Right | ResizeEdge.Top) or (ResizeEdge.Left | ResizeEdge.Bottom) => Cursors.SizeNESW,
        _ => Cursors.Default
    };

    [Flags]
    private enum ResizeEdge
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8
    }

    protected void ConfigureCaption(Control control, Action? clickCaption = null, int topResizeGrip = 0)
    {
        Point pointerStart = Point.Empty;
        Point windowStart = Point.Empty;
        bool dragging = false;
        bool pressed = false;

        control.MouseDown += (_, args) =>
        {
            if (args.Button != MouseButtons.Left) return;
            pointerStart = Cursor.Position;
            if (args.Y < topResizeGrip)
            {
                BeginResize(ResizeEdge.Top, pointerStart);
                control.Capture = true;
                return;
            }

            windowStart = Location;
            pressed = true;
            dragging = false;
            control.Capture = true;
        };

        control.MouseMove += (_, args) =>
        {
            if (topResizeGrip > 0)
            {
                bool resizing = resizeEdge == ResizeEdge.Top && control.Capture;
                control.Cursor = resizing || args.Y < topResizeGrip ? Cursors.SizeNS : Cursors.Default;
                if (resizing)
                {
                    Bounds = CalculateResizeBounds(Cursor.Position);
                    return;
                }
            }

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
            if (args.Button != MouseButtons.Left) return;
            if (topResizeGrip > 0 && resizeEdge == ResizeEdge.Top && control.Capture)
            {
                resizeEdge = ResizeEdge.None;
                control.Capture = false;
                return;
            }

            if (!pressed) return;
            pressed = false;
            control.Capture = false;
            if (!dragging) clickCaption?.Invoke();
        };

        if (topResizeGrip > 0)
        {
            control.MouseCaptureChanged += (_, _) =>
            {
                if (!control.Capture && resizeEdge == ResizeEdge.Top)
                    resizeEdge = ResizeEdge.None;
            };
        }
    }
}
