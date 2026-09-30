using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace GamesHub
{
    /// <summary>Image validation, conversion and atomic writes (System.Drawing).</summary>
    public static class ImageFiles
    {
        public const int MaxWidth = 3840;
        public const int MinSide = 16;

        /// <summary>".png" / ".jpg" from the file signature, or null for anything else.</summary>
        public static string SniffExt(byte[] data)
        {
            if (data == null || data.Length < 8) return null;
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return ".png";
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return ".jpg";
            return null;
        }

        /// <summary>True when the bytes are a JPEG/PNG that GDI+ can decode, at least 16x16.</summary>
        public static bool IsValidImage(byte[] data, out string ext)
        {
            ext = SniffExt(data);
            if (ext == null) return false;
            try
            {
                using (var ms = new MemoryStream(data))
                using (Image img = Image.FromStream(ms, false, true))
                    return img.Width >= MinSide && img.Height >= MinSide;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is ExternalException || ex is OutOfMemoryException)
            {
                Log.Info("Art: undecodable image data (" + ex.GetType().Name + ")");
                return false;
            }
        }

        /// <summary>Writes bytes to basePath+ext via a temp file in the same folder, replacing any
        /// variant with another extension. Returns the final path.</summary>
        public static string SaveBytes(string basePath, string ext, byte[] data)
        {
            string target = basePath + ext;
            WriteAtomic(target, tmp => File.WriteAllBytes(tmp, data));
            RemoveOtherVariants(basePath, ext);
            return target;
        }

        public static void WriteAtomic(string target, Action<string> writeTemp)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string tmp = target + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            try
            {
                writeTemp(tmp);
                if (File.Exists(target)) File.Replace(tmp, target, null);
                else File.Move(tmp, target);
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        private static void RemoveOtherVariants(string basePath, string keepExt)
        {
            foreach (string p in new[] { ".jpg", ".png" }.Where(e => e != keepExt).Select(e => basePath + e).Where(File.Exists))
            {
                try { File.Delete(p); }
                catch (Exception ex) when (ExpectedErrors.IsFileSystem(ex)) { Log.Warn("Art: cannot delete " + p, ex); }
            }
        }

        /// <summary>
        /// Stores a user-provided image as basePath + (.png|.jpg). Downscales when wider than 3840px.
        /// Keeps the original bytes when no conversion is needed. Throws on invalid input.
        /// </summary>
        public static string SaveNormalized(byte[] data, string basePath, bool png)
        {
            string ext = png ? ".png" : ".jpg";
            using (var ms = new MemoryStream(data))
            using (Image src = Image.FromStream(ms, false, true))
            {
                if (src.Width < MinSide || src.Height < MinSide) throw new ArgumentException("image too small");
                if (src.Width <= MaxWidth && SniffExt(data) == ext) return SaveBytes(basePath, ext, data);
                int w = Math.Min(src.Width, MaxWidth);
                int h = Math.Max(1, (int)Math.Round(src.Height * (double)w / src.Width));
                using (var bmp = new Bitmap(w, h, png ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        if (!png) g.Clear(Color.Black);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        using (var attrs = new ImageAttributes())
                        {
                            attrs.SetWrapMode(WrapMode.TileFlipXY);   // avoids dark edges when scaling
                            g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
                        }
                    }
                    string target = basePath + ext;
                    WriteAtomic(target, tmp => SaveBitmap(bmp, tmp, png));
                    RemoveOtherVariants(basePath, ext);
                    return target;
                }
            }
        }

        public static void SaveBitmap(Bitmap bmp, string file, bool png)
        {
            if (png) { bmp.Save(file, ImageFormat.Png); return; }
            ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using (var quality = new EncoderParameter(Encoder.Quality, 92L))
            using (var ps = new EncoderParameters(1))
            {
                ps.Param[0] = quality;
                bmp.Save(file, jpeg, ps);
            }
        }

        /// <summary>Bounding box of pixels with alpha &gt; threshold; Rectangle.Empty when blank.</summary>
        public static Rectangle ContentBounds(Bitmap bmp, byte alphaThreshold = 16)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                byte[] px = new byte[stride * bmp.Height];
                Marshal.Copy(data.Scan0, px, 0, px.Length);
                int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
                for (int y = 0; y < bmp.Height; y++)
                    for (int x = 0; x < bmp.Width; x++)
                        if (px[y * stride + x * 4 + 3] > alphaThreshold)
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                return maxX < 0 ? Rectangle.Empty : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        /// <summary>Crops transparent padding to a centered square (keeps transparency). Caller disposes.</summary>
        public static Bitmap CropToSquare(Bitmap src, Rectangle content)
        {
            int side = Math.Max(content.Width, content.Height);
            var dst = new Bitmap(side, side, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceCopy;
                int dx = (side - content.Width) / 2, dy = (side - content.Height) / 2;
                g.DrawImage(src, new Rectangle(dx, dy, content.Width, content.Height), content, GraphicsUnit.Pixel);
            }
            return dst;
        }
    }
}
