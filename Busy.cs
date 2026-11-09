namespace PC_Medic;

/// <summary>
/// Puts the wait cursor on the owning window while a scan, load or install runs, and raises the
/// shell's marquee bar at the same time. These passes touch the registry, the disk or a child
/// process, so the window has to show that work is happening, not just the pointer.
/// Two slow passes can overlap \u2014 the update page's winget query and its driver search do \u2014 so the
/// cursor is reference counted and only drops back when the last scope is released. A scope that
/// outlives the page that owns it is dropped by <see cref="ReleaseAll"/> as the page is left.
/// </summary>
internal sealed class BusyScope : IDisposable
{
    private static readonly object Gate = new();
    private static readonly HashSet<BusyScope> Live = new();
    private static int _depth;
    private static Form? _owner;

    private bool _released;

    public BusyScope(Control? anchor)
    {
        lock (Gate)
        {
            Live.Add(this);

            if (_depth++ > 0) return;

            _owner = anchor?.FindForm();

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

            // Dropped by ReleaseAll already: the cursor belongs to nobody now, so leave it alone.
            if (!Live.Remove(this)) return;

            if (--_depth > 0) return;

            _depth = 0;
            if (_owner is not null && !_owner.IsDisposed) _owner.UseWaitCursor = false;
            _owner = null;
            Cursor.Current = Cursors.Default;
            Busy.Indicator?.End();
        }
    }

    /// <summary>
    /// Drops every outstanding scope at once, for work still running after its page was left behind.
    /// The scopes stay valid: a later Dispose finds itself no longer live and does nothing.
    /// </summary>
    public static void ReleaseAll()
    {
        lock (Gate)
        {
            foreach (BusyScope scope in Live) scope._released = true;
            Live.Clear();
            _depth = 0;

            if (_owner is not null && !_owner.IsDisposed) _owner.UseWaitCursor = false;
            _owner = null;
            Cursor.Current = Cursors.Default;
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

    /// <summary>Clears the wait cursor when the page that started the work is no longer on screen.</summary>
    public static void ReleaseWaitCursor() => BusyScope.ReleaseAll();
}
