using System.Drawing.Drawing2D;

namespace PC_Medic;

/// <summary>Which hand-drawn plate belongs to a rig.</summary>
internal enum RigMark
{
    Generic,
    Cinebench,
    FurMark,
    HwInfo,
    DiskMark,
    DiskInfo,
    Folder,
}

/// <summary>
/// Vector plates for the benchmark rigs, drawn in code so the suite keeps shipping without
/// image assets. Each mark is a simplified stand-in, not a vendor logo file.
/// </summary>
internal static class RigArt
{
    public static RigMark For(string folder) => folder switch
    {
        "CinebenchPortable" => RigMark.Cinebench,
        "FurMarkPortable" => RigMark.FurMark,
        "HardwareInfoPortable" => RigMark.HwInfo,
        "CrystalDiskMarkPortable" => RigMark.DiskMark,
        "CrystalDiskInfoPortable" => RigMark.DiskInfo,
        _ => RigMark.Generic,
    };

    public static void Paint(Graphics g, RectangleF box, RigMark mark, Color tone)
    {
        float size = Math.Min(box.Width, box.Height);
        if (size < 8) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var area = new RectangleF(
            box.X + ((box.Width - size) / 2f),
            box.Y + ((box.Height - size) / 2f),
            size,
            size);

        float stroke = Math.Max(1.3f, size * 0.075f);

        switch (mark)
        {
            case RigMark.Cinebench:
                Cinebench(g, area, tone, stroke);
                break;
            case RigMark.FurMark:
                Flame(g, area, tone, stroke);
                break;
            case RigMark.HwInfo:
                Trace(g, area, tone, stroke);
                break;
            case RigMark.DiskMark:
                Disk(g, area, tone, stroke, speed: true);
                break;
            case RigMark.DiskInfo:
                Disk(g, area, tone, stroke, speed: false);
                break;
            case RigMark.Folder:
                Folder(g, area, tone, stroke);
                break;
            default:
                Plate(g, area, tone, stroke);
                break;
        }
    }

    /// <summary>CPU die with a four-core pattern.</summary>
    private static void Cinebench(Graphics g, RectangleF a, Color tone, float stroke)
    {
        var die = new RectangleF(a.X + (a.Width * 0.13f), a.Y + (a.Height * 0.13f), a.Width * 0.74f, a.Height * 0.74f);

        using (GraphicsPath path = Theme.Round(die, a.Width * 0.12f))
        {
            using var pen = new Pen(tone, stroke);
            g.DrawPath(pen, path);
        }

        float cell = die.Width * 0.26f;
        using var fill = new SolidBrush(Theme.Alpha(tone, 210));

        for (int row = 0; row < 2; row++)
        {
            for (int column = 0; column < 2; column++)
            {
                g.FillRectangle(
                    fill,
                    die.X + (die.Width * 0.16f) + (column * cell * 1.36f),
                    die.Y + (die.Height * 0.16f) + (row * cell * 1.36f),
                    cell,
                    cell);
            }
        }
    }

    /// <summary>Heat plume for the GPU stress rig.</summary>
    private static void Flame(Graphics g, RectangleF a, Color tone, float stroke)
    {
        using var flame = new GraphicsPath();
        flame.AddBezier(
            new PointF(a.X + (a.Width * 0.50f), a.Y + (a.Height * 0.09f)),
            new PointF(a.X + (a.Width * 0.82f), a.Y + (a.Height * 0.38f)),
            new PointF(a.X + (a.Width * 0.78f), a.Y + (a.Height * 0.70f)),
            new PointF(a.X + (a.Width * 0.50f), a.Y + (a.Height * 0.89f)));
        flame.AddBezier(
            new PointF(a.X + (a.Width * 0.50f), a.Y + (a.Height * 0.89f)),
            new PointF(a.X + (a.Width * 0.22f), a.Y + (a.Height * 0.70f)),
            new PointF(a.X + (a.Width * 0.18f), a.Y + (a.Height * 0.38f)),
            new PointF(a.X + (a.Width * 0.50f), a.Y + (a.Height * 0.09f)));
        flame.CloseFigure();

        using (var fill = new SolidBrush(Theme.Alpha(tone, 60)))
        {
            g.FillPath(fill, flame);
        }

        using (var pen = new Pen(tone, stroke))
        {
            g.DrawPath(pen, flame);
        }

        using var core = new SolidBrush(tone);
        g.FillEllipse(core, a.X + (a.Width * 0.41f), a.Y + (a.Height * 0.53f), a.Width * 0.18f, a.Height * 0.20f);
    }

    /// <summary>Sensor trace, the HWiNFO signature shape.</summary>
    private static void Trace(Graphics g, RectangleF a, Color tone, float stroke)
    {
        PointF[] trace =
        {
            new(a.X + (a.Width * 0.10f), a.Y + (a.Height * 0.55f)),
            new(a.X + (a.Width * 0.32f), a.Y + (a.Height * 0.55f)),
            new(a.X + (a.Width * 0.44f), a.Y + (a.Height * 0.24f)),
            new(a.X + (a.Width * 0.58f), a.Y + (a.Height * 0.76f)),
            new(a.X + (a.Width * 0.70f), a.Y + (a.Height * 0.55f)),
            new(a.X + (a.Width * 0.90f), a.Y + (a.Height * 0.55f)),
        };

        using var pen = new Pen(tone, stroke)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        g.DrawLines(pen, trace);
    }

    /// <summary>Drive body: a speed arrow for the benchmark, a health blip for the SMART tool.</summary>
    private static void Disk(Graphics g, RectangleF a, Color tone, float stroke, bool speed)
    {
        var body = new RectangleF(a.X + (a.Width * 0.16f), a.Y + (a.Height * 0.25f), a.Width * 0.68f, a.Height * 0.50f);

        using (GraphicsPath path = Theme.Round(body, a.Width * 0.10f))
        {
            using var pen = new Pen(tone, stroke);
            g.DrawPath(pen, path);
        }

        using var dot = new SolidBrush(Theme.Alpha(tone, 210));
        g.FillEllipse(dot, body.X + (body.Width * 0.09f), body.Y + (body.Height * 0.63f), a.Width * 0.07f, a.Width * 0.07f);

        if (speed)
        {
            float cy = body.Y + (body.Height * 0.38f);
            float tail = body.X + (body.Width * 0.44f);
            float head = body.X + (body.Width * 0.86f);

            using var arrow = new Pen(tone, stroke);
            g.DrawLine(arrow, tail, cy, head, cy);
            g.DrawLine(arrow, head - (a.Width * 0.11f), cy - (a.Height * 0.10f), head, cy);
            g.DrawLine(arrow, head - (a.Width * 0.11f), cy + (a.Height * 0.10f), head, cy);
            return;
        }

        PointF[] blip =
        {
            new(body.X + (body.Width * 0.34f), body.Y + (body.Height * 0.34f)),
            new(body.X + (body.Width * 0.45f), body.Y + (body.Height * 0.34f)),
            new(body.X + (body.Width * 0.51f), body.Y + (body.Height * 0.16f)),
            new(body.X + (body.Width * 0.59f), body.Y + (body.Height * 0.50f)),
            new(body.X + (body.Width * 0.69f), body.Y + (body.Height * 0.34f)),
            new(body.X + (body.Width * 0.84f), body.Y + (body.Height * 0.34f)),
        };

        using var line = new Pen(tone, Math.Max(1.1f, stroke * 0.8f)) { LineJoin = LineJoin.Round };
        g.DrawLines(line, blip);
    }

    private static void Folder(Graphics g, RectangleF a, Color tone, float stroke)
    {
        var body = new RectangleF(a.X + (a.Width * 0.14f), a.Y + (a.Height * 0.32f), a.Width * 0.72f, a.Height * 0.42f);

        using (GraphicsPath path = Theme.Round(body, a.Width * 0.08f))
        {
            using var pen = new Pen(tone, stroke);
            g.DrawPath(pen, path);
        }

        using var tab = new Pen(tone, stroke);
        g.DrawLine(
            tab,
            body.X + (body.Width * 0.08f),
            body.Y,
            body.X + (body.Width * 0.40f),
            body.Y);
    }

    /// <summary>Fallback plate with a download arrow, used for any rig without its own mark.</summary>
    private static void Plate(Graphics g, RectangleF a, Color tone, float stroke)
    {
        var plate = new RectangleF(a.X + (a.Width * 0.16f), a.Y + (a.Height * 0.16f), a.Width * 0.68f, a.Height * 0.68f);

        using (GraphicsPath path = Theme.Round(plate, a.Width * 0.12f))
        {
            using var pen = new Pen(tone, stroke);
            g.DrawPath(pen, path);
        }

        float cx = plate.X + (plate.Width / 2f);
        float top = plate.Y + (plate.Height * 0.28f);
        float bottom = plate.Y + (plate.Height * 0.66f);

        using var down = new Pen(tone, stroke);
        g.DrawLine(down, cx, top, cx, bottom);
        g.DrawLine(down, cx - (plate.Width * 0.15f), bottom - (plate.Height * 0.14f), cx, bottom);
        g.DrawLine(down, cx + (plate.Width * 0.15f), bottom - (plate.Height * 0.14f), cx, bottom);
    }
}
