using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GamesHub.Installer
{
    internal static class Brand
    {
        public static readonly Color Back = Color.FromArgb(0x0b, 0x0d, 0x16);
        public static readonly Color Surface = Color.FromArgb(0x12, 0x15, 0x20);
        public static readonly Color Surface2 = Color.FromArgb(0x18, 0x1b, 0x2a);
        public static readonly Color Border = Color.FromArgb(0x2a, 0x2e, 0x3d);
        public static readonly Color BorderHover = Color.FromArgb(0x3b, 0x40, 0x55);
        public static readonly Color Text = Color.FromArgb(0xe5, 0xe7, 0xeb);
        public static readonly Color Muted = Color.FromArgb(0x9c, 0xa3, 0xaf);
        public static readonly Color Dim = Color.FromArgb(0x6b, 0x72, 0x80);
        public static readonly Color Violet = Color.FromArgb(0x7c, 0x5c, 0xff);
        public static readonly Color Cyan = Color.FromArgb(0x22, 0xd3, 0xee);
        public static readonly Color Danger = Color.FromArgb(0xf8, 0x71, 0x71);

        private static readonly Lazy<string[]> Installed = new Lazy<string[]>(() =>
        {
            using (var fc = new InstalledFontCollection()) return fc.Families.Select(f => f.Name).ToArray();
        });

        private static string Pick(params string[] names) =>
            names.FirstOrDefault(n => Installed.Value.Contains(n, StringComparer.OrdinalIgnoreCase)) ?? "Segoe UI";

        /// <summary>Segoe UI Variable (Windows 11) with Segoe UI fallback.</summary>
        public static readonly string TextFamily = Pick("Segoe UI Variable Text", "Segoe UI");
        public static readonly string DisplayFamily = Pick("Segoe UI Variable Display", "Segoe UI Semibold", "Segoe UI");

        public static Font Font(float pt, FontStyle style = FontStyle.Regular) => new Font(TextFamily, pt, style);
        public static Font Display(float pt, FontStyle style = FontStyle.Bold) => new Font(DisplayFamily, pt, style);

        public static LinearGradientBrush Accent(RectangleF r, float angle = 0f)
        {
            if (r.Width < 1) r.Width = 1;
            if (r.Height < 1) r.Height = 1;
            return new LinearGradientBrush(r, Violet, Cyan, angle);
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void HighQuality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>Dark title bar + (Windows 11) rounded corners and brand caption color.</summary>
        public static void StyleWindow(Form f)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
                int caption = Back.R | (Back.G << 8) | (Back.B << 16); // COLORREF 0x00BBGGRR
                DwmSetWindowAttribute(f.Handle, 35 /* DWMWA_CAPTION_COLOR, Win11 */, ref caption, 4);
            }
            catch (Exception ex) { InstallerLog.Warn("DWM styling unavailable", ex); }
        }

        /// <summary>Draws the GamesHub mark (icon if available, else a gradient tile with "G").</summary>
        public static void DrawLogo(Graphics g, Rectangle r, Image icon)
        {
            HighQuality(g);
            if (icon != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(icon, r);
                return;
            }
            using (GraphicsPath p = Rounded(r, r.Width * 0.28f))
            using (LinearGradientBrush b = Accent(r, 45f)) g.FillPath(b, p);
            using (Font f = Display(r.Height * 0.36f))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("G", f, Brushes.White, r, sf);
        }
    }
}
