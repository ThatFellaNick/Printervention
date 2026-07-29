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

            if (recommendation.Vendor.Equals("Kyocera", StringComparison.OrdinalIgnoreCase) &&
                !DriverCatalog.HasPreferredModelTerms(recommendation.ModelQuery))
            {
                throw new InvalidOperationException("Kyocera KX requires the exact ECOSYS or TASKalfa model. Enter the model shown on the printer or its web page, then run install again.");
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
                // Canon currently publishes Generic Plus PCL6 as the supported PCL package for C5800 models.
                return "https://downloads.canon.com/sss2026/drivers/Generic_Plus_PCL6_v3.40.zip";
            }

            if (recommendation.Vendor.Equals("Kyocera", StringComparison.OrdinalIgnoreCase))
            {
                // Kyocera distributes model-specific KX registrations in one signed, official package.
                return "https://www.kyoceradocumentsolutions.us/content/dam/download-center-americas-cf/us/drivers/drivers/KX_Print_Driver_zip.download.zip";
            }

            if (recommendation.Vendor.Equals("Brother", StringComparison.OrdinalIgnoreCase))
            {
                return FindBrotherDriverPackageUrl(recommendation);
            }

            if (recommendation.Vendor.Equals("Epson", StringComparison.OrdinalIgnoreCase))
            {
                return FindEpsonDriverPackageUrl(recommendation);
            }

            if (recommendation.Vendor.Equals("HP", StringComparison.OrdinalIgnoreCase))
            {
                // HP UPD is the approved fallback when no exact model-specific PCL6 driver is installed.
                return "https://ftp.hp.com/pub/softlib/software13/printers/UPD/upd-pcl6-win10-x64-8.2.0.26778.zip";
            }

            using (var client = CreateWebClient())
            {
                var html = client.DownloadString(recommendation.SupportUrl);
                var candidates = ExtractLinks(html, recommendation.SupportUrl)
                    .Where(candidate => recommendation.IsAuthorizedUrl(candidate.Url))
                    .Where(IsDriverDownloadCandidate)
                    .Where(candidate => !IsBlockedDriverUrl(candidate.Url, candidate.Context, recommendation))
                    .ToList();

                var preferred = candidates
                    .OrderByDescending(candidate => ScoreDriverUrl(candidate, recommendation))
                    .FirstOrDefault();

                if (preferred == null)
                {
                    throw new InvalidOperationException("I could not find a model-specific PCL/PCL6 or Kyocera KX download link on the official vendor page. Use the browser page to download it, then choose the extracted folder.");
                }

                return preferred.Url;
            }
        }

        private static string FindBrotherDriverPackageUrl(DriverRecommendation recommendation)
        {
            using (var client = CreateWebClient())
            {
                var searchHtml = client.DownloadString(recommendation.SupportUrl);
                var productPage = ExtractAnchorLinks(searchHtml, recommendation.SupportUrl)
                    .Where(link => link.Url.IndexOf("downloadtop.aspx", StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderByDescending(link => ScoreModelLink(link, recommendation.ModelQuery))
                    .FirstOrDefault();
                if (productPage == null || ScoreModelLink(productPage, recommendation.ModelQuery) == 0)
                {
                    throw new InvalidOperationException("Brother did not return an exact product page for this model.");
                }

                var productHtml = client.DownloadString(productPage.Url);
                var osPage = ExtractLinks(productHtml, productPage.Url)
                    .Where(link => link.Url.IndexOf("downloadlist.aspx", StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderByDescending(link => link.Url.IndexOf("os=10068", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 :
                        link.Url.IndexOf("os=10013", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0)
                    .FirstOrDefault();
                if (osPage == null)
                {
                    throw new InvalidOperationException("Brother did not list a Windows driver page for this model.");
                }

                var driverListHtml = client.DownloadString(osPage.Url);
                var driverDetails = ExtractAnchorLinks(driverListHtml, osPage.Url)
                    .FirstOrDefault(link => link.Url.IndexOf("downloadend.aspx", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        link.Text.Equals("Printer Driver", StringComparison.OrdinalIgnoreCase));
                if (driverDetails == null)
                {
                    throw new InvalidOperationException("Brother did not list an exact-model Printer Driver package for this model.");
                }

                var detailsHtml = client.DownloadString(driverDetails.Url);
                var agreementLink = ExtractAnchorLinks(detailsHtml, driverDetails.Url)
                    .FirstOrDefault(link => link.Url.IndexOf("downloadhowto.aspx", StringComparison.OrdinalIgnoreCase) >= 0);
                if (agreementLink == null)
                {
                    throw new InvalidOperationException("Brother's driver agreement page did not expose a download link.");
                }

                var downloadHtml = client.DownloadString(agreementLink.Url);
                var package = ExtractAnchorLinks(downloadHtml, agreementLink.Url)
                    .FirstOrDefault(link => link.Url.IndexOf("download.brother.com", StringComparison.OrdinalIgnoreCase) >= 0);
                if (package == null || !recommendation.IsAuthorizedUrl(package.Url))
                {
                    throw new InvalidOperationException("Brother's final package URL was not on an authorized Brother domain.");
                }

                return package.Url;
            }
        }

        private static string FindEpsonDriverPackageUrl(DriverRecommendation recommendation)
        {
            using (var client = CreateWebClient())
            {
                var searchHtml = client.DownloadString(recommendation.SupportUrl);
                var modelPage = ExtractAnchorLinks(searchHtml, recommendation.SupportUrl)
                    .Where(link => link.Url.IndexOf("/Support/", StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderByDescending(link => ScoreModelLink(link, recommendation.ModelQuery))
                    .FirstOrDefault();
                if (modelPage == null || ScoreModelLink(modelPage, recommendation.ModelQuery) == 0)
                {
                    throw new InvalidOperationException("Epson did not return an exact support page for this model.");
                }

                var modelHtml = client.DownloadString(modelPage.Url);
                var package = ExtractLinks(modelHtml, modelPage.Url)
                    .Where(candidate => recommendation.IsAuthorizedUrl(candidate.Url))
                    .Where(IsDriverDownloadCandidate)
                    .Where(candidate => !IsBlockedDriverUrl(candidate.Url, candidate.Context, recommendation))
                    .OrderByDescending(candidate => ScoreDriverUrl(candidate, recommendation) +
                        (candidate.Url.IndexOf("Core_X64", StringComparison.OrdinalIgnoreCase) >= 0 ? 25 : 0))
                    .FirstOrDefault();
                if (package == null)
                {
                    throw new InvalidOperationException("Epson did not list an exact-model PCL6 package for this model.");
                }

                return package.Url;
            }
        }

        private static int ScoreModelLink(DriverDownloadCandidate link, string model)
        {
            var combined = (link.Url + " " + link.Text).Replace("-", string.Empty).Replace(" ", string.Empty);
            return Regex.Matches(model ?? string.Empty, @"[A-Za-z]*\d+[A-Za-z0-9]*")
                .Cast<Match>()
                .Count(match => combined.IndexOf(match.Value.Replace("-", string.Empty), StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsCanonC5800Series(DriverRecommendation recommendation)
        {
            return recommendation.Vendor.Equals("Canon", StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(recommendation.ModelQuery ?? string.Empty, @"\bC58(?:40|50|60|70)i?\b", RegexOptions.IgnoreCase);
        }

        private static IEnumerable<DriverDownloadCandidate> ExtractLinks(string html, string baseUrl)
        {
            var links = new List<DriverDownloadCandidate>();
            foreach (Match match in Regex.Matches(html ?? string.Empty, "(?:href|src|value)\\s*=\\s*[\"'](?<url>[^\"']+)[\"']", RegexOptions.IgnoreCase))
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

        private static IEnumerable<DriverDownloadCandidate> ExtractAnchorLinks(string html, string baseUrl)
        {
            foreach (Match match in Regex.Matches(html ?? string.Empty,
                "<a[^>]+href\\s*=\\s*[\"'](?<url>[^\"']+)[\"'][^>]*>(?<text>.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                Uri parsed;
                if (Uri.TryCreate(new Uri(baseUrl), WebUtility.HtmlDecode(match.Groups["url"].Value), out parsed))
                {
                    var text = WebUtility.HtmlDecode(Regex.Replace(match.Groups["text"].Value, "<[^>]+>", " ")).Trim();
                    yield return new DriverDownloadCandidate(parsed.AbsoluteUri, text, text);
                }
            }
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

        private static bool IsBlockedDriverUrl(string url, string context, DriverRecommendation recommendation)
        {
            var lowered = (Uri.UnescapeDataString(url) + " " + context).ToLowerInvariant();
            var isAllowedCanonGenericPlus = recommendation.Vendor.Equals("Canon", StringComparison.OrdinalIgnoreCase) &&
                lowered.Contains("generic") &&
                (lowered.Contains("pcl6") || lowered.Contains("pcl 6"));

            return lowered.Contains("universal") ||
                lowered.Contains("global") ||
                (lowered.Contains("generic") && !isAllowedCanonGenericPlus) ||
                lowered.Contains(" v4") ||
                lowered.Contains("v4_") ||
                lowered.Contains("class");
        }

        private static int ScoreDriverUrl(DriverDownloadCandidate candidate, DriverRecommendation recommendation)
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

            if (lowered.Contains("generic"))
            {
                score -= 40;
            }

            foreach (var term in Regex.Matches(recommendation.ModelQuery ?? string.Empty, @"[A-Za-z]*\d+[A-Za-z0-9]*")
                .Cast<Match>()
                .Select(match => match.Value.ToLowerInvariant()))
            {
                if (lowered.Contains(term))
                {
                    score += 80;
                }
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
                if (TryExtractWithSevenZip(packagePath, extractFolder) ||
                    TryExtractEmbeddedZip(packagePath, extractFolder) ||
                    TryExtractSelfExtractor(packagePath, extractFolder))
                {
                    return extractFolder;
                }
            }

            File.Copy(packagePath, Path.Combine(extractFolder, Path.GetFileName(packagePath)), true);
            return extractFolder;
        }

        private static bool TryExtractEmbeddedZip(string packagePath, string extractFolder)
        {
            try
            {
                var bytes = File.ReadAllBytes(packagePath);
                for (var index = bytes.Length - 22; index >= 0; index--)
                {
                    if (bytes[index] != 0x50 || bytes[index + 1] != 0x4B ||
                        bytes[index + 2] != 0x05 || bytes[index + 3] != 0x06)
                    {
                        continue;
                    }

                    var centralDirectorySize = BitConverter.ToUInt32(bytes, index + 12);
                    var centralDirectoryOffset = BitConverter.ToUInt32(bytes, index + 16);
                    var archiveStart = index - centralDirectorySize - centralDirectoryOffset;
                    if (archiveStart < 0 || archiveStart >= index)
                    {
                        continue;
                    }

                    var embeddedZip = Path.Combine(extractFolder, "embedded-driver.zip");
                    using (var output = File.Create(embeddedZip))
                    {
                        output.Write(bytes, (int)archiveStart, bytes.Length - (int)archiveStart);
                    }

                    var embeddedFolder = Path.Combine(extractFolder, "embedded");
                    Directory.CreateDirectory(embeddedFolder);
                    ZipFile.ExtractToDirectory(embeddedZip, embeddedFolder);
                    return Directory.EnumerateFiles(embeddedFolder, "*.inf", SearchOption.AllDirectories).Any();
                }
            }
            catch
            {
                // Not every vendor EXE contains a conventional ZIP overlay.
            }

            return false;
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

    internal sealed class DriverDownloadCandidate
    {
        public DriverDownloadCandidate(string url, string context)
            : this(url, context, context)
        {
        }

        public DriverDownloadCandidate(string url, string context, string text)
        {
            Url = url;
            Context = context ?? string.Empty;
            Text = text ?? string.Empty;
        }

        public string Url { get; private set; }
        public string Context { get; private set; }
        public string Text { get; private set; }
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
