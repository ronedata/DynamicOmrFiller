using System;
using System.Windows.Forms;

namespace DynamicOmrFiller
{
    internal static class AppEntry
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
