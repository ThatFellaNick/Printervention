/*
  Printervention
  Official vendor download discovery, package download, extraction, and staging helpers.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Printervention
{
    internal sealed class DriverDownloadService
    {
        private readonly PrinterInstaller _installer;

        public DriverDownloadService(PrinterInstaller installer)
        {
            _installer = installer;
        }

        public DriverInstallResult InstallFromRecommendation(DriverRecommendation recommendation)
        {
            if (recommendation == null)
            {
                throw new ArgumentNullException("recommendation");
            }

            if (!recommendation.IsKnownVendor)
            {
                throw new InvalidOperationException("Automatic driver download needs a matched vendor first.");
            }

            var packageUrl = FindDriverPackageUrl(recommendation);
            var workingFolder = CreateWorkingFolder(recommendation);
            var packagePath = DownloadPackage(packageUrl, workingFolder);
            var extractedFolder = ExtractPackage(packagePath, workingFolder);
            var stageOutput = _installer.StageDriverFolder(extractedFolder, recommendation.ModelQuery, recommendation.Vendor);

            return new DriverInstallResult(packageUrl, packagePath, extractedFolder, stageOutput);
        }

        private static string FindDriverPackageUrl(DriverRecommendation recommendation)
        {
            if (IsCanonC5800Series(recommendation))
            {
                throw new CompliantDriverUnavailableException(
                    "Canon's official page for this C5800-series model currently offers only Canon Generic Plus PCL6. " +
                    "Printervention rejected that package because it is a common generic driver, not a model-specific driver. " +
                    "No driver or print object was installed.");
            }

            using (var client = CreateWebClient())
            {
                var html = client.DownloadString(recommendation.SupportUrl);
                var candidates = ExtractLinks(html, recommendation.SupportUrl)
                    .Where(candidate => recommendation.IsAuthorizedUrl(candidate.Url))
                    .Where(IsDriverDownloadCandidate)
                    .Where(candidate => !IsBlockedDriverUrl(candidate.Url, candidate.Context))
                    .ToList();

                var preferred = candidates
                    .OrderByDescending(ScoreDriverUrl)
                    .FirstOrDefault();

                if (preferred == null)
                {
                    throw new InvalidOperationException("I could not find a model-specific PCL/PCL6 download link on the official vendor page. Use the browser page to download it, then choose the extracted folder.");
                }

                return preferred.Url;
            }
        }

        private static bool IsCanonC5800Series(DriverRecommendation recommendation)
        {
            return recommendation.Vendor.Equals("Canon", StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(recommendation.ModelQuery ?? string.Empty, @"\bC58(?:40|50|60|70)i?\b", RegexOptions.IgnoreCase);
        }

        private static IEnumerable<DriverDownloadCandidate> ExtractLinks(string html, string baseUrl)
        {
            var links = new List<DriverDownloadCandidate>();
            foreach (Match match in Regex.Matches(html ?? string.Empty, "(?:href|src)\\s*=\\s*[\"'](?<url>[^\"']+)[\"']", RegexOptions.IgnoreCase))
            {
                var value = WebUtility.HtmlDecode(match.Groups["url"].Value);
                Uri parsed;
                if (Uri.TryCreate(new Uri(baseUrl), value, out parsed))
                {
                    links.Add(new DriverDownloadCandidate(parsed.AbsoluteUri, ExtractContext(html, match.Index)));
                }
            }

            foreach (Match match in Regex.Matches(html ?? string.Empty, "https?://[^\\s\"'<>]+", RegexOptions.IgnoreCase))
            {
                links.Add(new DriverDownloadCandidate(match.Value.TrimEnd('.', ',', ';', ')'), ExtractContext(html, match.Index)));
            }

            return links
                .GroupBy(candidate => candidate.Url, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First());
        }

        private static string ExtractContext(string html, int index)
        {
            if (string.IsNullOrEmpty(html))
            {
                return string.Empty;
            }

            var start = Math.Max(0, index - 800);
            var length = Math.Min(html.Length - start, 1600);
            return WebUtility.HtmlDecode(Regex.Replace(html.Substring(start, length), "<[^>]+>", " "));
        }

        private static bool IsDriverDownloadCandidate(DriverDownloadCandidate candidate)
        {
            var loweredUrl = Uri.UnescapeDataString(candidate.Url).ToLowerInvariant();
            var loweredContext = candidate.Context.ToLowerInvariant();
            var looksLikePackage = loweredUrl.EndsWith(".zip") ||
                loweredUrl.EndsWith(".cab") ||
                loweredUrl.EndsWith(".exe") ||
                loweredUrl.Contains("download") ||
                loweredUrl.Contains("/pub_");

            return looksLikePackage && (loweredUrl.Contains("pcl") ||
                loweredUrl.Contains("pcl6") ||
                loweredUrl.Contains("pcl_6") ||
                loweredContext.Contains("pcl 6 driver") ||
                loweredContext.Contains("pcl6 driver"));
        }

        private static bool IsBlockedDriverUrl(string url, string context)
        {
            var lowered = (Uri.UnescapeDataString(url) + " " + context).ToLowerInvariant();
            return lowered.Contains("universal") ||
                lowered.Contains("global") ||
                lowered.Contains("generic") ||
                lowered.Contains(" v4") ||
                lowered.Contains("v4_") ||
                lowered.Contains("class");
        }

        private static int ScoreDriverUrl(DriverDownloadCandidate candidate)
        {
            var lowered = (Uri.UnescapeDataString(candidate.Url) + " " + candidate.Context).ToLowerInvariant();
            var score = 0;
            if (lowered.Contains("pcl 6 driver") || lowered.Contains("pcl6 driver"))
            {
                score += 50;
            }

            if (lowered.Contains("pcl6") || lowered.Contains("pcl_6"))
            {
                score += 30;
            }

            if (lowered.EndsWith(".zip"))
            {
                score += 20;
            }

            if (lowered.EndsWith(".exe"))
            {
                score += 5;
            }

            return score;
        }

        private static string DownloadPackage(string url, string workingFolder)
        {
            var fileName = Path.GetFileName(new Uri(url).LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "driver-package";
            }

            var packagePath = Path.Combine(workingFolder, fileName);
            using (var client = CreateWebClient())
            {
                client.DownloadFile(url, packagePath);
            }

            return packagePath;
        }

        private static string ExtractPackage(string packagePath, string workingFolder)
        {
            var extractFolder = Path.Combine(workingFolder, "extracted");
            Directory.CreateDirectory(extractFolder);

            var extension = Path.GetExtension(packagePath).ToLowerInvariant();
            if (extension == ".zip")
            {
                ZipFile.ExtractToDirectory(packagePath, extractFolder);
                return extractFolder;
            }

            if (extension == ".cab")
            {
                RunExpand(packagePath, extractFolder);
                return extractFolder;
            }

            if (extension == ".exe")
            {
                if (TryExtractWithSevenZip(packagePath, extractFolder) || TryExtractSelfExtractor(packagePath, extractFolder))
                {
                    return extractFolder;
                }
            }

            File.Copy(packagePath, Path.Combine(extractFolder, Path.GetFileName(packagePath)), true);
            return extractFolder;
        }

        private static bool TryExtractWithSevenZip(string packagePath, string extractFolder)
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe")
            };

            var sevenZip = candidates.FirstOrDefault(File.Exists);
            if (sevenZip == null)
            {
                return false;
            }

            try
            {
                PrinterInstaller.RunProcessWithOutput(sevenZip, "x " + Quote(packagePath) + " -o" + Quote(extractFolder) + " -y", true);
                return Directory.EnumerateFiles(extractFolder, "*.inf", SearchOption.AllDirectories).Any();
            }
            catch
            {
                return false;
            }
        }

        private static bool TryExtractSelfExtractor(string packagePath, string extractFolder)
        {
            var attempts = new[]
            {
                "/extract:" + Quote(extractFolder) + " /quiet",
                "/s /e /f " + Quote(extractFolder),
                "-y -o" + Quote(extractFolder)
            };

            foreach (var arguments in attempts)
            {
                try
                {
                    PrinterInstaller.RunProcessWithOutput(packagePath, arguments, false);
                    if (Directory.EnumerateFiles(extractFolder, "*.inf", SearchOption.AllDirectories).Any())
                    {
                        return true;
                    }
                }
                catch
                {
                    // Self-extracting vendor packages use different switches; try the next common pattern.
                }
            }

            return false;
        }

        private static void RunExpand(string packagePath, string extractFolder)
        {
            PrinterInstaller.RunProcessWithOutput("expand.exe", Quote(packagePath) + " -F:* " + Quote(extractFolder), true);
        }

        private static string CreateWorkingFolder(DriverRecommendation recommendation)
        {
            var safeName = Regex.Replace(recommendation.ModelQuery ?? recommendation.Vendor, "[^A-Za-z0-9._-]+", "_").Trim('_');
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "driver";
            }

            var folder = Path.Combine(Path.GetTempPath(), "Printervention", safeName + "_" + DateTime.Now.ToString("yyyyMMddHHmmss"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static WebClient CreateWebClient()
        {
            var client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = "Printervention/1.0";
            return client;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }

    internal sealed class CompliantDriverUnavailableException : InvalidOperationException
    {
        public CompliantDriverUnavailableException(string message)
            : base(message)
        {
        }
    }

    internal sealed class DriverDownloadCandidate
    {
        public DriverDownloadCandidate(string url, string context)
        {
            Url = url;
            Context = context ?? string.Empty;
        }

        public string Url { get; private set; }
        public string Context { get; private set; }
    }

    internal sealed class DriverInstallResult
    {
        public DriverInstallResult(string packageUrl, string packagePath, string extractedFolder, string stageOutput)
        {
            PackageUrl = packageUrl;
            PackagePath = packagePath;
            ExtractedFolder = extractedFolder;
            StageOutput = stageOutput;
        }

        public string PackageUrl { get; private set; }
        public string PackagePath { get; private set; }
        public string ExtractedFolder { get; private set; }
        public string StageOutput { get; private set; }
    }
}
