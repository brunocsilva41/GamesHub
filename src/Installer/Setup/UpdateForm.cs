using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace GamesHub.Installer
{
    internal sealed class UpdateForm : Form
    {
        public int ExitCode { get; private set; } = 1;
        private readonly SetupOptions opts;
        private readonly Image logo = Payload.Logo();
        private readonly BrandProgress bar = new BrandProgress();
        private readonly Label status;
        private bool busy = true;

        public UpdateForm(SetupOptions o)
        {
            opts = o;
            SuspendLayout();
            Text = "Atualizando o GamesHub";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Brand.Back;
            ForeColor = Brand.Text;
            Font = Brand.Font(9.75f);
            ShowInTaskbar = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception ex) { InstallerLog.Warn("No window icon", ex); }
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);

            Controls.Add(new Label
            {
                Text = "Atualizando o GamesHub para a versão " + Product.Version + "…", Font = Brand.Display(12f), ForeColor = Brand.Text,
                BackColor = Brand.Back, AutoSize = false, Bounds = new Rectangle(88, 30, 330, 26), UseMnemonic = false,
            });
            status = new Label
            {
                Text = "Aguardando o GamesHub fechar…", Font = Brand.Font(9f), ForeColor = Brand.Muted, BackColor = Brand.Back,
                AutoSize = false, Bounds = new Rectangle(89, 58, 330, 20), UseMnemonic = false,
            };
            Controls.Add(status);
            bar.Bounds = new Rectangle(88, 94, 332, 6);
            Controls.Add(bar);

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(452, 132);
            ResumeLayout(false);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float s = DeviceDpi / 96f;
            Graphics g = e.Graphics;
            using (var b = Brand.Accent(new RectangleF(0, 0, Width, 3 * s))) g.FillRectangle(b, 0, 0, Width, 3 * s);
            using (var pen = new Pen(Brand.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            Brand.DrawLogo(g, new Rectangle((int)(28 * s), (int)(32 * s), (int)(44 * s), (int)(44 * s)), logo);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            var engine = new SetupEngine
            {
                Progress = (pct, msg) => BeginInvoke((Action)(() => { bar.Value = pct; status.Text = msg; })),
                ConfirmCloseApp = () => true,
                ConfirmKill = () => true,
            };
            var t = new Thread(() =>
            {
                SetupResult r = null;
                Exception error = null;
                try { r = engine.Run(opts); }
                catch (Exception ex) { error = ex; InstallerLog.Error("Update failed", ex); }
                BeginInvoke((Action)(() => Done(r, error)));
            }) { IsBackground = true, Name = "update" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        private void Done(SetupResult r, Exception error)
        {
            busy = false;
            if (error == null)
            {
                ExitCode = 0;
                InstallerLog.WriteResult(SetupProgram.ResultFile, "OK:" + r.InstallDir);
                SetupProgram.LaunchApp(r.ExePath);
            }
            else
            {
                InstallerLog.WriteResult(SetupProgram.ResultFile, "ERRO:" + error.Message);
                MessageBox.Show(this, "Não foi possível atualizar o GamesHub.\n\n" + error.Message + "\n\nDetalhes em " + InstallerLog.FilePath,
                    "GamesHub", MessageBoxButtons.OK, MessageBoxIcon.Error);
                // bring the (old or partially updated) app back so the user isn't left with nothing
                SetupProgram.LaunchApp(System.IO.Path.Combine(SetupEngine.ResolveInstallDir(opts.InstallDir), Product.ExeName));
            }
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
            base.OnFormClosing(e);
        }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ClassStyle |= 0x20000; /* CS_DROPSHADOW */ return cp; }
        }
    }
}
