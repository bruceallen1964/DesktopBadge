using System.Drawing;
using System.Drawing.Drawing2D;

namespace DesktopBadge.Core;

/// <summary>Builds tray icons from the active desktop colour.</summary>
internal static class BadgeIconFactory
{
    public static Icon Create(System.Windows.Media.Color accent)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var active = Color.FromArgb(accent.A, accent.R, accent.G, accent.B);
            var idle = Color.FromArgb(255, 104, 104, 118);
            var idleEdge = Color.FromArgb(255, 34, 34, 42);

            // A 2x2 "desktops" grid with the active desktop picked out in colour.
            var cells = new (int X, int Y)[] { (2, 2), (17, 2), (2, 17), (17, 17) };

            for (var i = 0; i < cells.Length; i++)
            {
                var (x, y) = cells[i];
                using var path = RoundedRect(new Rectangle(x, y, 13, 13), 4);
                using var brush = new SolidBrush(i == 0 ? active : idle);
                using var pen = new Pen(i == 0 ? idleEdge : idleEdge, 1.2f);
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
