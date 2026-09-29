// OWNER: DIST agent. Wizard window shell (header, pages, footer buttons) shared by Setup and Uninstall.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GamesHub.Installer
{
    internal sealed class HeaderBar : BrandControl
    {
        private readonly Image logo;
        private string subtitle;
        private string step = "";
        private readonly Font titleFont = Brand.Display(15f);
        private readonly Font subFont = Brand.Font(9f);

        public HeaderBar(string title, string subtitle, Image logo)
        {
            Text = title;
            this.subtitle = subtitle;
            this.logo = logo;
            Height = 96;
        }

        public string Step { get => step; set { step = value ?? ""; Invalidate(); } }
        public string Subtitle { get => subtitle; set { subtitle = value ?? ""; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Brand.Back);
            Brand.HighQuality(g);
            float s = S;
            using (LinearGradientBrush b = Brand.Accent(new RectangleF(0, 0, Width, 3 * s))) g.FillRectangle(b, 0, 0, Width, 3 * s);
            // soft glow behind the logo
            var glow = new RectangleF(8 * s, -30 * s, 180 * s, 150 * s);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(glow);
                using (var pgb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(38, Brand.Violet), SurroundColors = new[] { Color.FromArgb(0, Brand.Violet) } })
                    g.FillEllipse(pgb, glow);
            }
            Brand.DrawLogo(g, new Rectangle((int)(32 * s), (int)(26 * s), (int)(44 * s), (int)(44 * s)), logo);
            int x = (int)(90 * s);
            TextRenderer.DrawText(g, Text, titleFont, new Point(x - (int)(2 * s), (int)(22 * s)), Brand.Text, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, subtitle, subFont, new Point(x, (int)(54 * s)), Brand.Muted, TextFormatFlags.NoPadding);
            if (step.Length > 0)
                TextRenderer.DrawText(g, step, subFont, new Rectangle(0, (int)(38 * s), Width - (int)(32 * s), (int)(20 * s)), Brand.Dim,
                    TextFormatFlags.Right | TextFormatFlags.NoPadding);
            using (var pen = new Pen(Brand.Border)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { titleFont.Dispose(); subFont.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class FooterBar : Panel
    {
        public static readonly Color Fill = Color.FromArgb(0x0e, 0x10, 0x1b);
        private readonly List<BrandButton> right = new List<BrandButton>();
        private BrandButton left;

        public FooterBar()
        {
            BackColor = Fill;
            Height = 72;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        }

        /// <summary>Buttons shown on the right, in reading order (last one is the rightmost).</summary>
        public void SetButtons(BrandButton leftButton, params BrandButton[] rightButtons)
        {
            foreach (Control c in Controls) c.Visible = false;
            left = leftButton;
            right.Clear();
            right.AddRange(rightButtons);
            if (left != null) { if (!Controls.Contains(left)) Controls.Add(left); left.Visible = true; }
            foreach (BrandButton b in right) { if (!Controls.Contains(b)) Controls.Add(b); b.Visible = true; }
            PerformLayout();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            float s = DeviceDpi / 96f;
            int h = (int)(36 * s), y = (Height - h) / 2, pad = (int)(32 * s), gap = (int)(10 * s);
            int x = Width - pad;
            for (int i = right.Count - 1; i >= 0; i--)
            {
                int w = Math.Max((int)(112 * s), TextRenderer.MeasureText(right[i].Text, right[i].Font).Width + (int)(36 * s));
                x -= w;
                right[i].SetBounds(x, y, w, h);
                x -= gap;
            }
            if (left != null)
            {
                int w = Math.Max((int)(96 * s), TextRenderer.MeasureText(left.Text, left.Font).Width + (int)(32 * s));
                left.SetBounds(pad - (int)(12 * s), y, w, h);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Brand.Border)) e.Graphics.DrawLine(pen, 0, 0, Width, 0);
        }
    }

    internal abstract class BrandForm : Form
    {
        protected readonly HeaderBar Header;
        protected readonly Panel Content;
        protected readonly FooterBar Footer;
        private readonly List<Panel> pages = new List<Panel>();

        /// <summary>Page area in 96-DPI units (the form auto-scales everything created before CompleteLayout).</summary>
        protected const int PageWidth = 596, PageHeight = 300;

        protected BrandForm(string windowTitle, string title, string subtitle, Image logo)
        {
            SuspendLayout();
            Text = windowTitle;
            BackColor = Brand.Back;
            ForeColor = Brand.Text;
            Font = Brand.Font(9.75f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception ex) { InstallerLog.Warn("No window icon", ex); }

            Content = new Panel { Dock = DockStyle.Fill, BackColor = Brand.Back, Padding = new Padding(32, 20, 32, 6) };
            Footer = new FooterBar { Dock = DockStyle.Bottom };
            Header = new HeaderBar(title, subtitle, logo) { Dock = DockStyle.Top };
            Controls.Add(Content);
            Controls.Add(Footer);
            Controls.Add(Header);
        }

        /// <summary>Call at the end of the derived constructor, after all pages were built.</summary>
        protected void CompleteLayout()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(660, 96 + 72 + 26 + PageHeight);
            ResumeLayout(false);
            PerformLayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Brand.StyleWindow(this);
        }

        protected Panel NewPage()
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Brand.Back, Visible = false };
            pages.Add(p);
            Content.Controls.Add(p);
            return p;
        }

        protected void ShowPage(Panel page)
        {
            foreach (Panel p in pages) p.Visible = p == page;
        }

        protected static Label AddLabel(Control parent, string text, int x, int y, int w, int h, Font font, Color color,
            ContentAlignment align = ContentAlignment.TopLeft)
        {
            var l = new Label
            {
                Text = text, AutoSize = false, Location = new Point(x, y), Size = new Size(w, h), Font = font,
                ForeColor = color, BackColor = parent.BackColor, TextAlign = align, UseMnemonic = false,
            };
            parent.Controls.Add(l);
            return l;
        }

        protected static T Place<T>(Control parent, T c, int x, int y, int w, int h) where T : Control
        {
            c.SetBounds(x, y, w, h);
            parent.Controls.Add(c);
            return c;
        }

        protected DialogResult Ask(string text, MessageBoxButtons buttons = MessageBoxButtons.YesNo, MessageBoxIcon icon = MessageBoxIcon.Question)
            => MessageBox.Show(this, text, Text, buttons, icon);
    }
}
