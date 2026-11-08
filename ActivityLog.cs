namespace PC_Medic;

internal enum LogLevel
{
    Info,
    Success,
    Warn,
    Error,
}

internal readonly record struct LogEntry(DateTime Time, LogLevel Level, string Message)
{
    public Color Tone => Level switch
    {
        LogLevel.Success => Theme.Good,
        LogLevel.Warn => Theme.Warn,
        LogLevel.Error => Theme.Bad,
        _ => Theme.Accent,
    };

    public string Stamp => Time.ToString("HH:mm:ss");
}

internal static class ActivityLog
{
    private const int Capacity = 300;
    private static readonly List<LogEntry> Entries = new();
    private static readonly object Gate = new();

    public static event Action<LogEntry>? Written;

    public static void Info(string message) => Write(LogLevel.Info, message);

    public static void Success(string message) => Write(LogLevel.Success, message);

    public static void Warn(string message) => Write(LogLevel.Warn, message);

    public static void Error(string message) => Write(LogLevel.Error, message);

    public static void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        lock (Gate)
        {
            Entries.Add(entry);
            if (Entries.Count > Capacity) Entries.RemoveRange(0, Entries.Count - Capacity);
        }

        Written?.Invoke(entry);
    }

    public static IReadOnlyList<LogEntry> Snapshot()
    {
        lock (Gate)
        {
            return Entries.ToArray();
        }
    }
}
