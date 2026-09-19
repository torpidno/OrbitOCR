using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace OrbitOCR.Services;

public class ScreenCaptureService
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    public record VirtualScreenBounds(int Left, int Top, int Width, int Height);

    public VirtualScreenBounds GetVirtualScreenBounds()
    {
        int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        // Fallback if virtual screen metrics are unavailable
        if (width <= 0 || height <= 0)
        {
            left = 0;
            top = 0;
            width = (int)SystemParameters.PrimaryScreenWidth;
            height = (int)SystemParameters.PrimaryScreenHeight;
        }

        return new VirtualScreenBounds(left, top, width, height);
    }

    public (Bitmap Bitmap, VirtualScreenBounds Bounds) CaptureEntireDesktop()
    {
        var bounds = GetVirtualScreenBounds();
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(
                bounds.Left,
                bounds.Top,
                0,
                0,
                new System.Drawing.Size(bounds.Width, bounds.Height),
                CopyPixelOperation.SourceCopy);
        }

        return (bitmap, bounds);
    }

    public static BitmapSource ConvertToBitmapSource(Bitmap bitmap)
    {
        IntPtr hBitmap = bitmap.GetHbitmap();
        try
        {
            var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            // Freeze to enable cross-thread and performance optimizations
            if (bitmapSource.CanFreeze)
            {
                bitmapSource.Freeze();
            }

            return bitmapSource;
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    public static Bitmap? CropBitmap(Bitmap source, Rectangle cropRect)
    {
        // Clamp rectangle within source boundaries
        int x = Math.Max(0, cropRect.X);
        int y = Math.Max(0, cropRect.Y);
        int w = Math.Min(cropRect.Width, source.Width - x);
        int h = Math.Min(cropRect.Height, source.Height - y);

        if (w <= 0 || h <= 0) return null;

        var clampedRect = new Rectangle(x, y, w, h);
        var cropped = new Bitmap(w, h, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(cropped))
        {
            g.DrawImage(
                source,
                new Rectangle(0, 0, w, h),
                clampedRect,
                GraphicsUnit.Pixel);
        }

        return cropped;
    }
}
