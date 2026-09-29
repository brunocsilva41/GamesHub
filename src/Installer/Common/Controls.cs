using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace GamesHub.Installer
{
    internal abstract class BrandControl : Control
    {
        protected BrandControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw, true);
            ForeColor = Brand.Text;
            Font = Brand.Font(9.75f);
        }
        protected float S => DeviceDpi / 96f;
        protected Color ParentBack => Parent?.BackColor ?? Brand.Back;
        protected bool hover, pressed;
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    }

    internal enum ButtonKind { Primary, Secondary, Ghost }

    internal sealed class BrandButton : BrandControl, IButtonControl
    {
        public ButtonKind Kind { get; set; }
        public DialogResult DialogResult { get; set; }
        private bool isDefault;

        public BrandButton(string text, ButtonKind kind = ButtonKind.Secondary)
        {
            Text = text;
            Kind = kind;
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Size = new Size(112, 36);
            if (kind == ButtonKind.Primary) Font = Brand.Font(9.75f, FontStyle.Bold);
        }

        public void NotifyDefault(bool value) { isDefault = value; Invalidate(); }
        public void PerformClick() { if (Enabled && Visible) OnClick(EventArgs.Empty); }

        protected override void OnClick(EventArgs e)
        {
            Form f = FindForm();
            if (f != null && DialogResult != DialogResult.None) f.DialogResult = DialogResult;
            base.OnClick(e);
        }

        protected override bool IsInputKey(Keys k) => k == Keys.Enter || k == Keys.Space || base.IsInputKey(k);
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { PerformClick(); e.Handled = true; }
            base.OnKeyUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(ParentBack);
            Brand.HighQuality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color text = Brand.Text;
            using (GraphicsPath p = Brand.Rounded(r, 8 * S))
            {
                if (!Enabled)
                {
                    using (var b = new SolidBrush(Brand.Surface)) g.FillPath(b, p);
                    using (var pen = new Pen(Brand.Border)) g.DrawPath(pen, p);
                    text = Brand.Dim;
                }
                else if (Kind == ButtonKind.Primary)
                {
                    using (LinearGradientBrush b = Brand.Accent(r)) g.FillPath(b, p);
                    int overlay = pressed ? -40 : hover ? 28 : 0;
                    if (overlay != 0)
                        using (var b = new SolidBrush(overlay > 0 ? Color.FromArgb(overlay, Color.White) : Color.FromArgb(-overlay, Color.Black)))
                            g.FillPath(b, p);
                    text = Color.White;
                }
                else if (Kind == ButtonKind.Secondary)
                {
                    using (var b = new SolidBrush(pressed ? Brand.Surface : hover ? Brand.Mix(Brand.Surface2, Color.White, 0.04f) : Brand.Surface2))
                        g.FillPath(b, p);
                    using (var pen = new Pen(hover ? Brand.BorderHover : Brand.Border)) g.DrawPath(pen, p);
                }
                else
                {
                    if (hover) using (var b = new SolidBrush(Brand.Surface2)) g.FillPath(b, p);
                    text = hover ? Brand.Text : Brand.Muted;
                }
                if (Focused && ShowFocusCues)
                {
                    var fr = RectangleF.Inflate(r, -2 * S, -2 * S);
                    using (GraphicsPath fp = Brand.Rounded(fr, 6 * S))
                    using (var pen = new Pen(Color.FromArgb(200, Kind == ButtonKind.Primary ? Color.White : Brand.Cyan), 1.2f * S))
                        g.DrawPath(pen, fp);
                }
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Checkbox with an optional muted description line.</summary>
    internal sealed class BrandCheck : BrandControl
    {
        private bool isChecked;
        public string Description { get; set; } = "";
        public event EventHandler CheckedChanged;

        public BrandCheck(string text, bool isChecked, string description = "")
        {
            Text = text;
            this.isChecked = isChecked;
            Description = description ?? "";
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Size = new Size(460, string.IsNullOrEmpty(Description) ? 28 : 44);
        }

        public bool Checked
        {
            get => isChecked;
            set { if (isChecked == value) return; isChecked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
        }

        protected override void OnClick(EventArgs e) { if (Enabled) Checked = !Checked; Focus(); base.OnClick(e); }
        protected override bool IsInputKey(Keys k) => k == Keys.Space || base.IsInputKey(k);
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && Enabled) { Checked = !Checked; e.Handled = true; }
            base.OnKeyUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(ParentBack);
            Brand.HighQuality(g);
            float s = S, box = 18 * s;
            int lineH = TextRenderer.MeasureText("Ag", Font).Height;
            var br = new RectangleF(1 * s, (lineH - box) / 2 + 3 * s, box, box);
            using (GraphicsPath p = Brand.Rounded(br, 5 * s))
            {
                if (isChecked)
                {
                    using (LinearGradientBrush b = Brand.Accent(br, 45f)) g.FillPath(b, p);
                    if (!Enabled) using (var b = new SolidBrush(Color.FromArgb(140, Brand.Back))) g.FillPath(b, p);
                    using (var pen = new Pen(Color.White, 2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        g.DrawLines(pen, new[]
                        {
                            new PointF(br.X + box * 0.25f, br.Y + box * 0.52f),
                            new PointF(br.X + box * 0.43f, br.Y + box * 0.70f),
                            new PointF(br.X + box * 0.76f, br.Y + box * 0.32f),
                        });
                }
                else
                {
                    using (var b = new SolidBrush(Brand.Surface)) g.FillPath(b, p);
                    using (var pen = new Pen(hover && Enabled ? Brand.Muted : Brand.BorderHover, 1.4f * s)) g.DrawPath(pen, p);
                }
                if (Focused && ShowFocusCues)
                    using (GraphicsPath fp = Brand.Rounded(RectangleF.Inflate(br, 2.5f * s, 2.5f * s), 7 * s))
                    using (var pen = new Pen(Color.FromArgb(180, Brand.Cyan), 1.2f * s)) g.DrawPath(pen, fp);
            }
            int x = (int)(30 * s);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, (int)(3 * s), Width - x, lineH), Enabled ? Brand.Text : Brand.Dim,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            if (Description.Length > 0)
                using (Font small = Brand.Font(8.75f))
                    TextRenderer.DrawText(g, Description, small, new Rectangle(x, (int)(3 * s) + lineH, Width - x, Height - lineH), Brand.Muted,
                        TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>Rounded text box + "Procurar…" folder picker.</summary>
    internal sealed class BrandPathBox : BrandControl
    {
        private readonly TextBox box = new TextBox();
        private readonly BrandButton browse = new BrandButton("Procurar…");
        public string DialogDescription { get; set; } = "Escolha uma pasta";

        public BrandPathBox(string value)
        {
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Brand.Surface;
            box.ForeColor = Brand.Text;
            box.Font = Brand.Font(9.75f);
            box.Text = value ?? "";
            box.GotFocus += (s, e) => Invalidate();
            box.LostFocus += (s, e) => Invalidate();
            browse.Click += OnBrowse;
            Controls.Add(box);
            Controls.Add(browse);
            Size = new Size(560, 36);
            Cursor = Cursors.IBeam;
        }

        public string Value { get => box.Text.Trim(); set => box.Text = value ?? ""; }

        protected override void OnMouseDown(MouseEventArgs e) { box.Focus(); base.OnMouseDown(e); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int bw = (int)(108 * S), gap = (int)(8 * S), pad = (int)(12 * S);
            browse.SetBounds(Width - bw, 0, bw, Height);
            box.SetBounds(pad, (Height - box.PreferredHeight) / 2 + 1, Math.Max(10, Width - bw - gap - pad * 2), box.PreferredHeight);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            box.Enabled = browse.Enabled = Enabled;
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(ParentBack);
            Brand.HighQuality(g);
            float bw = 108 * S + 8 * S;
            var r = new RectangleF(0.5f, 0.5f, Width - bw - 1f, Height - 1.5f);
            using (GraphicsPath p = Brand.Rounded(r, 8 * S))
            {
                using (var b = new SolidBrush(Brand.Surface)) g.FillPath(b, p);
                if (box.Focused) using (LinearGradientBrush b = Brand.Accent(r)) using (var pen = new Pen(b, 1.5f * S)) g.DrawPath(pen, p);
                else using (var pen = new Pen(Brand.Border)) g.DrawPath(pen, p);
            }
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog { Description = DialogDescription, ShowNewFolderButton = true })
            {
                string cur = Value;
                try
                {
                    while (!string.IsNullOrEmpty(cur) && !Directory.Exists(cur)) cur = Path.GetDirectoryName(cur);
                }
                catch (Exception ex) { InstallerLog.Warn("Invalid path in box: " + Value, ex); cur = null; }
                if (!string.IsNullOrEmpty(cur)) dlg.SelectedPath = cur;
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK) Value = dlg.SelectedPath;
            }
            box.Focus();
        }
    }

    /// <summary>Thin rounded progress bar with a violet→cyan fill that eases toward Value (0..100).</summary>
    internal sealed class BrandProgress : BrandControl
    {
        private float shown;
        private int target;
        private readonly Timer anim = new Timer { Interval = 16 };

        public BrandProgress()
        {
            Size = new Size(560, 8);
            anim.Tick += (s, e) =>
            {
                shown += (target - shown) * 0.18f;
                if (Math.Abs(target - shown) < 0.2f) { shown = target; anim.Stop(); }
                Invalidate();
            };
        }

        public int Value
        {
            get => target;
            set { target = Math.Max(0, Math.Min(100, value)); if (!anim.Enabled) anim.Start(); }
        }

        protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(ParentBack);
            Brand.HighQuality(g);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Brand.Rounded(r, r.Height / 2))
            using (var b = new SolidBrush(Brand.Surface2)) g.FillPath(b, p);
            float w = r.Width * shown / 100f;
            if (w < r.Height) return;
            var fr = new RectangleF(0, 0, w, r.Height);
            using (GraphicsPath p = Brand.Rounded(fr, r.Height / 2))
            using (LinearGradientBrush b = Brand.Accent(new RectangleF(0, 0, r.Width, r.Height))) g.FillPath(b, p);
        }
    }

    /// <summary>Rounded info box with a gradient accent bar on the left.</summary>
    internal sealed class Callout : BrandControl
    {
        public Callout(string text) { Text = text; Size = new Size(560, 64); Font = Brand.Font(9f); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(ParentBack);
            Brand.HighQuality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Brand.Rounded(r, 10 * S))
            {
                using (var b = new SolidBrush(Brand.Surface)) g.FillPath(b, p);
                using (var pen = new Pen(Brand.Border)) g.DrawPath(pen, p);
                g.SetClip(p);
                using (LinearGradientBrush b = Brand.Accent(new RectangleF(0, 0, 4 * S, Height), 90f)) g.FillRectangle(b, 0, 0, 4 * S, Height);
                g.ResetClip();
            }
            int pad = (int)(16 * S);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(pad, pad / 2, Width - pad * 2, Height - pad), Brand.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
    }

    /// <summary>Feature list with gradient bullets.</summary>
    internal sealed class BulletList : BrandControl
    {
        private readonly string[] items;
        public BulletList(params string[] items) { this.items = items; Size = new Size(560, items.Length * 26); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(ParentBack);
            Brand.HighQuality(g);
            float row = (float)Height / Math.Max(1, items.Length), d = 8 * S;
            for (int i = 0; i < items.Length; i++)
            {
                float y = i * row;
                var dot = new RectangleF(2 * S, y + (row - d) / 2, d, d);
                using (LinearGradientBrush b = Brand.Accent(dot, 45f)) g.FillEllipse(b, dot);
                TextRenderer.DrawText(g, items[i], Font, new Rectangle((int)(20 * S), (int)y, Width - (int)(20 * S), (int)row), Brand.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>Gradient circle with a check mark (success) or an exclamation mark (error).</summary>
    internal sealed class StatusBadge : BrandControl
    {
        public bool Error { get; set; }
        public StatusBadge() { Size = new Size(64, 64); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(ParentBack);
            Brand.HighQuality(g);
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            if (Error) using (var b = new SolidBrush(Brand.Danger)) g.FillEllipse(b, r);
            else using (LinearGradientBrush b = Brand.Accent(r, 45f)) g.FillEllipse(b, r);
            float w = r.Width;
            using (var pen = new Pen(Color.White, w * 0.09f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (Error)
                {
                    g.DrawLine(pen, r.X + w / 2, r.Y + w * 0.27f, r.X + w / 2, r.Y + w * 0.56f);
                    g.DrawLine(pen, r.X + w / 2, r.Y + w * 0.72f, r.X + w / 2, r.Y + w * 0.73f);
                }
                else
                    g.DrawLines(pen, new[]
                    {
                        new PointF(r.X + w * 0.29f, r.Y + w * 0.52f),
                        new PointF(r.X + w * 0.44f, r.Y + w * 0.66f),
                        new PointF(r.X + w * 0.72f, r.Y + w * 0.36f),
                    });
            }
        }
    }
}
