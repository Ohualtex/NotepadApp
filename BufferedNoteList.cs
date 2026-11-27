namespace NotepadApp;

internal sealed class BufferedNoteList : Control
{
    private const int ScrollGutter = 12;
    private const int ThumbWidth = 3;
    private const int MinimumThumbHeight = 22;
    private readonly System.Windows.Forms.Timer wheelTimer = new() { Interval = 15 };
    private Note[] items = [];
    private int[] rowOffsets = [0];
    private int selectedIndex = -1;
    private int scrollOffset;
    private int wheelTarget;
    private int thumbGrabOffset;
    private bool draggingThumb;
    private bool railGesture;

    public BufferedNoteList()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        AccessibleRole = AccessibleRole.List;
        wheelTimer.Tick += (_, _) => AdvanceWheel();
    }

    internal IReadOnlyList<Note> Items => items;
    internal Note? SelectedItem => selectedIndex >= 0 ? items[selectedIndex] : null;
    internal int CardWidth => Math.Max(0, ClientSize.Width - ScrollGutter);
    internal int ScrollOffset => scrollOffset;
    private int MaximumOffset => Math.Max(0, rowOffsets[^1] - ClientSize.Height);

    internal event DrawItemEventHandler? DrawItem;
    internal event EventHandler? ViewportScrolled;
    internal event Action<Note>? NoteActivated;

    internal int IndexOf(Note note) => Array.IndexOf(items, note);

    internal void SetCards(Note[] notes, int[] heights, Note? selection, int offset)
    {
        items = notes;
        selectedIndex = selection is null ? -1 : IndexOf(selection);
        SetHeights(heights);
        ScrollTo(offset);
    }

    internal void SetHeights(int[] heights)
    {
        rowOffsets = new int[heights.Length + 1];
        for (int index = 0; index < heights.Length; index++)
            rowOffsets[index + 1] = rowOffsets[index] + heights[index];

        ScrollTo(scrollOffset);
        Invalidate();
    }

    internal void SetRowHeight(int index, int height)
    {
        int difference = height - (rowOffsets[index + 1] - rowOffsets[index]);
        if (difference == 0) return;

        for (int next = index + 1; next < rowOffsets.Length; next++)
            rowOffsets[next] += difference;

        ScrollTo(scrollOffset);
        Invalidate();
    }

    internal void ScrollTo(int offset)
    {
        wheelTimer.Stop();
        wheelTarget = Math.Clamp(offset, 0, MaximumOffset);
        SetOffset(wheelTarget);
    }

    internal Rectangle GetItemRectangle(int index) => new(
        0, rowOffsets[index] - scrollOffset, CardWidth, rowOffsets[index + 1] - rowOffsets[index]);

    internal int IndexFromPoint(Point point) =>
        point.X >= 0 && point.X < CardWidth && point.Y >= 0 && point.Y < ClientSize.Height
            ? IndexAtOffset(point.Y + scrollOffset)
            : -1;

    private int IndexAtOffset(int offset)
    {
        if (offset < 0 || offset >= rowOffsets[^1]) return -1;
        int index = Array.BinarySearch(rowOffsets, offset);
        return index >= 0 ? index : ~index - 1;
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);
        var cardArea = new Rectangle(0, 0, CardWidth, ClientSize.Height);
        var dirty = Rectangle.Intersect(cardArea, args.ClipRectangle);
        if (!dirty.IsEmpty)
        {
            int first = IndexAtOffset(dirty.Top + scrollOffset);
            for (int index = first; index >= 0 && index < items.Length; index++)
            {
                Rectangle bounds = GetItemRectangle(index);
                if (bounds.Top >= dirty.Bottom) break;
                var state = index == selectedIndex ? DrawItemState.Selected : DrawItemState.None;
                DrawItem?.Invoke(this, new DrawItemEventArgs(args.Graphics, Font, bounds, index, state));
            }
        }

        Rectangle thumb = GetThumb();
        if (!thumb.IsEmpty && thumb.IntersectsWith(args.ClipRectangle))
        {
            using var brush = new SolidBrush(NoteButtonSurface.Blend(BackColor, ForeColor, draggingThumb ? 65 : 47));
            args.Graphics.FillRectangle(brush, thumb);
        }
    }

    protected override void OnSizeChanged(EventArgs args)
    {
        base.OnSizeChanged(args);
        ScrollTo(scrollOffset);
    }

    protected override void OnMouseWheel(MouseEventArgs args)
    {
        base.OnMouseWheel(args);
        int lines = SystemInformation.MouseWheelScrollLines;
        if (lines == 0) return;
        int pixels = lines < 0 ? ClientSize.Height : lines * Font.Height;
        int delta = (int)Math.Round(args.Delta * pixels / 120.0);
        int start = wheelTimer.Enabled ? wheelTarget : scrollOffset;
        wheelTarget = Math.Clamp(start - delta, 0, MaximumOffset);
        if (scrollOffset != wheelTarget) wheelTimer.Start();
    }

    private void AdvanceWheel()
    {
        int distance = wheelTarget - scrollOffset;
        if (distance == 0)
        {
            wheelTimer.Stop();
            return;
        }

        SetOffset(scrollOffset + Math.Sign(distance) * Math.Max(1, Math.Abs(distance) / 4));
        if (scrollOffset == wheelTarget) wheelTimer.Stop();
    }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        if (args.Button == MouseButtons.Left)
        {
            Focus();
            railGesture = args.X >= CardWidth;
            if (railGesture)
            {
                Rectangle thumb = GetThumb();
                if (!thumb.IsEmpty)
                {
                    if (args.Y >= thumb.Top && args.Y < thumb.Bottom)
                    {
                        thumbGrabOffset = args.Y - thumb.Top;
                        draggingThumb = true;
                        Capture = true;
                        Invalidate(thumb);
                    }
                    else ScrollTo(scrollOffset + (args.Y < thumb.Top ? -ClientSize.Height : ClientSize.Height));
                }
            }
            else
            {
                int index = IndexFromPoint(args.Location);
                if (index >= 0) SelectRow(index);
            }
        }
        base.OnMouseDown(args);
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        if (draggingThumb)
        {
            int travel = ClientSize.Height - GetThumb().Height;
            if (travel > 0)
                ScrollTo((int)Math.Round((args.Y - thumbGrabOffset) * (double)MaximumOffset / travel));
        }
        base.OnMouseMove(args);
    }

    protected override void OnMouseUp(MouseEventArgs args)
    {
        if (draggingThumb && args.Button == MouseButtons.Left)
            Capture = false;
        base.OnMouseUp(args);
    }

    protected override void OnMouseCaptureChanged(EventArgs args)
    {
        base.OnMouseCaptureChanged(args);
        if (Capture || !draggingThumb) return;
        draggingThumb = false;
        Invalidate(GetThumb());
    }

    protected override void OnMouseClick(MouseEventArgs args)
    {
        if (!railGesture || args.Button != MouseButtons.Left) base.OnMouseClick(args);
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown or Keys.Enter
        || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs args)
    {
        if (items.Length > 0)
        {
            int current = Math.Max(0, selectedIndex);
            int next = args.KeyCode switch
            {
                Keys.Up => current - 1,
                Keys.Down => selectedIndex + 1,
                Keys.Home => 0,
                Keys.End => items.Length - 1,
                Keys.PageUp => IndexAtOffset(Math.Max(0, rowOffsets[current] - ClientSize.Height)),
                Keys.PageDown => IndexAtOffset(Math.Min(rowOffsets[^1] - 1, rowOffsets[current] + ClientSize.Height)),
                _ => -1
            };
            if (args.KeyCode is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown)
            {
                args.Handled = true;
                args.SuppressKeyPress = true;
                next = Math.Clamp(next, 0, items.Length - 1);
                SelectRow(next);
                Rectangle bounds = GetItemRectangle(next);
                if (bounds.Top < 0 || bounds.Height > ClientSize.Height) ScrollTo(rowOffsets[next]);
                else if (bounds.Bottom > ClientSize.Height) ScrollTo(rowOffsets[next + 1] - ClientSize.Height);
            }
        }
        if (args.KeyCode == Keys.Enter && SelectedItem is Note note)
        {
            args.Handled = true;
            args.SuppressKeyPress = true;
            NoteActivated?.Invoke(note);
        }
        base.OnKeyDown(args);
    }

    protected override void OnGotFocus(EventArgs args)
    {
        base.OnGotFocus(args);
        InvalidateRow(selectedIndex);
    }

    protected override void OnLostFocus(EventArgs args)
    {
        base.OnLostFocus(args);
        InvalidateRow(selectedIndex);
    }

    private void SelectRow(int index)
    {
        if (selectedIndex == index) return;
        InvalidateRow(selectedIndex);
        selectedIndex = index;
        InvalidateRow(selectedIndex);
        AccessibilityNotifyClients(AccessibleEvents.Selection, index);
    }

    private void InvalidateRow(int index)
    {
        if (index >= 0) Invalidate(GetItemRectangle(index));
    }

    private void SetOffset(int value)
    {
        if (scrollOffset == value) return;
        scrollOffset = value;
        Invalidate();
        ViewportScrolled?.Invoke(this, EventArgs.Empty);
    }

    private Rectangle GetThumb()
    {
        if (MaximumOffset == 0 || ClientSize.Height <= 0) return Rectangle.Empty;
        int height = Math.Clamp((int)((long)ClientSize.Height * ClientSize.Height / rowOffsets[^1]),
            Math.Min(MinimumThumbHeight, ClientSize.Height), ClientSize.Height);
        int top = (int)((long)(ClientSize.Height - height) * scrollOffset / MaximumOffset);
        return new Rectangle(ClientSize.Width - ThumbWidth, top, ThumbWidth, height);
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new NoteListAccessibility(this);

    private sealed class NoteListAccessibility(BufferedNoteList list) : ControlAccessibleObject(list)
    {
        public override int GetChildCount() => list.items.Length;

        public override AccessibleObject? GetChild(int index) => index >= 0 && index < list.items.Length
            ? new NoteRowAccessibility(list, list.items[index])
            : null;

        public override AccessibleObject? GetSelected() => GetChild(list.selectedIndex);

        public override AccessibleObject? GetFocused() => list.Focused ? GetSelected() : null;
    }

    private sealed class NoteRowAccessibility(BufferedNoteList list, Note note) : AccessibleObject
    {
        public override AccessibleObject Parent => list.AccessibilityObject;
        public override AccessibleRole Role => AccessibleRole.ListItem;
        public override string? Name
        {
            get => note.DisplayTitle;
            set { }
        }

        public override string DefaultAction => "Open";

        public override Rectangle Bounds
        {
            get
            {
                int index = list.IndexOf(note);
                return index < 0 ? Rectangle.Empty : list.RectangleToScreen(
                    Rectangle.Intersect(list.ClientRectangle, list.GetItemRectangle(index)));
            }
        }

        public override AccessibleStates State
        {
            get
            {
                int index = list.IndexOf(note);
                var state = AccessibleStates.Focusable | AccessibleStates.Selectable;
                if (index < 0 || Bounds.Width <= 0 || Bounds.Height <= 0) state |= AccessibleStates.Offscreen;
                if (index >= 0 && index == list.selectedIndex)
                {
                    state |= AccessibleStates.Selected;
                    if (list.Focused) state |= AccessibleStates.Focused;
                }
                return state;
            }
        }

        public override void DoDefaultAction()
        {
            if (list.IndexOf(note) >= 0) list.NoteActivated?.Invoke(note);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) wheelTimer.Dispose();
        base.Dispose(disposing);
    }
}
