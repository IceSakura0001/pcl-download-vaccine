using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

class MakeIcon
{
    static void Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "vaccine.ico";
        int[] sizes = new int[] { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
        var pngs = new List<byte[]>();
        foreach (int size in sizes)
        {
            using (var bmp = Render(size))
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                pngs.Add(ms.ToArray());
            }
        }

        using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write((uint)pngs[i].Length);
                bw.Write((uint)offset);
                offset += pngs[i].Length;
            }
            foreach (var png in pngs) bw.Write(png);
        }
        Console.WriteLine("icon written: " + outPath + " (" + new FileInfo(outPath).Length + " bytes)");
    }

    static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);
            float s = size;
            var rect = new RectangleF(s * 0.03f, s * 0.03f, s * 0.94f, s * 0.94f);
            using (var path = RoundedRect(rect, s * 0.24f))
            using (var brush = new LinearGradientBrush(rect, Color.FromArgb(255, 64, 126, 240), Color.FromArgb(255, 22, 68, 168), 90f))
            {
                g.FillPath(brush, path);
            }

            using (var pen = new Pen(Color.White, Math.Max(1.4f, s * 0.085f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                float cx = s * 0.5f;
                float top = s * 0.25f;
                float mid = s * 0.52f;
                g.DrawLine(pen, cx, top, cx, mid);
                g.DrawLine(pen, cx - s * 0.14f, mid - s * 0.13f, cx, mid);
                g.DrawLine(pen, cx + s * 0.14f, mid - s * 0.13f, cx, mid);
                g.DrawLine(pen, s * 0.29f, s * 0.7f, s * 0.71f, s * 0.7f);
            }
        }
        return bmp;
    }

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
