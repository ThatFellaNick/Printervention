/*
  Printervention
  Main application entry point for the Windows Forms executable.
*/

using System;
using System.Windows.Forms;

namespace Printervention
{
    internal static class Program
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
