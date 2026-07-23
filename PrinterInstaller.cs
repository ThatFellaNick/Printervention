/*
  Printervention
  Windows printer queue and driver-store integration.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Text;

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

        public string StageDriverFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                throw new ArgumentException("Choose a folder that contains the extracted driver files.", "folderPath");
            }

            if (!Directory.EnumerateFiles(folderPath, "*.inf", SearchOption.AllDirectories).Any())
            {
                throw new InvalidOperationException("No INF driver files were found in that folder. Extract the vendor driver package first, then choose the extracted folder.");
            }

            // pnputil stages matching INF packages from the selected folder into the Windows driver store.
            return RunProcessWithOutput("pnputil.exe", "/add-driver " + Quote(Path.Combine(folderPath, "*.inf")) + " /subdirs /install", true);
        }

        public void CreateQueue(string ipAddress, string printerName, string driverName)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                throw new ArgumentException("Enter a printer IP address first.", "ipAddress");
            }

            IPAddress parsedIp;
            if (!IPAddress.TryParse(ipAddress.Trim(), out parsedIp))
            {
                throw new ArgumentException("The printer IP address is not valid.", "ipAddress");
            }

            if (string.IsNullOrWhiteSpace(printerName))
            {
                throw new ArgumentException("Enter a printer name.", "printerName");
            }

            if (!DriverCatalog.IsAllowedDriverName(driverName))
            {
                throw new InvalidOperationException("Choose an installed model-specific PCL/PCL6 driver that is not universal and not v4. If the dropdown is empty, download and extract the vendor package, then use Stage Driver Folder.");
            }

            var portName = "IP_" + parsedIp;
            EnsureTcpIpPort(portName, parsedIp.ToString());
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
            RunProcessWithOutput("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command " + Quote(command), throwOnError);
        }

        private static string RunProcessWithOutput(string fileName, string arguments, bool throwOnError)
        {
            var output = new StringBuilder();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.OutputDataReceived += (sender, args) => AppendLine(output, args.Data);
            process.ErrorDataReceived += (sender, args) => AppendLine(output, args.Data);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            var text = output.ToString().Trim();
            if (throwOnError && process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(text)
                    ? "Windows could not complete the printer driver action. Run as administrator and confirm the driver package is valid."
                    : text);
            }

            return text;
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

        private static void AppendLine(StringBuilder builder, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                builder.AppendLine(value);
            }
        }
    }
}
