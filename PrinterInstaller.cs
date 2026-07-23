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

            var infFiles = Directory.EnumerateFiles(folderPath, "*.inf", SearchOption.AllDirectories)
                .Where(IsLikelyPrinterDriverInf)
                .OrderBy(path => path)
                .ToList();

            if (!infFiles.Any())
            {
                throw new InvalidOperationException("No INF driver files were found in that folder. Extract the vendor driver package first, then choose the extracted folder.");
            }

            var result = new DriverStagingSummary();
            foreach (var infFile in infFiles)
            {
                var output = RunProcessWithOutput("pnputil.exe", "/add-driver " + Quote(infFile) + " /install", false);
                if (IsSuccessfulPnPOutput(output))
                {
                    result.AddSuccess(infFile, output);
                }
                else
                {
                    result.AddFailure(infFile, output);
                }
            }

            if (result.SuccessCount == 0)
            {
                throw new InvalidOperationException(result.BuildMessage());
            }

            return result.BuildMessage();
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
                throw new InvalidOperationException("Choose an installed model-specific PCL/PCL6 driver that is not universal and not v4. If the dropdown is empty, use Install Driver first.");
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

        public static string RunProcessWithOutput(string fileName, string arguments, bool throwOnError)
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

        private static bool IsLikelyPrinterDriverInf(string path)
        {
            var fileName = Path.GetFileName(path);
            if (fileName.Equals("oemsetup.inf", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("setup.inf", StringComparison.OrdinalIgnoreCase) ||
                fileName.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return true;
        }

        private static bool IsSuccessfulPnPOutput(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return false;
            }

            return output.IndexOf("Driver package added successfully", StringComparison.OrdinalIgnoreCase) >= 0 ||
                output.IndexOf("Driver package installed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                output.IndexOf("Published Name", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AppendLine(StringBuilder builder, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                builder.AppendLine(value);
            }
        }
    }

    internal sealed class DriverStagingSummary
    {
        private readonly List<string> _successes = new List<string>();
        private readonly List<string> _failures = new List<string>();

        public int SuccessCount
        {
            get { return _successes.Count; }
        }

        public int FailureCount
        {
            get { return _failures.Count; }
        }

        public void AddSuccess(string infFile, string output)
        {
            _successes.Add(Path.GetFileName(infFile) + Environment.NewLine + TrimOutput(output));
        }

        public void AddFailure(string infFile, string output)
        {
            _failures.Add(Path.GetFileName(infFile) + Environment.NewLine + TrimOutput(output));
        }

        public string BuildMessage()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Driver staging finished.");
            builder.AppendLine("Succeeded: " + SuccessCount);
            builder.AppendLine("Failed or skipped: " + FailureCount);

            if (_failures.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Failed items:");
                foreach (var failure in _failures.Take(5))
                {
                    builder.AppendLine(failure);
                    builder.AppendLine();
                }
            }

            return builder.ToString().Trim();
        }

        private static string TrimOutput(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return "No output was returned.";
            }

            return output.Length > 800 ? output.Substring(0, 800).Trim() + "..." : output.Trim();
        }
    }
}
