namespace PC_Medic;

/// <summary>
/// Puts the wait cursor on the owning window while a scan, load or install runs. These passes touch
/// the registry, the disk or a child process, so the pointer has to show that work is happening.
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
        }
    }
}

internal static class Busy
{
    /// <summary>Hold the returned scope for as long as the slow work runs.</summary>
    public static IDisposable Enter(Control? anchor) => new BusyScope(anchor);
}
