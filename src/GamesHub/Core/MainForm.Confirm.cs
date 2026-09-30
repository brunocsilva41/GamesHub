using System;
using System.Windows.Forms;

namespace GamesHub
{
    /// <summary>Native confirmations for actions the web page must not be able to perform on its own
    /// (running programs, starting uninstallers): the page cannot draw, click or dismiss these.</summary>
    internal sealed partial class MainForm
    {
        private bool _confirming;

        /// <summary>Yes/No warning owned by the main window, "Não" (No) focused by default. Returns false when
        /// another confirmation is already open (a page cannot stack prompts) or the user declines.</summary>
        public bool ConfirmDangerous(string title, string text, string yesHint)
        {
            if (InvokeRequired) return (bool)Invoke(new Func<bool>(() => ConfirmDangerous(title, text, yesHint)));
            if (_confirming || IsDisposed) return false;
            _confirming = true;
            try
            {
                IWin32Window owner = Visible ? this : null;
                string body = text + (string.IsNullOrEmpty(yesHint) ? "" : "\n\n" + yesHint);
                return MessageBox.Show(owner, body, title + " — " + AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                                       MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            }
            finally
            {
                _confirming = false;
            }
        }
    }
}
