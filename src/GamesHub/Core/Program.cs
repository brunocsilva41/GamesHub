// OWNER: CORE agent. Skeleton created by lead — replace entirely.
using System;
using System.Windows.Forms;

namespace GamesHub
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            AppPaths.EnsureDirs();
            Log.Info("GamesHub " + AppInfo.Version + " (skeleton)");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            MessageBox.Show("Skeleton build", AppInfo.Name);
        }
    }
}
