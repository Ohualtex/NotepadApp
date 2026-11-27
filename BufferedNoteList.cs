using System.Runtime.InteropServices;

namespace NotepadApp;

internal sealed class BufferedNoteList : ListBox
{
    private const int PaintMessage = 0x000F;
    private const int EraseBackgroundMessage = 0x0014;
    private const int PrintClientMessage = 0x0318;
    private const int PrintClient = 0x00000004;
    private const int SetItemHeightMessage = 0x01A0;
    private nint paintDevice;

    internal void SetRowHeight(int index, int height)
    {
        height = Math.Clamp(height, 1, 255);
        if (GetItemHeight(index) != height)
        {
            var message = Message.Create(Handle, SetItemHeightMessage, index, height);
            DefWndProc(ref message);
        }
    }

    protected override void WndProc(ref Message message)
    {
        switch (message.Msg)
        {
            case EraseBackgroundMessage:
                message.Result = 1;
                return;
            case PaintMessage when message.WParam == 0:
                PaintBuffered();
                return;
            default:
                base.WndProc(ref message);
                return;
        }
    }

    protected override void OnDrawItem(DrawItemEventArgs args)
    {
        // Native WM_PRINTCLIENT can request rows outside the actual update region.
        var bounds = new NativeRect(args.Bounds);
        if (paintDevice == 0 || RectVisible(paintDevice, in bounds))
            base.OnDrawItem(args);
    }

    private void PaintBuffered()
    {
        nint window = Handle;
        nint device = BeginPaint(window, out PaintState state);
        if (device == 0) return;

        nint previousDevice = paintDevice;
        try
        {
            Rectangle dirty = state.Dirty.ToRectangle();
            Rectangle client = ClientRectangle;
            if (dirty.IsEmpty || client.Width <= 0 || client.Height <= 0) return;

            using var target = Graphics.FromHdc(device);
            using var buffer = BufferedGraphicsManager.Current.Allocate(target, client);
            using var background = new SolidBrush(BackColor);
            buffer.Graphics.FillRectangle(background, dirty);

            nint bufferDevice = buffer.Graphics.GetHdc();
            paintDevice = device;
            try
            {
                IntersectClipRect(bufferDevice, dirty.Left, dirty.Top, dirty.Right, dirty.Bottom);
                // Keep native layout, selection, and scrolling behavior while redirecting drawing.
                var print = Message.Create(window, PrintClientMessage, bufferDevice, PrintClient);
                base.WndProc(ref print);
            }
            finally
            {
                paintDevice = previousDevice;
                buffer.Graphics.ReleaseHdc(bufferDevice);
            }

            buffer.Render(device);
        }
        finally
        {
            EndPaint(window, in state);
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint BeginPaint(nint window, out PaintState state);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool EndPaint(nint window, in PaintState state);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int IntersectClipRect(nint device, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern bool RectVisible(nint device, in NativeRect bounds);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect(Rectangle bounds)
    {
        private readonly int left = bounds.Left;
        private readonly int top = bounds.Top;
        private readonly int right = bounds.Right;
        private readonly int bottom = bounds.Bottom;

        internal Rectangle ToRectangle() => Rectangle.FromLTRB(left, top, right, bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintState
    {
        private nint device;
        private int erase;
        public NativeRect Dirty;
        private int restore;
        private int incrementalUpdate;
        // PAINTSTRUCT ends with 32 bytes reserved by Windows.
        private int reserved1, reserved2, reserved3, reserved4;
        private int reserved5, reserved6, reserved7, reserved8;
    }
}
