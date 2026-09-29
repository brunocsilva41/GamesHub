// OWNER: DIST agent. Uninstall window: confirm → progress → done.
using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace GamesHub.Installer
{
    internal sealed class UninstallForm : BrandForm
    {
        public int ExitCode { get; private set; } = 2; // 0 ok, 1 error, 2 cancelled
        public readonly UninstallEngine Engine = new UninstallEngine();

        private readonly Panel pConfirm, pProgress, pDone;
        private readonly BrandButton btnCancel = new BrandButton("Cancelar");
        private readonly BrandButton btnUninstall = new BrandButton("Desinstalar", ButtonKind.Primary);
        private readonly BrandButton btnClose = new BrandButton("Fechar", ButtonKind.Primary);
        private BrandCheck chkData;
        private BrandProgress progress;
        private Label lblStatus, lblDoneTitle, lblDoneText;
        private StatusBadge badge;
        private bool busy;

        public UninstallForm()
            : base("Desinstalar o GamesHub", "GamesHub", "Desinstalador · versão " + Product.Version, LoadLogo())
        {
            pConfirm = NewPage();
            AddLabel(pConfirm, "Desinstalar o GamesHub?", 0, 0, PageWidth, 40, Brand.Display(18f), Brand.Text);
            AddLabel(pConfirm, "O aplicativo, os atalhos e a inicialização automática serão removidos deste computador.",
                1, 44, PageWidth - 20, 44, Brand.Font(10f), Brand.Muted);
            Place(pConfirm, new Callout("Seus jogos não são afetados: a pasta de jogos e tudo dentro dela continuam exatamente como estão."),
                0, 100, PageWidth, 58);
            chkData = Place(pConfirm, new BrandCheck("Apagar também meus dados do GamesHub", false,
                "Configurações, biblioteca, tempo de jogo e capas baixadas (%LOCALAPPDATA%\\GamesHub)"), 0, 178, PageWidth, 44);

            pProgress = NewPage();
            AddLabel(pProgress, "Removendo o GamesHub…", 0, 36, PageWidth, 36, Brand.Display(16f), Brand.Text);
            lblStatus = AddLabel(pProgress, "Preparando…", 1, 80, PageWidth, 22, Brand.Font(9.5f), Brand.Muted);
            progress = Place(pProgress, new BrandProgress(), 0, 116, PageWidth, 8);

            pDone = NewPage();
            badge = Place(pDone, new StatusBadge(), 0, 8, 60, 60);
            lblDoneTitle = AddLabel(pDone, "", 80, 6, PageWidth - 80, 36, Brand.Display(18f), Brand.Text);
            lblDoneText = AddLabel(pDone, "", 81, 44, PageWidth - 81, 80, Brand.Font(9.5f), Brand.Muted);

            btnCancel.Click += (s, e) => Close();
            btnClose.Click += (s, e) => Close();
            btnUninstall.Click += (s, e) => StartUninstall();

            CompleteLayout();
            ShowPage(pConfirm);
            Footer.SetButtons(null, btnCancel, btnUninstall);
            AcceptButton = btnUninstall;
            CancelButton = btnCancel;
        }

        private static Image LoadLogo()
        {
            try
            {
                string ico = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "gamehub-app.ico");
                if (!File.Exists(ico)) return null;
                using (var icon = new Icon(ico, 128, 128)) return icon.ToBitmap();
            }
            catch (Exception ex) { InstallerLog.Warn("No logo", ex); return null; }
        }

        private void StartUninstall()
        {
            busy = true;
            bool purge = chkData.Checked;
            ShowPage(pProgress);
            Footer.SetButtons(null);
            AcceptButton = null; CancelButton = null;

            Engine.Progress = (pct, msg) => BeginInvoke((Action)(() => { progress.Value = pct; lblStatus.Text = msg; }));
            Engine.ConfirmCloseApp = () => (bool)Invoke((Func<bool>)(() =>
                Ask("O GamesHub está aberto e será fechado para continuar.", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK));
            Engine.ConfirmKill = () => (bool)Invoke((Func<bool>)(() =>
                Ask("O GamesHub não fechou sozinho (ele pode estar na bandeja do sistema).\n\nForçar o encerramento?",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes));

            var t = new Thread(() =>
            {
                Exception error = null;
                bool cancelled = false;
                try { Engine.Run(purge); }
                catch (OperationCanceledException) { cancelled = true; }
                catch (Exception ex) { error = ex; InstallerLog.Error("Uninstall failed", ex); }
                BeginInvoke((Action)(() => Done(error, cancelled, purge)));
            }) { IsBackground = true, Name = "uninstall" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        private void Done(Exception error, bool cancelled, bool purge)
        {
            busy = false;
            if (cancelled)
            {
                ShowPage(pConfirm);
                Footer.SetButtons(null, btnCancel, btnUninstall);
                AcceptButton = btnUninstall; CancelButton = btnCancel;
                return;
            }
            ExitCode = error == null ? 0 : 1;
            InstallerLog.WriteResult(UninstallProgram.ResultFile, error == null ? "OK" : "ERRO:" + error.Message);
            badge.Error = error != null;
            badge.Invalidate();
            if (error == null)
            {
                lblDoneTitle.Text = "GamesHub removido";
                lblDoneText.Text = (purge ? "O aplicativo e os seus dados foram apagados." : "O aplicativo foi removido. Seus dados ficaram em\n" + Product.DataDir
                    + "\ncaso você reinstale depois.") + "\nObrigado por jogar com o GamesHub!";
            }
            else
            {
                lblDoneTitle.Text = "Não foi possível concluir";
                lblDoneText.Text = error.Message + (InstallerLog.FilePath != null ? "\nDetalhes em " + InstallerLog.FilePath : "");
            }
            ShowPage(pDone);
            Footer.SetButtons(null, btnClose);
            AcceptButton = btnClose; CancelButton = btnClose;
            btnClose.Focus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy) { e.Cancel = true; return; }
            base.OnFormClosing(e);
        }
    }
}
