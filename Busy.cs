namespace PC_Medic;

/// <summary>
/// Puts the wait cursor on the owning window while a scan, load or install runs. These passes touch
/// the registry, the disk or a child process, so the pointer has to show that work is happening.
/// </summary>
internal sealed class BusyScope : IDisposable
{
    private readonly Form? _form;
    private readonly Cursor _previous;

    public BusyScope(Control? anchor)
    {
        _form = anchor?.FindForm();
        _previous = Cursor.Current ?? Cursors.Default;

        if (_form is not null && !_form.IsDisposed) _form.UseWaitCursor = true;
        Cursor.Current = Cursors.WaitCursor;
    }

    public void Dispose()
    {
        if (_form is not null && !_form.IsDisposed) _form.UseWaitCursor = false;
        Cursor.Current = _previous;
    }
}

internal static class Busy
{
    /// <summary>Hold the returned scope for as long as the slow work runs.</summary>
    public static IDisposable Enter(Control? anchor) => new BusyScope(anchor);
}
