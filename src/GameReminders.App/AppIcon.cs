using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace GameReminders.App;

internal static class AppIcon
{
    public static Icon Create(bool attention)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var tile = new SolidBrush(Color.FromArgb(37, 99, 184));
            using var tilePath = RoundedRectangle(new RectangleF(2, 2, 28, 28), 7);
            graphics.FillPath(tile, tilePath);

            using var bellPen = new Pen(Color.White, 2.5f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            graphics.DrawArc(bellPen, 9, 8, 14, 15, 190, 160);
            graphics.DrawLine(bellPen, 9.3f, 17, 7.5f, 22);
            graphics.DrawLine(bellPen, 24.7f, 22, 22.7f, 17);
            graphics.DrawLine(bellPen, 7.5f, 22, 24.7f, 22);
            graphics.DrawArc(bellPen, 13, 21, 6, 5, 5, 170);

            if (attention)
            {
                using var ring = new SolidBrush(Color.White);
                using var dot = new SolidBrush(Color.FromArgb(220, 38, 38));
                graphics.FillEllipse(ring, 21, 0, 11, 11);
                graphics.FillEllipse(dot, 22.5f, 1.5f, 8, 8);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
