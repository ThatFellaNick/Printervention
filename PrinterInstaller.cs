/*
  Printervention
  Windows printer queue and driver-store integration.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Printing;
using System.Linq;
using System.Management;

namespace Printervention
{
    internal sealed class PrinterInstaller
    {
        public IList<string> GetInstalledPclDrivers()
        {
            var drivers = new List<string>();

            using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PrinterDriver"))
            {
                foreach (ManagementObject driver in searcher.Get())
                {
                    var name = Convert.ToString(driver["Name"]);
                    if (DriverCatalog.IsAllowedDriverName(name))
                    {
                        drivers.Add(name);
                    }
                }
            }

            return drivers.OrderBy(name => name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public void CreateQueue(string ipAddress, string printerName, string driverName)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                throw new ArgumentException("Enter a printer IP address first.", "ipAddress");
            }

            if (string.IsNullOrWhiteSpace(printerName))
            {
                throw new ArgumentException("Enter a printer name.", "printerName");
            }

            if (!DriverCatalog.IsAllowedDriverName(driverName))
            {
                throw new InvalidOperationException("Choose an installed PCL/PCL6 driver that is not v4.");
            }

            var portName = "IP_" + ipAddress.Trim();
            EnsureTcpIpPort(portName, ipAddress.Trim());
            EnsurePrinterDoesNotExist(printerName.Trim());

            RunPowerShell("Add-Printer -Name " + PsQuote(printerName.Trim()) + " -DriverName " + PsQuote(driverName.Trim()) + " -PortName " + PsQuote(portName), true);
            ApplyPrinterDefaults(printerName.Trim());
        }

        public void OpenWindowsPrinterSettings()
        {
            Process.Start(new ProcessStartInfo("control.exe", "printers") { UseShellExecute = true });
        }

        private static void EnsureTcpIpPort(string portName, string ipAddress)
        {
            using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_TCPIPPrinterPort WHERE Name='" + EscapeWql(portName) + "'"))
            {
                if (searcher.Get().Count > 0)
                {
                    return;
                }
            }

            var portClass = new ManagementClass("Win32_TCPIPPrinterPort");
            using (var port = portClass.CreateInstance())
            {
                port["Name"] = portName;
                port["HostAddress"] = ipAddress;
                port["PortNumber"] = 9100;
                port["Protocol"] = 1;
                port["SNMPEnabled"] = true;
                port.Put();
            }
        }

        private static void ApplyPrinterDefaults(string printerName)
        {
            // Win32_Printer exposes the common defaults most admins expect to set after queue creation.
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer WHERE Name='" + EscapeWql(printerName) + "'"))
            {
                foreach (ManagementObject printer in searcher.Get())
                {
                    printer["Default"] = false;
                    printer["Duplex"] = false;
                    printer["EnableBIDI"] = true;
                    printer.Put();
                }
            }

            TrySetPrinterSettings(printerName);
            RunPowerShell("Set-PrintConfiguration -PrinterName " + PsQuote(printerName) + " -Color $false -DuplexingMode OneSided", false);
        }

        private static void TrySetPrinterSettings(string printerName)
        {
            try
            {
                var settings = new PrinterSettings { PrinterName = printerName };
                settings.DefaultPageSettings.Color = false;
                settings.DefaultPageSettings.PrinterSettings.Duplex = Duplex.Simplex;
            }
            catch
            {
                // Some drivers do not expose defaults through managed print settings.
            }
        }

        private static void EnsurePrinterDoesNotExist(string printerName)
        {
            using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Printer WHERE Name='" + EscapeWql(printerName) + "'"))
            {
                if (searcher.Get().Count > 0)
                {
                    throw new InvalidOperationException("A printer queue with that name already exists.");
                }
            }
        }

        private static void RunPowerShell(string command, bool throwOnError)
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " + Quote(command),
                UseShellExecute = false,
                CreateNoWindow = true
            });

            process.WaitForExit();
            if (throwOnError && process.ExitCode != 0)
            {
                throw new InvalidOperationException("Windows could not create the printer queue. Run as administrator and confirm the driver is installed.");
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string PsQuote(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        private static string EscapeWql(string value)
        {
            return value.Replace("\\", "\\\\").Replace("'", "\\'");
        }
    }
}
