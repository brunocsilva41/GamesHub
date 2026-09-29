// OWNER: CORE agent.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace GamesHub
{
    /// <summary>Persisted window state (%LOCALAPPDATA%\GamesHub\window.json). Bounds are physical pixels.</summary>
    public sealed class WindowPlacementData
    {
        public Rectangle Bounds = Rectangle.Empty;
        public bool Maximized;
        /// <summary>The one-time "still running in the tray" balloon was shown.</summary>
        public bool TrayHintShown;
    }

    public static class WindowPlacement
    {
        private static string FilePath => Path.Combine(AppPaths.DataDir, "window.json");

        /// <summary>Minimum visible part (px) of a saved window for it to count as on-screen.</summary>
        private const int MinVisible = 120;

        public static WindowPlacementData Load()
        {
            var d = Json.Load<Dictionary<string, object>>(FilePath, null);
            var p = new WindowPlacementData();
            if (d == null) return p;
            p.Bounds = new Rectangle((int)Json.Long(d, "x"), (int)Json.Long(d, "y"), (int)Json.Long(d, "width"), (int)Json.Long(d, "height"));
            p.Maximized = Json.Bool(d, "maximized");
            p.TrayHintShown = Json.Bool(d, "trayHintShown");
            return p;
        }

        public static void Save(WindowPlacementData p)
        {
            try
            {
                Json.Save(FilePath, new Dictionary<string, object>
                {
                    ["x"] = p.Bounds.X, ["y"] = p.Bounds.Y, ["width"] = p.Bounds.Width, ["height"] = p.Bounds.Height,
                    ["maximized"] = p.Maximized, ["trayHintShown"] = p.TrayHintShown,
                });
            }
            catch (Exception ex)
            {
                Log.Warn("Could not save window.json", ex);
            }
        }

        /// <summary>
        /// Makes saved bounds usable on the current monitor layout: sizes are clamped to [min, work area],
        /// a window that is (mostly) off-screen is centered on the primary work area (workAreas[0]) with
        /// the default size, otherwise it is moved fully inside the work area it overlaps most.
        /// </summary>
        public static Rectangle Clamp(Rectangle saved, IList<Rectangle> workAreas, Size min, Size def)
        {
            if (workAreas == null || workAreas.Count == 0) return new Rectangle(Point.Empty, def);
            Rectangle primary = workAreas[0];

            Rectangle area = Rectangle.Empty;
            long best = 0;
            if (saved.Width > 0 && saved.Height > 0)
            {
                foreach (Rectangle wa in workAreas)
                {
                    Rectangle i = Rectangle.Intersect(saved, wa);
                    long visible = i.Width >= MinVisible && i.Height >= MinVisible ? (long)i.Width * i.Height : 0;
                    if (visible > best) { best = visible; area = wa; }
                }
            }

            if (best == 0)
            {
                Size s = FitSize(def, min, primary.Size);
                return new Rectangle(primary.X + (primary.Width - s.Width) / 2, primary.Y + (primary.Height - s.Height) / 2, s.Width, s.Height);
            }

            Size size = FitSize(saved.Size, min, area.Size);
            int x = Math.Max(area.Left, Math.Min(saved.X, area.Right - size.Width));
            int y = Math.Max(area.Top, Math.Min(saved.Y, area.Bottom - size.Height));
            return new Rectangle(x, y, size.Width, size.Height);
        }

        private static Size FitSize(Size wanted, Size min, Size max)
        {
            int w = Math.Min(Math.Max(wanted.Width, min.Width), max.Width);
            int h = Math.Min(Math.Max(wanted.Height, min.Height), max.Height);
            return new Size(w, h);
        }

        /// <summary>Resize hit-test for the frameless window's border band (client coordinates).
        /// Returns an HT* code, or HTCLIENT when the point is not on the border.</summary>
        public static int HitTestBorder(Point p, Size client, int border)
        {
            if (border <= 0) return CoreNative.HTCLIENT;
            int corner = border * 3;
            bool left = p.X < border, right = p.X >= client.Width - border;
            bool top = p.Y < border, bottom = p.Y >= client.Height - border;
            bool nearLeft = p.X < corner, nearRight = p.X >= client.Width - corner;
            bool nearTop = p.Y < corner, nearBottom = p.Y >= client.Height - corner;

            if ((top && nearLeft) || (left && nearTop)) return CoreNative.HTTOPLEFT;
            if ((top && nearRight) || (right && nearTop)) return CoreNative.HTTOPRIGHT;
            if ((bottom && nearLeft) || (left && nearBottom)) return CoreNative.HTBOTTOMLEFT;
            if ((bottom && nearRight) || (right && nearBottom)) return CoreNative.HTBOTTOMRIGHT;
            if (left) return CoreNative.HTLEFT;
            if (right) return CoreNative.HTRIGHT;
            if (top) return CoreNative.HTTOP;
            if (bottom) return CoreNative.HTBOTTOM;
            return CoreNative.HTCLIENT;
        }
    }
}
