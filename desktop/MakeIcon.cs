// Builds tnote.ico from the same mark the title bar draws (Ui.Mark in tnote.cs): the T series box, a
// rounded square outline with a bold T inside, at the sizes Windows uses for shortcuts, the taskbar and Explorer.
// A second argument also saves the 256px image as a PNG preview.
//   csc /nologo /out:%TEMP%\MakeIcon.exe /r:System.Drawing.dll MakeIcon.cs && %TEMP%\MakeIcon.exe tnote.ico
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class MakeIcon
{
    static void Main(string[] args)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var images = new List<byte[]>();
        foreach (int s in sizes)
            using (var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            using (var ms = new MemoryStream())
            {
                Mark(g, new RectangleF(0, 0, s, s), Color.FromArgb(0x3B, 0x7B, 0xC4));
                if (s == 256 && args.Length > 1) bmp.Save(args[1], ImageFormat.Png);
                if (s >= 256) bmp.Save(ms, ImageFormat.Png); // PNG only for 256: older loaders read small entries as DIBs
                else WriteDib(bmp, ms);
                images.Add(ms.ToArray());
            }

        // ICO container: one directory entry per size, then the image data
        using (var w = new BinaryWriter(File.Create(args[0])))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
        }
    }

    // Same drawing as Ui.Mark in tnote.cs
    static void Mark(Graphics g, RectangleF r, Color c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = Math.Min(r.Width, r.Height), w = s * 0.11f, x = r.X + (r.Width - s) / 2, y = r.Y + (r.Height - s) / 2;
        var box = new RectangleF(x + w / 2 + s * 0.03f, y + w / 2 + s * 0.03f, s - w - s * 0.06f, s - w - s * 0.06f);
        float d = s * 0.38f;
        using (var path = new GraphicsPath())
        using (var pen = new Pen(c, w))
        using (var b = new SolidBrush(c))
        {
            path.AddArc(box.X, box.Y, d, d, 180, 90); path.AddArc(box.Right - d, box.Y, d, d, 270, 90);
            path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90); path.AddArc(box.X, box.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            g.DrawPath(pen, path);
            float bar = s * 0.46f, stem = w * 1.15f, top = y + s * 0.27f, bottom = y + s * 0.75f;
            g.FillRectangle(b, x + (s - bar) / 2, top, bar, w * 1.1f);
            g.FillRectangle(b, x + (s - stem) / 2, top, stem, bottom - top);
        }
    }

    // 32-bit icon DIB: header with doubled height, bottom-up BGRA rows, then an all-zero AND mask
    static void WriteDib(Bitmap bmp, Stream s)
    {
        int n = bmp.Width;
        var w = new BinaryWriter(s);
        w.Write(40); w.Write(n); w.Write(n * 2); w.Write((short)1); w.Write((short)32);
        w.Write(0); w.Write(n * n * 4); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = n - 1; y >= 0; y--)
            for (int x = 0; x < n; x++) { var c = bmp.GetPixel(x, y); w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A); }
        int maskRow = ((n + 31) / 32) * 4;
        w.Write(new byte[maskRow * n]);
        w.Flush();
    }
}
