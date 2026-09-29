using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal class Conv
{
    private static readonly int[] Sizes = new int[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    private static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("uso: conv <dir-png> <dir-ico>");
            return;
        }
        string src = Path.GetFullPath(args[0]);
        string dst = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(dst);
        foreach (string p in Directory.GetFiles(src, "*.png"))
        {
            string name = Path.GetFileNameWithoutExtension(p);
            string outPath = Path.Combine(dst, name + ".ico");
            try
            {
                MakeIco(p, outPath);
                Console.WriteLine("OK " + name + ".ico");
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL " + name + ": " + ex.Message);
            }
        }
    }

    private static void MakeIco(string png, string icoPath)
    {
        using (Bitmap src = (Bitmap)Image.FromFile(png))
        {
            int n = Sizes.Length;
            byte[][] blobs = new byte[n][];
            bool[] pngs = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int s = Sizes[i];
                using (Bitmap b = Scale(src, s))
                {
                    if (s == 256)
                    {
                        blobs[i] = Png(b);
                        pngs[i] = true;
                    }
                    else
                    {
                        blobs[i] = Bmp(b, s);
                        pngs[i] = false;
                    }
                }
            }
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write((short)0);
                w.Write((short)1);
                w.Write((short)n);
                int offset = 6 + n * 16;
                for (int i = 0; i < n; i++)
                {
                    int s = Sizes[i];
                    w.Write((byte)(s == 256 ? 0 : s));
                    w.Write((byte)(s == 256 ? 0 : s));
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((short)1);
                    w.Write((short)32);
                    w.Write(blobs[i].Length);
                    w.Write(offset);
                    offset += blobs[i].Length;
                }
                for (int i = 0; i < n; i++) w.Write(blobs[i]);
                w.Flush();
                File.WriteAllBytes(icoPath, ms.ToArray());
                ms.Close();
            }
        }
    }

    private static Bitmap Scale(Bitmap src, int s)
    {
        Bitmap b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, 0, 0, s, s);
        }
        return b;
    }

    private static byte[] Bmp(Bitmap b, int s)
    {
        int maskStride = ((s + 31) / 32) * 4;
        int bmpBytes = 40 + s * s * 4;
        int andBytes = maskStride * s;
        byte[] buf = new byte[bmpBytes + andBytes];
        using (MemoryStream ms = new MemoryStream(buf))
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(40);
            w.Write(s);
            w.Write(s * 2);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(0);
            w.Write(s * s * 4);
            w.Write(0);
            w.Write(0);
            w.Write(0);
            w.Write(0);
            for (int y = s - 1; y >= 0; y--)
            {
                for (int x = 0; x < s; x++)
                {
                    Color c = b.GetPixel(x, y);
                    w.Write(c.B);
                    w.Write(c.G);
                    w.Write(c.R);
                    w.Write(c.A);
                }
            }
            w.Write(buf, bmpBytes, andBytes);
            w.Flush();
            ms.Close();
        }
        return buf;
    }

    private static byte[] Png(Bitmap b)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            b.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }
}