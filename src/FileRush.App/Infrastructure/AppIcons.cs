using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileRush.App.Infrastructure;

public static class AppIcons
{
    private static Icon? _trayIcon;
    private static ImageSource? _windowIcon;

    public static Icon TrayIcon => _trayIcon ??= CreateIcon(32);

    public static ImageSource WindowIcon
    {
        get
        {
            if (_windowIcon is null)
            {
                using var bitmap = CreateBitmap(32);
                var handle = bitmap.GetHbitmap();
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    _windowIcon = source;
                }
                finally
                {
                    DeleteObject(handle);
                }
            }
            return _windowIcon;
        }
    }

    public static void Apply(Window window)
    {
        window.Icon = WindowIcon;
    }

    private static Icon CreateIcon(int size)
    {
        using var bitmap = CreateBitmap(size);
        var handle = bitmap.GetHicon();
        var icon = (Icon)Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        return icon;
    }

    private static Bitmap CreateBitmap(int size)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);
        using var back = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, size, size), System.Drawing.Color.FromArgb(0x3C, 0xB0, 0x43), System.Drawing.Color.FromArgb(0x1E, 0x7A, 0x2B), 90f);
        g.FillEllipse(back, 1, 1, size - 2, size - 2);
        using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0x14, 0x5A, 0x1E), Math.Max(1, size / 16f));
        g.DrawEllipse(pen, 1, 1, size - 2, size - 2);
        var w = size * 0.22f;
        var cx = size / 2f;
        var top = size * 0.2f;
        var shaft = size * 0.36f;
        var head = size * 0.62f;
        var bottom = size * 0.78f;
        var points = new[]
        {
            new PointF(cx - w / 2, top),
            new PointF(cx + w / 2, top),
            new PointF(cx + w / 2, shaft + (head - shaft) * 0.35f),
            new PointF(cx + w * 1.1f, shaft + (head - shaft) * 0.35f),
            new PointF(cx, bottom),
            new PointF(cx - w * 1.1f, shaft + (head - shaft) * 0.35f),
            new PointF(cx - w / 2, shaft + (head - shaft) * 0.35f)
        };
        g.FillPolygon(System.Drawing.Brushes.White, points);
        return bitmap;
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
