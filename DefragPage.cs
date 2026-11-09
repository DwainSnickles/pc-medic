using DiskDefrag;

namespace PC_Medic;

/// <summary>Hosts the Disk Defrag tool inside the console instead of in a second process.</summary>
internal sealed class DefragPage : PageBase
{
    private Form? _host;

    public DefragPage()
        : base("DISK DEFRAG", "Analyse, defragment and optimize the volumes on this PC \u2014 live cluster map, inside the console.")
    {
    }

    /// <summary>True while the hosted tool is running an operation.</summary>
    public bool IsWorking => _host is not null && DefragHost.IsWorking(_host);

    public override void OnNavigated()
    {
        if (_host is not null) return;

        _host = DefragHost.CreateEmbedded();
        _host.Dock = DockStyle.Fill;
        Controls.Add(_host);
        _host.Show();
    }
}
