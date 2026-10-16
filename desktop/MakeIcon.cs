// Builds Notepad.ico from the same document glyph the title bar shows (Segoe Fluent Icons U+E8A5 in
// the accent color), at the sizes Windows uses for shortcuts, the taskbar and Explorer.
//   csc /nologo /out:%TEMP%\MakeIcon.exe /r:System.Drawing.dll MakeIcon.cs && %TEMP%\MakeIcon.exe Notepad.ico
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

static class MakeIcon
{
    static void Main(string[] args)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var images = new List<byte[]>();
        using (var font = new FontFamily("Segoe Fluent Icons"))
            foreach (int s in sizes)
                using (var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                using (var brush = new SolidBrush(Color.FromArgb(0x00, 0x5F, 0xB8)))
                using (var f = new Font(font, s * 0.82f, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var ms = new MemoryStream())
                {
                    g.TextRenderingHint = s < 32 ? TextRenderingHint.AntiAliasGridFit : TextRenderingHint.AntiAlias;
                    g.DrawString("", f, brush, new RectangleF(0, 0, s, s), sf);
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
