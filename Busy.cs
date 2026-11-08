namespace PC_Medic;

/// <summary>
/// Puts the wait cursor on the owning window while a scan, load or install runs, and raises the
/// shell's marquee bar at the same time. These passes touch the registry, the disk or a child
/// process, so the window has to show that work is happening, not just the pointer.
/// Two slow passes can overlap \u2014 the update page's winget query and its driver search do \u2014 so the
/// cursor is reference counted and only drops back when the last scope is released.
/// </summary>
internal sealed class BusyScope : IDisposable
{
    private static readonly object Gate = new();
    private static int _depth;
    private static Cursor _previous = Cursors.Default;
    private static Form? _owner;

    private bool _released;

    public BusyScope(Control? anchor)
    {
        lock (Gate)
        {
            if (_depth++ > 0) return;

            _owner = anchor?.FindForm();
            _previous = Cursor.Current ?? Cursors.Default;

            if (_owner is not null && !_owner.IsDisposed) _owner.UseWaitCursor = true;
            Cursor.Current = Cursors.WaitCursor;
            Busy.Indicator?.Begin(_owner);
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (_released) return;
            _released = true;

            if (--_depth > 0) return;

            _depth = 0;
            if (_owner is not null && !_owner.IsDisposed) _owner.UseWaitCursor = false;
            _owner = null;
            Cursor.Current = _previous;
            Busy.Indicator?.End();
        }
    }
}

internal static class Busy
{
    /// <summary>
    /// The shell's marquee strip. A wait cursor alone says nothing about what is happening, so every
    /// busy scope also shows a moving bar; the scope hides it again when the last one is released.
    /// </summary>
    public static BusyBar? Indicator { get; set; }

    /// <summary>Hold the returned scope for as long as the slow work runs.</summary>
    public static IDisposable Enter(Control? anchor) => new BusyScope(anchor);
}
