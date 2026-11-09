using System.Drawing.Drawing2D;

namespace PC_Medic;

/// <summary>First screen: PC MEDIC asks for administrator rights and lets the user continue read-only instead.</summary>
internal sealed class ElevationGateForm : Form
{
    private const int WmNcHitTest = 0x0084;
    private const int HtCaption = 2;
    private const int HtClient = 1;

    private readonly NeonButton _elevate = new() { Text = "RESTART AS ADMIN", Size = new Size(180, 34), Primary = true };
    private readonly NeonButton _limited = new() { Text = "CONTINUE LIMITED", Size = new Size(170, 34) };

    public ElevationGateForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(660, 318);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        Text = "Administrator Access";

        Controls.Add(MakeLabel("ADMINISTRATOR ACCESS", Theme.Kicker, Theme.Alpha(Theme.Accent, 215), new Point(30, 26)));
        Controls.Add(MakeLabel("RUN WITH FULL CONTROL", new Font(Theme.H2.FontFamily, 16f, FontStyle.Bold), Theme.Text, new Point(30, 44)));

        Label body = MakeLabel(
            "PC MEDIC changes machine-wide settings: SFC and DISM repairs, restore points, power plans, driver scans and Disk Defrag. Windows only allows those with administrator rights.",
            Theme.UiSmall,
            Theme.TextDim,
            new Point(30, 84));
        body.MaximumSize = new Size(ClientSize.Width - 60, 0);
        Controls.Add(body);

        Label warn = MakeLabel(
            "Continuing limited keeps the dashboards live, but repairs, machine-wide tweaks and the Disk Defrag cluster map will not work.",
            Theme.UiSmall,
            Theme.Warn,
            new Point(30, 158));
        warn.MaximumSize = new Size(ClientSize.Width - 60, 0);
        Controls.Add(warn);

        _limited.SetBounds(ClientSize.Width - 30 - 170, ClientSize.Height - 30 - 34, 170, 34);
        _elevate.SetBounds(_limited.Left - 12 - 180, ClientSize.Height - 30 - 34, 180, 34);

        Controls.Add(_limited);
        Controls.Add(_elevate);

        _elevate.Click += (_, _) =>
        {
            RestartRequested = true;
            DialogResult = DialogResult.OK;
            Close();
        };

        _limited.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
    }

    /// <summary>True when the user chose to relaunch through UAC instead of continuing limited.</summary>
    public bool RestartRequested { get; private set; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode != Keys.Escape) return;

        DialogResult = DialogResult.Cancel;
        Close();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WmNcHitTest && (int)m.Result == HtClient) m.Result = HtCaption;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.PaintSurface(g, new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 12f, Theme.Surface, Theme.Alpha(Theme.Accent, 170), Theme.Accent);
    }

    private static Label MakeLabel(string text, Font font, Color color, Point location) => new()
    {
        Text = text,
        Font = font,
        ForeColor = color,
        BackColor = Color.Transparent,
        AutoSize = true,
        Location = location,
    };
}
