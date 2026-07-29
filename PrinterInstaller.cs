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
using System.Text.RegularExpressions;

namespace Printervention
{
    internal sealed class PrinterInstaller
    {
        public IList<string> GetInstalledPclDrivers()
        {
            return GetInstalledPclDrivers(null);
        }

        public IList<string> GetInstalledPclDrivers(string preferredModel)
        {
            return GetInstalledPclDrivers(preferredModel, null);
        }

        public IList<string> GetInstalledPclDrivers(string preferredModel, string preferredVendor)
        {
            var drivers = new List<string>();

            using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PrinterDriver"))
            {
                foreach (ManagementObject driver in searcher.Get())
                {
                    var name = Convert.ToString(driver["Name"]);
                    var cleanName = NormalizeDriverName(name);
                    if (DriverCatalog.IsCompatibleDriverName(cleanName, preferredModel, preferredVendor))
                    {
                        drivers.Add(cleanName);
                    }
                }
            }

            return drivers.OrderBy(name => name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public string StageDriverFolder(string folderPath)
        {
            return StageDriverFolder(folderPath, null);
        }

        public string StageDriverFolder(string folderPath, string preferredModel)
        {
            return StageDriverFolder(folderPath, preferredModel, null);
        }

        public string StageDriverFolder(string folderPath, string preferredModel, string preferredVendor)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                throw new ArgumentException("Choose a folder that contains the extracted driver files.", "folderPath");
            }

            var infFiles = Directory.EnumerateFiles(folderPath, "*.inf", SearchOption.AllDirectories)
                .Where(IsLikelyPrinterDriverInf)
                .Where(IsNativeArchitectureInf)
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
                    RegisterMatchingPrintDrivers(infFile, preferredModel, preferredVendor, result);
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
            CreateQueue(ipAddress, printerName, driverName, printerName);
        }

        public void CreateQueue(string ipAddress, string printerName, string driverName, string preferredModel)
        {
            CreateQueue(ipAddress, printerName, driverName, preferredModel, null);
        }

        public void CreateQueue(string ipAddress, string printerName, string driverName, string preferredModel, string preferredVendor)
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

            var normalizedDriverName = NormalizeDriverName(driverName);
            if (!DriverCatalog.IsCompatibleDriverName(normalizedDriverName, preferredModel, preferredVendor))
            {
                throw new InvalidOperationException("Choose an approved exact-model printer driver for this brand and model. Brother and Epson names may omit PCL; HP Universal Printing PCL 6 is the only universal exception; v4 remains blocked. If the dropdown is empty, add the printer to the install list so Printervention can stage a driver.");
            }

            var portName = "IP_" + parsedIp;
            EnsureTcpIpPort(portName, parsedIp.ToString());
            EnsurePrinterDoesNotExist(printerName.Trim());

            CreatePrinterQueueWithFallbacks(printerName.Trim(), portName, normalizedDriverName);
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
            TryApplyWmiPrinterDefaults(printerName);
            TrySetPrinterSettings(printerName);
            RunPowerShell("Set-PrintConfiguration -PrinterName " + PsQuote(printerName) + " -Color $false -DuplexingMode OneSided", false);
            TryApplyPrintTicketDefaults(printerName);
            RunPowerShell("Get-Printer -Name " + PsQuote(printerName) + " | Set-Printer -EnableBidi $true", false);
        }

        private static void TryApplyPrintTicketDefaults(string printerName)
        {
            // Canon exposes Auto Color Detection as a separate private feature. Setting only the
            // standard monochrome flag leaves the Canon UI at Auto [Color/B&W].
            var command =
                "$configuration = Get-PrintConfiguration -PrinterName " + PsQuote(printerName) + "; " +
                "[xml]$ticket = $configuration.PrintTicketXml; " +
                "$namespaces = New-Object System.Xml.XmlNamespaceManager($ticket.NameTable); " +
                "$namespaces.AddNamespace('psf', 'http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework'); " +
                "$colorOption = $ticket.SelectSingleNode(\"//psf:Feature[@name='psk:PageOutputColor']/psf:Option\", $namespaces); " +
                "if ($colorOption) { $colorOption.SetAttribute('name', 'psk:Monochrome') }; " +
                "$autoFeature = $ticket.SelectSingleNode(\"//psf:Feature[contains(@name, ':PageOutputColorAutoDetection')]\", $namespaces); " +
                "if ($autoFeature) { " +
                "  $autoOption = $autoFeature.SelectSingleNode('psf:Option', $namespaces); " +
                "  $featureName = $autoFeature.GetAttribute('name'); " +
                "  $prefixEnd = $featureName.IndexOf(':'); " +
                "  if ($autoOption -and $prefixEnd -gt 0) { $autoOption.SetAttribute('name', $featureName.Substring(0, $prefixEnd) + ':None') } " +
                "}; " +
                "Set-PrintConfiguration -PrinterName " + PsQuote(printerName) + " -PrintTicketXml $ticket.OuterXml";

            RunPowerShell(command, false);
        }

        private static void TryApplyWmiPrinterDefaults(string printerName)
        {
            try
            {
                // Win32_Printer exposes common queue defaults, but some vendor drivers throw after creation.
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer WHERE Name='" + EscapeWql(printerName) + "'"))
                {
                    foreach (ManagementObject printer in searcher.Get())
                    {
                        try
                        {
                            printer["Default"] = false;
                            printer["Duplex"] = false;
                            printer["EnableBIDI"] = true;
                            printer.Put();
                        }
                        catch
                        {
                            // Driver-specific WMI failures should not block PrintConfiguration defaults.
                        }
                    }
                }
            }
            catch
            {
                // The PowerShell print module still gets a chance to apply B&W and simplex defaults.
            }
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
            if (PrinterExists(printerName))
            {
                throw new InvalidOperationException("A printer queue with that name already exists.");
            }
        }

        private static bool PrinterExists(string printerName)
        {
            using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Printer WHERE Name='" + EscapeWql(printerName) + "'"))
            {
                return searcher.Get().Count > 0;
            }
        }

        private static void CreatePrinterQueueWithFallbacks(string printerName, string portName, string driverName)
        {
            var attempted = new List<string>();
            Exception firstError = null;

            // Vendor packages can expose one friendly model name in the UI and a different exact
            // print-driver name to Add-Printer, especially Ricoh-family packages.
            foreach (var candidate in ResolveInstalledDriverNameCandidates(driverName))
            {
                try
                {
                    attempted.Add("Add-Printer: " + candidate);
                    RunPowerShell("Add-Printer -Name " + PsQuote(printerName) + " -DriverName " + PsQuote(candidate) + " -PortName " + PsQuote(portName), true);
                    if (PrinterExists(printerName))
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    if (PrinterExists(printerName))
                    {
                        return;
                    }

                    if (firstError == null)
                    {
                        firstError = ex;
                    }
                }
            }

            foreach (var candidate in ResolveInstalledDriverNameCandidates(driverName))
            {
                attempted.Add("PrintUI: " + candidate);
                TryCreateQueueWithPrintUi(printerName, portName, candidate, false);
                if (PrinterExists(printerName))
                {
                    return;
                }

                TryCreateQueueWithPrintUi(printerName, portName, candidate, true);
                if (PrinterExists(printerName))
                {
                    return;
                }
            }

            var message = new StringBuilder();
            message.AppendLine("Windows could not create the printer queue with the selected driver.");
            message.AppendLine();
            message.AppendLine("Tried:");
            foreach (var item in attempted.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                message.AppendLine("- " + item);
            }

            if (firstError != null)
            {
                message.AppendLine();
                message.AppendLine("First Windows error:");
                message.AppendLine(firstError.Message);
            }

            throw new InvalidOperationException(message.ToString().Trim());
        }

        private static IEnumerable<string> ResolveInstalledDriverNameCandidates(string driverName)
        {
            var normalizedDriverName = NormalizeDriverName(driverName);
            var candidates = new List<string> { normalizedDriverName };

            foreach (var installedName in GetInstalledPrinterDriverNames())
            {
                var normalizedInstalledName = NormalizeDriverName(installedName);
                if (normalizedInstalledName.Equals(normalizedDriverName, StringComparison.OrdinalIgnoreCase) ||
                    installedName.Equals(normalizedDriverName, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(installedName);
                    candidates.Add(normalizedInstalledName);
                }
            }

            return candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<string> GetInstalledPrinterDriverNames()
        {
            var names = new List<string>();

            var powerShellOutput = RunProcessWithOutput("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command " + Quote("Get-PrinterDriver | Select-Object -ExpandProperty Name"), false);
            foreach (var line in SplitLines(powerShellOutput))
            {
                if (DriverCatalog.IsAllowedDriverName(line))
                {
                    names.Add(line.Trim());
                }
            }

            using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PrinterDriver"))
            {
                foreach (ManagementObject driver in searcher.Get())
                {
                    var name = Convert.ToString(driver["Name"]);
                    if (DriverCatalog.IsAllowedDriverName(NormalizeDriverName(name)))
                    {
                        names.Add(name);
                    }
                }
            }

            return names;
        }

        private static void TryCreateQueueWithPrintUi(string printerName, string portName, string driverName, bool includeInboxInf)
        {
            var arguments = "printui.dll,PrintUIEntry /if /b " + Quote(printerName) +
                " /r " + Quote(portName) +
                " /m " + Quote(driverName);

            if (includeInboxInf)
            {
                var inboxInf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "inf", "ntprint.inf");
                arguments += " /f " + Quote(inboxInf);
            }

            RunProcessWithOutput("rundll32.exe", arguments, false);
        }

        private static void RunPowerShell(string command, bool throwOnError)
        {
            RunProcessWithOutput("powershell.exe", "-NoProfile -Command " + Quote(command), throwOnError);
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
            if (fileName.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            // Vendor bundles also contain setup and USB utility INFs. A real Type 3 print INF
            // declares the Printer class and a manufacturer section, including Kyocera OEMSETUP.INF.
            var header = File.ReadLines(path).Take(500).ToArray();
            return header.Any(line => line.Trim().Equals("[Manufacturer]", StringComparison.OrdinalIgnoreCase)) &&
                header.Any(line => Regex.IsMatch(line, @"^\s*Class\s*=\s*Printer\s*$", RegexOptions.IgnoreCase));
        }

        private static bool IsNativeArchitectureInf(string path)
        {
            var segments = path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            var has64BitFolder = segments.Any(segment =>
                segment.Equals("x64", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("amd64", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("64bit", StringComparison.OrdinalIgnoreCase));
            var has32BitFolder = segments.Any(segment =>
                segment.Equals("x86", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("i386", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("32bit", StringComparison.OrdinalIgnoreCase));
            var hasArm64Folder = segments.Any(segment =>
                segment.Equals("arm64", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("aarch64", StringComparison.OrdinalIgnoreCase));
            var isArm64OperatingSystem = IsArm64OperatingSystem();

            if (hasArm64Folder && !isArm64OperatingSystem)
            {
                return false;
            }

            if (isArm64OperatingSystem && (has64BitFolder || has32BitFolder) && !hasArm64Folder)
            {
                return false;
            }

            if (Environment.Is64BitOperatingSystem && has32BitFolder && !has64BitFolder)
            {
                return false;
            }

            if (!Environment.Is64BitOperatingSystem && has64BitFolder && !has32BitFolder)
            {
                return false;
            }

            // Some vendors do not use architecture folder names, so inspect the INF decorations too.
            var architectureLines = File.ReadLines(path).Take(500).ToArray();
            var targetsAmd64 = architectureLines.Any(line => line.IndexOf("NTamd64", StringComparison.OrdinalIgnoreCase) >= 0);
            var targetsX86 = architectureLines.Any(line => line.IndexOf("NTx86", StringComparison.OrdinalIgnoreCase) >= 0);
            var targetsArm64 = architectureLines.Any(line => line.IndexOf("NTarm64", StringComparison.OrdinalIgnoreCase) >= 0);

            if (isArm64OperatingSystem)
            {
                return targetsArm64 || (!targetsAmd64 && !targetsX86);
            }

            if (Environment.Is64BitOperatingSystem)
            {
                return targetsAmd64 || (!targetsX86 && !targetsArm64);
            }

            return targetsX86 || (!targetsAmd64 && !targetsArm64);
        }

        private static string NormalizeDriverName(string driverName)
        {
            if (string.IsNullOrWhiteSpace(driverName))
            {
                return string.Empty;
            }

            var marker = driverName.IndexOf(",3,", StringComparison.OrdinalIgnoreCase);
            return marker > 0 ? driverName.Substring(0, marker).Trim() : driverName.Trim();
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

        private static IEnumerable<string> SplitLines(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new string[0];
            }

            return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line));
        }

        private static void RegisterMatchingPrintDrivers(string infFile, string preferredModel, string preferredVendor, DriverStagingSummary result)
        {
            var registeredCount = 0;
            var previouslyRegistered = new HashSet<string>(result.RegisteredDriverNames, StringComparer.OrdinalIgnoreCase);
            var names = GetDriverNamesFromInf(infFile)
                .Where(name => DriverCatalog.IsCompatibleDriverName(name, preferredModel, preferredVendor))
                .Where(name => !previouslyRegistered.Contains(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(name => ScoreDriverName(name, preferredModel, preferredVendor))
                .ToList();

            foreach (var name in names.Take(12))
            {
                var output = TryRegisterPrintDriver(infFile, name);
                if (IsPrintDriverRegistrationSuccess(output))
                {
                    result.AddRegistration(name, output);
                    registeredCount++;
                    if (registeredCount >= 4 && HasExactVendorRegistration(registeredCount, preferredVendor, result))
                    {
                        return;
                    }
                }
            }
        }

        private static bool HasExactVendorRegistration(int registeredCount, string preferredVendor, DriverStagingSummary result)
        {
            return registeredCount > 0 &&
                !string.IsNullOrWhiteSpace(preferredVendor) &&
                result.RegisteredDriverNames.Any(name => DriverCatalog.IsExactVendorMatch(preferredVendor, name));
        }

        private static IEnumerable<string> GetDriverNamesFromInf(string infFile)
        {
            foreach (var line in File.ReadLines(infFile))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith(";", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
                {
                    continue;
                }

                var equalsIndex = trimmed.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }

                var name = trimmed.Substring(0, equalsIndex).Trim().Trim('"');
                if (name.Length > 2)
                {
                    yield return name;
                }
            }
        }

        private static int ScoreDriverName(string driverName, string preferredModel, string preferredVendor)
        {
            var score = 0;
            var lowered = driverName.ToLowerInvariant();
            if (lowered.Contains("pcl 6") || lowered.Contains("pcl6"))
            {
                score += 30;
            }

            if (!string.IsNullOrWhiteSpace(preferredModel))
            {
                foreach (var term in preferredModel.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (term.Any(char.IsDigit) && driverName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score += 20;
                    }
                }
            }

            if (DriverCatalog.IsExactVendorMatch(preferredVendor, driverName))
            {
                score += 80;
            }
            else if (DriverCatalog.IsVendorFamilyMatch(preferredVendor, driverName))
            {
                // Ricoh-family packages may contain Savin, Lanier, or Gestetner siblings. Keep them
                // viable, but prefer the selected/discovered brand when that exact name is present.
                score += 35;
            }

            return score;
        }

        private static string TryRegisterPrintDriver(string infFile, string driverName)
        {
            var architecture = IsArm64OperatingSystem()
                ? "ARM64"
                : Environment.Is64BitOperatingSystem ? "x64" : "Intel";
            var arguments = "printui.dll,PrintUIEntry /ia /m " + Quote(driverName) +
                " /h " + Quote(architecture) +
                " /v " + Quote("Type 3 - User Mode") +
                " /f " + Quote(infFile);
            return RunProcessWithOutput("rundll32.exe", arguments, false);
        }

        private static bool IsArm64OperatingSystem()
        {
            var processorArchitecture = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ??
                Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? string.Empty;
            return processorArchitecture.Equals("ARM64", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPrintDriverRegistrationSuccess(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                // PrintUI often returns success with no console output.
                return true;
            }

            return output.IndexOf("error", StringComparison.OrdinalIgnoreCase) < 0 &&
                output.IndexOf("failed", StringComparison.OrdinalIgnoreCase) < 0;
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
        private readonly List<string> _registrations = new List<string>();

        public int SuccessCount
        {
            get { return _successes.Count; }
        }

        public int FailureCount
        {
            get { return _failures.Count; }
        }

        public int RegistrationCount
        {
            get { return _registrations.Count; }
        }

        public IEnumerable<string> RegisteredDriverNames
        {
            get
            {
                return _registrations.Select(registration =>
                    registration.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).FirstOrDefault() ?? string.Empty);
            }
        }

        public void AddSuccess(string infFile, string output)
        {
            _successes.Add(Path.GetFileName(infFile) + Environment.NewLine + TrimOutput(output));
        }

        public void AddFailure(string infFile, string output)
        {
            _failures.Add(Path.GetFileName(infFile) + Environment.NewLine + TrimOutput(output));
        }

        public void AddRegistration(string driverName, string output)
        {
            _registrations.Add(driverName + Environment.NewLine + TrimOutput(output));
        }

        public string BuildMessage()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Driver staging finished.");
            builder.AppendLine("Succeeded: " + SuccessCount);
            builder.AppendLine("Registered print drivers: " + RegistrationCount);
            builder.AppendLine("Failed or skipped: " + FailureCount);

            if (_registrations.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Registered:");
                foreach (var registration in _registrations.Take(5))
                {
                    builder.AppendLine(registration);
                    builder.AppendLine();
                }
            }

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
