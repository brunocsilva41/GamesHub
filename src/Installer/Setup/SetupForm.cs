// OWNER: DIST agent. Setup wizard: welcome → options → progress → done.
using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace GamesHub.Installer
{
    internal sealed class SetupForm : BrandForm
    {
        public int ExitCode { get; private set; } = 2; // 0 ok, 1 error, 2 cancelled

        private readonly SetupOptions opts;
        private readonly Panel pWelcome, pOptions, pProgress, pDone;
        private readonly BrandButton btnBack = new BrandButton("‹  Voltar", ButtonKind.Ghost);
        private readonly BrandButton btnCancel = new BrandButton("Cancelar");
        private readonly BrandButton btnNext = new BrandButton("Avançar  ›", ButtonKind.Primary);
        private readonly BrandButton btnInstall = new BrandButton("Instalar", ButtonKind.Primary);
        private readonly BrandButton btnFinish = new BrandButton("Concluir", ButtonKind.Primary);
        private readonly BrandButton btnClose = new BrandButton("Fechar");
        private BrandPathBox pathInstall, pathGames;
        private BrandCheck chkDesktop, chkStart, chkAutostart, chkOpen;
        private Label lblStatus, lblPercent, lblProgressTitle, lblDoneTitle, lblDoneText;
        private BrandProgress progress;
        private StatusBadge badge;
        private Callout doneTip;
        private SetupResult result;
        private bool busy;

        public SetupForm(SetupOptions o)
            : base("Instalar o GamesHub", "GamesHub", "Instalador · versão " + Product.Version, Payload.Logo())
        {
            opts = o;
            pWelcome = BuildWelcome();
            pOptions = BuildOptions();
            pProgress = BuildProgress();
            pDone = BuildDone();

            btnCancel.Click += (s, e) => Close();
            btnClose.Click += (s, e) => Close();
            btnNext.Click += (s, e) => GoOptions();
            btnBack.Click += (s, e) => GoWelcome();
            btnInstall.Click += (s, e) => StartInstall();
            btnFinish.Click += (s, e) => Finish();

            CompleteLayout();
            GoWelcome();
        }

        // ------------------------------------------------------------------ pages

        private Panel BuildWelcome()
        {
            Panel p = NewPage();
            ExistingInstall ex = ExistingInstall.Detect(opts.InstallDir);
            AddLabel(p, ex.Any ? "Atualizar o GamesHub" : "Bem-vindo ao GamesHub", 0, 0, PageWidth, 40, Brand.Display(18f), Brand.Text);
            AddLabel(p, "Uma biblioteca leve e bonita para todos os seus jogos de PC — Steam, Epic e os atalhos da sua pasta de jogos, reunidos num só lugar.",
                1, 44, PageWidth - 20, 44, Brand.Font(10f), Brand.Muted);
            Place(p, new BulletList(
                "Importação automática da Steam e da Epic Games",
                "Capas e artes em alta resolução, baixadas sozinhas",
                "Tempo de jogo, favoritos e coleções",
                "Teclado, controle e modo Big Picture"), 2, 100, PageWidth, 108);

            string note;
            if (ex.HasV1)
                note = "Encontramos a versão anterior (1.x) em " + opts.InstallDir + ". Ela será atualizada para a " + Product.Version
                     + " — seus jogos, capas e histórico são preservados.";
            else if (ex.HasV2)
                note = "O GamesHub " + ex.Version + " já está instalado. Continuar vai atualizar para a " + Product.Version + " mantendo seus dados.";
            else
                note = "Instalação só para o seu usuário: não precisa de permissão de administrador e não instala nada além do GamesHub.";
            if (Prereqs.WebView2Version() == null)
                note = "Atenção: o Microsoft Edge WebView2 Runtime não foi encontrado. O GamesHub precisa dele para abrir — "
                     + "baixe gratuitamente em go.microsoft.com/fwlink/p/?LinkId=2124703.";
            else if (!Prereqs.HasNet48())
                note = "Atenção: o .NET Framework 4.8 não foi encontrado. Instale-o pelo Windows Update antes de usar o GamesHub.";
            Place(p, new Callout(note), 0, 226, PageWidth, 66);
            return p;
        }

        private Panel BuildOptions()
        {
            Panel p = NewPage();
            AddLabel(p, "Pasta de instalação", 0, 0, 300, 20, Brand.Font(9f, FontStyle.Bold), Brand.Muted);
            pathInstall = Place(p, new BrandPathBox(opts.InstallDir) { DialogDescription = "Onde instalar o GamesHub" }, 0, 22, PageWidth, 36);
            AddLabel(p, "Pasta de jogos", 0, 70, 300, 20, Brand.Font(9f, FontStyle.Bold), Brand.Muted);
            pathGames = Place(p, new BrandPathBox(opts.GamesDir) { DialogDescription = "Pasta onde ficam os atalhos dos seus jogos" }, 0, 92, PageWidth, 36);
            AddLabel(p, "Onde ficam os atalhos dos seus jogos. O GamesHub nunca apaga nada desta pasta.",
                1, 132, PageWidth, 20, Brand.Font(8.75f), Brand.Dim);
            chkDesktop = Place(p, new BrandCheck("Criar atalho na Área de Trabalho", opts.DesktopShortcut), 0, 166, PageWidth, 28);
            chkStart = Place(p, new BrandCheck("Criar atalho no Menu Iniciar", opts.StartMenuShortcut), 0, 198, PageWidth, 28);
            chkAutostart = Place(p, new BrandCheck("Iniciar com o Windows (minimizado na bandeja)", opts.StartWithWindows), 0, 230, PageWidth, 28);
            long size = Payload.UncompressedSize();
            if (size > 0)
                AddLabel(p, "Espaço necessário: " + (size / 1048576.0).ToString("0.0") + " MB", 0, 272, PageWidth, 20,
                    Brand.Font(8.75f), Brand.Dim, ContentAlignment.MiddleRight);
            return p;
        }

        private Panel BuildProgress()
        {
            Panel p = NewPage();
            lblProgressTitle = AddLabel(p, "Instalando o GamesHub…", 0, 36, PageWidth, 36, Brand.Display(16f), Brand.Text);
            lblStatus = AddLabel(p, "Preparando…", 1, 80, PageWidth, 22, Brand.Font(9.5f), Brand.Muted);
            progress = Place(p, new BrandProgress(), 0, 116, PageWidth, 8);
            lblPercent = AddLabel(p, "0%", 0, 132, PageWidth, 20, Brand.Font(8.75f), Brand.Dim, ContentAlignment.TopRight);
            return p;
        }

        private Panel BuildDone()
        {
            Panel p = NewPage();
            badge = Place(p, new StatusBadge(), 0, 8, 60, 60);
            lblDoneTitle = AddLabel(p, "Tudo pronto!", 80, 6, PageWidth - 80, 36, Brand.Display(18f), Brand.Text);
            lblDoneText = AddLabel(p, "", 81, 44, PageWidth - 81, 60, Brand.Font(9.5f), Brand.Muted);
            doneTip = Place(p, new Callout("Dica: pressione Ctrl+Alt+G em qualquer lugar para abrir o GamesHub. Arraste atalhos ou executáveis "
                + "para a janela para adicionar jogos — Steam e Epic são importados automaticamente."), 0, 124, PageWidth, 66);
            chkOpen = Place(p, new BrandCheck("Abrir GamesHub", true), 0, 212, PageWidth, 28);
            return p;
        }

        // ------------------------------------------------------------------ navigation

        private void GoWelcome()
        {
            ShowPage(pWelcome);
            Header.Step = "Etapa 1 de 3";
            Footer.SetButtons(null, btnCancel, btnNext);
            AcceptButton = btnNext; CancelButton = btnCancel;
            btnNext.Focus();
        }

        private void GoOptions()
        {
            ShowPage(pOptions);
            Header.Step = "Etapa 2 de 3";
            btnInstall.Text = "Instalar";
            Footer.SetButtons(btnBack, btnCancel, btnInstall);
            AcceptButton = btnInstall; CancelButton = btnCancel;
            btnInstall.Focus();
        }

        private void StartInstall()
        {
            if (pathInstall.Value.Length == 0) { Ask("Informe a pasta de instalação.", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (pathGames.Value.Length == 0) { Ask("Informe a pasta de jogos.", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            string dir;
            try { dir = SetupEngine.ResolveInstallDir(pathInstall.Value); }
            catch (Exception ex)
            {
                InstallerLog.Warn("Invalid install dir", ex);
                Ask("A pasta de instalação não é válida.", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Directory.Exists(pathGames.Value)
                && Ask("A pasta de jogos não existe:\n" + pathGames.Value + "\n\nEla será criada quando você adicionar o primeiro jogo. Continuar?")
                   != DialogResult.Yes)
                return;

            opts.InstallDir = dir;
            opts.GamesDir = pathGames.Value;
            opts.DesktopShortcut = chkDesktop.Checked;
            opts.StartMenuShortcut = chkStart.Checked;
            opts.StartWithWindows = chkAutostart.Checked;

            busy = true;
            ShowPage(pProgress);
            Header.Step = "Etapa 3 de 3";
            Footer.SetButtons(null);
            AcceptButton = null; CancelButton = null;
            progress.Value = 0;

            var engine = new SetupEngine
            {
                Progress = (pct, msg) => BeginInvoke((Action)(() => { progress.Value = pct; lblStatus.Text = msg; lblPercent.Text = pct + "%"; })),
                ConfirmCloseApp = () => (bool)Invoke((Func<bool>)(() =>
                    Ask("O GamesHub está aberto e será fechado para continuar a instalação.", MessageBoxButtons.OKCancel, MessageBoxIcon.Information)
                    == DialogResult.OK)),
                ConfirmKill = () => (bool)Invoke((Func<bool>)(() =>
                    Ask("O GamesHub não fechou sozinho (ele pode estar na bandeja do sistema, perto do relógio).\n\nForçar o encerramento?",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)),
            };
            var t = new Thread(() =>
            {
                try
                {
                    SetupResult r = engine.Run(opts);
                    BeginInvoke((Action)(() => OnInstalled(r)));
                }
                catch (OperationCanceledException)
                {
                    InstallerLog.Info("Install cancelled by user");
                    BeginInvoke((Action)(() => { busy = false; GoOptions(); }));
                }
                catch (Exception ex)
                {
                    InstallerLog.Error("Install failed", ex);
                    BeginInvoke((Action)(() => OnFailed(ex)));
                }
            }) { IsBackground = true, Name = "setup" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        private void OnInstalled(SetupResult r)
        {
            busy = false;
            result = r;
            ExitCode = 0;
            InstallerLog.WriteResult(SetupProgram.ResultFile, "OK:" + r.InstallDir);
            badge.Error = false;
            lblDoneTitle.Text = r.UpgradedFromV1 ? "Atualizado para a " + Product.Version + "!" : "Tudo pronto!";
            lblDoneText.Text = "O GamesHub " + Product.Version + " foi instalado em\n" + r.InstallDir;
            doneTip.Visible = chkOpen.Visible = true;
            ShowPage(pDone);
            Header.Step = "";
            Footer.SetButtons(null, btnFinish);
            AcceptButton = btnFinish; CancelButton = btnFinish;
            btnFinish.Focus();
        }

        private void OnFailed(Exception ex)
        {
            busy = false;
            ExitCode = 1;
            InstallerLog.WriteResult(SetupProgram.ResultFile, "ERRO:" + ex.Message);
            badge.Error = true;
            badge.Invalidate();
            lblDoneTitle.Text = "Não foi possível concluir";
            lblDoneText.Text = ex.Message + (InstallerLog.FilePath != null ? "\nDetalhes em " + InstallerLog.FilePath : "");
            doneTip.Visible = chkOpen.Visible = false;
            ShowPage(pDone);
            Header.Step = "";
            btnInstall.Text = "Tentar de novo";
            Footer.SetButtons(null, btnClose, btnInstall);
            AcceptButton = btnInstall; CancelButton = btnClose;
        }

        private void Finish()
        {
            if (result != null && chkOpen.Checked) SetupProgram.LaunchApp(result.ExePath);
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy) { e.Cancel = true; return; }
            if (ExitCode == 2 && e.CloseReason == CloseReason.UserClosing
                && Ask("Deseja cancelar a instalação do GamesHub?") != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            if (ExitCode == 2) InstallerLog.WriteResult(SetupProgram.ResultFile, "ERRO:cancelado pelo usuário");
            base.OnFormClosing(e);
        }
    }
}
