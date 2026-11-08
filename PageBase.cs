namespace PC_Medic;

/// <summary>Shared base for the seven module pages: fixed docked layout, no flow panels.</summary>
internal abstract class PageBase : UserControl
{
    protected PageBase(string title, string subtitle)
    {
        TitleText = title;
        SubtitleText = subtitle;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        Dock = DockStyle.Fill;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string TitleText { get; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string SubtitleText { get; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action<string>? NavigateTo { get; set; }

    public virtual void OnNavigated() { }

    public virtual void RefreshData() { }

    protected static NeonButton Button(string text, bool primary = false, int width = 160, int height = 34)
        => new()
        {
            Text = text,
            Primary = primary,
            Size = new Size(width, height),
        };

    protected static Card Panel(string header, string kicker = "")
    {
        var card = new Card(header, kicker) { Dock = DockStyle.Fill, Margin = new Padding(0) };
        return card;
    }

    /// <summary>Runs work off the UI thread and reports failures to the activity feed.</summary>
    protected static async Task RunGuarded(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            ActivityLog.Error(ex.Message);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Bg);
    }
}

