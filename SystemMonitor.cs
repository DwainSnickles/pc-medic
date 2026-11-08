namespace PC_Medic;

internal readonly record struct SystemSample(
    double CpuPercent,
    double MemoryPercent,
    ulong MemoryUsed,
    ulong MemoryTotal,
    TimeSpan Uptime);

/// <summary>One-second sampler with exponential smoothing so the gauges stay readable.</summary>
internal sealed class SystemMonitor : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private long _idle;
    private long _kernel;
    private long _user;
    private bool _primed;
    private double _cpu;

    public event Action<SystemSample>? Sampled;

    public void Start()
    {
        if (_timer.Enabled) return;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    public void Stop() => _timer.Stop();

    private void Tick()
    {
        (ulong total, ulong available) = Native.PhysicalMemory();
        ulong used = total >= available ? total - available : 0;
        double memoryPercent = total > 0 ? used * 100.0 / total : 0;

        if (Native.CpuTimes(out long idle, out long kernel, out long user))
        {
            if (_primed)
            {
                long deltaIdle = idle - _idle;
                long deltaTotal = kernel - _kernel + (user - _user);
                if (deltaTotal > 0)
                {
                    double raw = Math.Clamp((deltaTotal - deltaIdle) * 100.0 / deltaTotal, 0, 100);
                    _cpu = _primed ? (_cpu * 0.5) + (raw * 0.5) : raw;
                }
            }

            _idle = idle;
            _kernel = kernel;
            _user = user;
            _primed = true;
        }

        Sampled?.Invoke(new SystemSample(_cpu, memoryPercent, used, total, Native.Uptime));
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
