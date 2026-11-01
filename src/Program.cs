using System;
using System.IO;
using System.Windows.Forms;

namespace PaifuViewer
{
    static class Program
    {
        /// <summary>引数に牌譜 JSON のパスを渡すと、起動時にそのファイルを読み込む。</summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var form = new MainForm();
            if (args.Length > 0 && File.Exists(args[0])) form.PendingFile = args[0];
            Application.Run(form);
        }
    }
}
