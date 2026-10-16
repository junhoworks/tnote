// Builds tnote.ico from the same mark the title bar draws (Ui.MarkBitmap in tnote.cs): the T series box, a
// rounded square with a bold T and a pencil over its corner, at the sizes Windows uses for shortcuts, the taskbar and Explorer.
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
                using (var mk = MarkBitmap(s, Color.FromArgb(0x3B, 0x7B, 0xC4))) g.DrawImage(mk, 0, 0, s, s);
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

    // The T series mark: a rounded square with a bold T, and a pencil over its lower right corner with a
    // clear gap around it (MakeIcon.cs draws tnote.ico the same way). Drawn on its own bitmap so the gap is transparent.
    static Bitmap MarkBitmap(int s, Color c)
    {
        var bmp = new Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        using (var b = new SolidBrush(c))
        using (var clear = new SolidBrush(Color.Transparent))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = s * 0.085f, d = s * 0.26f;
            var box = new RectangleF(s * 0.04f + w / 2, s * 0.04f + w / 2, s * 0.66f - w, s * 0.66f - w);
            using (var path = new GraphicsPath())
            using (var pen = new Pen(c, w))
            {
                path.AddArc(box.X, box.Y, d, d, 180, 90); path.AddArc(box.Right - d, box.Y, d, d, 270, 90);
                path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90); path.AddArc(box.X, box.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                g.DrawPath(pen, path);
            }
            float cx = s * 0.37f, bar = s * 0.32f, stem = s * 0.1f, top = s * 0.2f, bottom = s * 0.55f;
            g.FillRectangle(b, cx - bar / 2, top, bar, s * 0.095f);
            g.FillRectangle(b, cx - stem / 2, top, stem, bottom - top);

            // pencil: drawn pointing left from its tip, then turned to point down-left
            float hw = s * 0.11f, tip = s * 0.19f, len = s * 0.66f, cap = s * 0.12f, gap = s * 0.03f;
            var m = new Matrix(); m.Translate(s * 0.36f, s * 0.97f); m.Rotate(-45);
            using (var outline = new GraphicsPath())
            using (var cut = new Pen(Color.Transparent, s * 0.09f) { LineJoin = LineJoin.Round })
            {
                outline.AddPolygon(new[] { new PointF(0, 0), new PointF(tip, -hw), new PointF(len, -hw), new PointF(len, hw), new PointF(tip, hw) });
                outline.Transform(m);
                g.CompositingMode = CompositingMode.SourceCopy; // knock out the box under and around the pencil
                g.DrawPath(cut, outline); g.FillPath(clear, outline);
                g.CompositingMode = CompositingMode.SourceOver;
            }
            using (var parts = new GraphicsPath())
            {
                parts.AddPolygon(new[] { new PointF(0, 0), new PointF(tip, -hw), new PointF(tip, hw) });
                parts.AddRectangle(new RectangleF(tip + gap, -hw, len - cap - tip - 2 * gap, 2 * hw));
                parts.AddRectangle(new RectangleF(len - cap, -hw, cap, 2 * hw));
                parts.Transform(m);
                g.FillPath(b, parts);
            }
        }
        return bmp;
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
