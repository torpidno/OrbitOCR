using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace OrbitOCR.Utils;

public static class IconHelper
{
    public static Icon GenerateAppIcon()
    {
        int[] sizes = [16, 32, 48, 64];
        var pngStreams = new List<byte[]>();

        foreach (int size in sizes)
        {
            using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float pad = size * 0.08f;
                var outerRect = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);

                // Outer orbital glowing ring
                using (var ringPen = new Pen(Color.FromArgb(230, 59, 130, 246), Math.Max(1.5f, size * 0.08f)))
                {
                    g.DrawEllipse(ringPen, outerRect);
                }

                // Inner circle / lens
                float innerPad = size * 0.26f;
                var innerRect = new RectangleF(innerPad, innerPad, size - innerPad * 2, size - innerPad * 2);
                using (var fillBrush = new SolidBrush(Color.FromArgb(200, 96, 165, 250)))
                {
                    g.FillEllipse(fillBrush, innerRect);
                }

                // Center white pupil/dot
                float dotPad = size * 0.40f;
                var dotRect = new RectangleF(dotPad, dotPad, size - dotPad * 2, size - dotPad * 2);
                using (var whiteBrush = new SolidBrush(Color.White))
                {
                    g.FillEllipse(whiteBrush, dotRect);
                }

                // Small search lens handle accent
                using (var handlePen = new Pen(Color.FromArgb(240, 99, 102, 241), Math.Max(2f, size * 0.10f)))
                {
                    handlePen.StartCap = LineCap.Round;
                    handlePen.EndCap = LineCap.Round;
                    g.DrawLine(
                        handlePen,
                        size * 0.72f, size * 0.72f,
                        size * 0.94f, size * 0.94f);
                }
            }

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            pngStreams.Add(ms.ToArray());
        }

        using var icoStream = new MemoryStream();
        using var writer = new BinaryWriter(icoStream);

        // ICONDIR
        writer.Write((short)0); // Reserved
        writer.Write((short)1); // Type 1 = ICO
        writer.Write((short)sizes.Length); // Image count

        int offset = 6 + (16 * sizes.Length);

        for (int i = 0; i < sizes.Length; i++)
        {
            int size = sizes[i];
            byte[] data = pngStreams[i];

            writer.Write((byte)(size >= 256 ? 0 : size)); // Width
            writer.Write((byte)(size >= 256 ? 0 : size)); // Height
            writer.Write((byte)0); // Colors
            writer.Write((byte)0); // Reserved
            writer.Write((short)1); // Color planes
            writer.Write((short)32); // Bits per pixel
            writer.Write(data.Length); // Image size
            writer.Write(offset); // Image offset

            offset += data.Length;
        }

        foreach (var data in pngStreams)
        {
            writer.Write(data);
        }

        icoStream.Position = 0;
        return new Icon(icoStream);
    }

    public static void EnsureIconFileExists(string filePath)
    {
        if (File.Exists(filePath)) return;

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var icon = GenerateAppIcon();
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        icon.Save(fs);
    }
}
