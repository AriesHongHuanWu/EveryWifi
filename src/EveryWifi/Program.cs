using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using EveryWifi.UI;

namespace EveryWifi
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool newInstance;
            using (var mutex = new Mutex(true, "Local\\EveryWifi-" + Environment.UserName, out newInstance))
            {
                if (!newInstance) { MessageBox.Show("EveryWifi 已在執行，請從右下角通知區域開啟。", "EveryWifi"); return; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (sender, error) => MessageBox.Show(error.Exception.Message, "EveryWifi · 操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Application.Run(new MainForm(args.Contains("--tray"), args.Contains("--preview")));
            }
        }
    }
}
