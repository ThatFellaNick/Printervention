/*
  Printervention
  Main application entry point for the Windows Forms executable.
*/

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
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

            if (!EnsureAdministrativeContext())
            {
                return;
            }

            Application.Run(new MainForm());
        }

        private static bool EnsureAdministrativeContext()
        {
            if (IsAdministratorOrSystem())
            {
                return true;
            }

            var response = MessageBox.Show(
                "Printervention needs administrator rights to stage print drivers, create TCP/IP ports, and install print queues. Relaunch as administrator now?",
                "Administrator rights needed",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (response != DialogResult.Yes)
            {
                return true;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    UseShellExecute = true,
                    Verb = "runas"
                });

                return false;
            }
            catch (Win32Exception)
            {
                MessageBox.Show("Windows did not grant administrator rights. Printervention will continue, but install actions may fail.", "Elevation cancelled", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return true;
            }
        }

        private static bool IsAdministratorOrSystem()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                if (identity == null)
                {
                    return false;
                }

                if (identity.IsSystem)
                {
                    return true;
                }

                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
    }
}
