/*
  Printervention
  Vendor driver catalog and matching rules for non-v4 PCL and Kyocera KX guidance.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace Printervention
{
    internal sealed class DriverCatalog
    {
        private readonly List<VendorDriverProfile> _profiles;

        public DriverCatalog()
        {
            _profiles = BuildProfiles();
        }

        public IEnumerable<VendorDriverProfile> Profiles
        {
            get { return _profiles; }
        }

        public VendorDriverProfile MatchVendor(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            foreach (var profile in _profiles)
            {
                if (profile.Matches(text))
                {
                    return profile;
                }
            }

            return null;
        }

        public DriverRecommendation Recommend(string vendor, string model)
        {
            var combined = (vendor + " " + model).Trim();
            var profile = MatchVendor(combined) ?? MatchVendor(vendor);

            if (profile == null)
            {
                return DriverRecommendation.Unknown(model);
            }

            var query = string.IsNullOrWhiteSpace(model) ? profile.DisplayName : model.Trim();
            return new DriverRecommendation(profile, query);
        }

        public static bool IsAllowedDriverName(string driverName)
        {
            return IsAllowedDriverName(driverName, null);
        }

        public static bool IsAllowedDriverName(string driverName, string preferredModel)
        {
            if (string.IsNullOrWhiteSpace(driverName))
            {
                return false;
            }

            var name = driverName.ToLowerInvariant();
            if (name.Contains(" v4") || name.EndsWith("v4") || name.Contains("class driver"))
            {
                return false;
            }

            if (name.Contains("universal") || name.Contains("global") || name.Contains("generic"))
            {
                return false;
            }

            if (!name.Contains("pcl"))
            {
                return false;
            }

            return !HasPreferredModelTerms(preferredModel) || LooksLikeModelSpecificDriver(driverName, preferredModel);
        }

        public static bool IsCompatibleDriverName(string driverName, string preferredModel, string preferredVendor)
        {
            var isAllowed = IsAllowedDriverName(driverName, preferredModel, IsCanonGenericPlusAllowed(preferredVendor)) ||
                IsAllowedKyoceraKxDriver(driverName, preferredModel, preferredVendor) ||
                IsAllowedBrotherOrEpsonModelDriver(driverName, preferredModel, preferredVendor) ||
                IsAllowedHpUniversalPcl6Driver(driverName, preferredVendor);

            return isAllowed &&
                !HasConflictingVendorFamily(driverName, preferredVendor);
        }

        private static bool IsAllowedDriverName(string driverName, string preferredModel, bool allowCanonGenericPlus)
        {
            if (string.IsNullOrWhiteSpace(driverName))
            {
                return false;
            }

            var name = driverName.ToLowerInvariant();
            if (name.Contains(" v4") || name.EndsWith("v4") || name.Contains("class driver"))
            {
                return false;
            }

            if (name.Contains("universal") || name.Contains("global"))
            {
                return false;
            }

            var isCanonGenericPlus = allowCanonGenericPlus &&
                name.Contains("canon generic plus") &&
                (name.Contains("pcl 6") || name.Contains("pcl6"));
            if (name.Contains("generic") && !isCanonGenericPlus)
            {
                return false;
            }

            if (!name.Contains("pcl"))
            {
                return false;
            }

            return isCanonGenericPlus ||
                !HasPreferredModelTerms(preferredModel) ||
                LooksLikeModelSpecificDriver(driverName, preferredModel);
        }

        private static bool IsCanonGenericPlusAllowed(string preferredVendor)
        {
            return string.Equals(preferredVendor, "Canon", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAllowedKyoceraKxDriver(string driverName, string preferredModel, string preferredVendor)
        {
            if (!string.Equals(preferredVendor, "Kyocera", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(driverName))
            {
                return false;
            }

            var name = driverName.ToLowerInvariant();
            if (!Regex.IsMatch(driverName, @"\bKX\b", RegexOptions.IgnoreCase) ||
                name.Contains(" v4") || name.EndsWith("v4") || name.Contains("class driver") ||
                name.Contains("universal") || name.Contains("classic") || name.Contains("generic") ||
                name.Contains(" xps") || name.EndsWith("xps"))
            {
                return false;
            }

            // A KX package contains many models, so never accept one without an exact model term.
            return HasPreferredModelTerms(preferredModel) &&
                LooksLikeModelSpecificDriver(driverName, preferredModel);
        }

        private static bool IsAllowedBrotherOrEpsonModelDriver(string driverName, string preferredModel, string preferredVendor)
        {
            var isBrother = string.Equals(preferredVendor, "Brother", StringComparison.OrdinalIgnoreCase);
            var isEpson = string.Equals(preferredVendor, "Epson", StringComparison.OrdinalIgnoreCase);
            if ((!isBrother && !isEpson) ||
                string.IsNullOrWhiteSpace(driverName))
            {
                return false;
            }

            var name = driverName.ToLowerInvariant();
            var isBlocked = name.Contains("universal") || name.Contains("global") || name.Contains("generic") ||
                name.Contains(" v4") || name.EndsWith("v4") || name.Contains("class driver") ||
                name.Contains("ipp class") || name.Contains(" xps") || name.EndsWith("xps") ||
                name.Contains("scanner") || name.Contains("fax");
            var isBlockedBrotherLanguage = isBrother &&
                (name.Contains("br-script") || name.Contains("postscript"));

            // These vendors commonly publish exact-model Type 3 printer names without a PCL label.
            return !isBlocked && !isBlockedBrotherLanguage &&
                HasPreferredModelTerms(preferredModel) && LooksLikeModelSpecificDriver(driverName, preferredModel);
        }

        private static bool IsAllowedHpUniversalPcl6Driver(string driverName, string preferredVendor)
        {
            if (!string.Equals(preferredVendor, "HP", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(driverName))
            {
                return false;
            }

            var name = driverName.ToLowerInvariant();
            return name.Contains("hp universal printing") &&
                (name.Contains("pcl 6") || name.Contains("pcl6")) &&
                !name.Contains(" v4") && !name.EndsWith("v4") && !name.Contains("class driver");
        }

        public static bool IsVendorFamilyMatch(string preferredVendor, string driverName)
        {
            if (string.IsNullOrWhiteSpace(preferredVendor) || string.IsNullOrWhiteSpace(driverName))
            {
                return false;
            }

            return GetVendorFamilyAliases(preferredVendor)
                .Any(alias => driverName.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static bool IsExactVendorMatch(string preferredVendor, string driverName)
        {
            return !string.IsNullOrWhiteSpace(preferredVendor) &&
                !string.IsNullOrWhiteSpace(driverName) &&
                driverName.IndexOf(preferredVendor, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool HasPreferredModelTerms(string preferredModel)
        {
            return ExtractPreferredModelTerms(preferredModel).Any();
        }

        public static string[] GetVendorFamilyAliases(string vendor)
        {
            if (IsRicohFamilyName(vendor))
            {
                return new[] { "Ricoh", "Savin", "Lanier", "Gestetner", "Nashuatec", "Rex-Rotary", "Aficio" };
            }

            if (string.IsNullOrWhiteSpace(vendor))
            {
                return new string[0];
            }

            if (vendor.Equals("Fujifilm", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "Fujifilm", "Fuji Xerox" };
            }

            if (vendor.Equals("HP", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "HP", "Hewlett-Packard", "Hewlett Packard" };
            }

            if (vendor.Equals("Konica Minolta", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "Konica Minolta", "Konica", "Minolta", "bizhub" };
            }

            if (vendor.Equals("Kyocera", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "Kyocera", "ECOSYS", "TASKalfa" };
            }

            if (vendor.Equals("OKI", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "OKI", "OKIDATA" };
            }

            if (vendor.Equals("Toshiba", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "Toshiba", "e-STUDIO" };
            }

            return new[] { vendor };
        }

        private static bool HasConflictingVendorFamily(string driverName, string preferredVendor)
        {
            if (string.IsNullOrWhiteSpace(driverName) || string.IsNullOrWhiteSpace(preferredVendor))
            {
                return false;
            }

            var preferredAliases = GetVendorFamilyAliases(preferredVendor);
            if (preferredAliases.Any(alias => driverName.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return false;
            }

            return KnownVendorFamilies()
                .Where(family => !family.Any(alias => preferredAliases.Contains(alias, StringComparer.OrdinalIgnoreCase)))
                .Any(family => family.Any(alias => driverName.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static IEnumerable<string[]> KnownVendorFamilies()
        {
            yield return new[] { "Brother" };
            yield return new[] { "Canon" };
            yield return new[] { "Epson" };
            yield return new[] { "Fujifilm", "Fuji Xerox" };
            yield return new[] { "Fujitsu" };
            yield return new[] { "HP", "Hewlett-Packard", "Hewlett Packard" };
            yield return new[] { "Konica", "Minolta", "bizhub" };
            yield return new[] { "Kyocera", "ECOSYS", "TASKalfa" };
            yield return new[] { "Lexmark" };
            yield return new[] { "OKI", "OKIDATA" };
            yield return new[] { "Panasonic" };
            yield return new[] { "Pantum" };
            yield return new[] { "Ricoh", "Savin", "Lanier", "Gestetner", "Nashuatec", "Rex-Rotary", "Aficio" };
            yield return new[] { "Riso" };
            yield return new[] { "Sharp" };
            yield return new[] { "Toshiba", "e-STUDIO" };
            yield return new[] { "Xerox" };
        }

        private static bool IsRicohFamilyName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var lowered = value.ToLowerInvariant();
            return lowered.Contains("ricoh") ||
                lowered.Contains("savin") ||
                lowered.Contains("lanier") ||
                lowered.Contains("gestetner") ||
                lowered.Contains("nashuatec") ||
                lowered.Contains("rex-rotary") ||
                lowered.Contains("aficio");
        }

        private static bool LooksLikeModelSpecificDriver(string driverName, string preferredModel)
        {
            if (string.IsNullOrWhiteSpace(preferredModel))
            {
                return false;
            }

            var driver = driverName.ToLowerInvariant();
            var modelTerms = ExtractPreferredModelTerms(preferredModel)
                .Select(term => term.ToLowerInvariant())
                .ToArray();

            return modelTerms.Any(term => driver.Contains(term));
        }

        private static IEnumerable<string> ExtractPreferredModelTerms(string preferredModel)
        {
            if (string.IsNullOrWhiteSpace(preferredModel))
            {
                return new string[0];
            }

            return preferredModel
                .Split(new[] { ' ', '-', '_', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(term => term.Any(char.IsDigit) && term.Length >= 3)
                .ToArray();
        }

        private static List<VendorDriverProfile> BuildProfiles()
        {
            return new List<VendorDriverProfile>
            {
                new VendorDriverProfile("Brother", "Brother exact-model printer driver", "https://support.brother.com/", "Prefer an exact-model PCL/PCL6 package when Brother labels one. Otherwise, use Brother's exact-model Printer Driver even when its package or Windows driver name omits PCL. Avoid universal, generic, BR-Script, class, and v4 drivers.", new[] { "brother.com", "support.brother.com", "download.brother.com" }, "brother"),
                new VendorDriverProfile("Canon", "Canon model-specific PCL6 or Generic Plus PCL6 printer driver", "https://www.usa.canon.com/support", "Prefer an exact model-specific Canon PCL6 package. Canon Generic Plus PCL6 is allowed when Canon does not offer a model-specific package. Avoid UFR II-only, PS-only, class, universal, and v4 packages.", new[] { "canon.com", "usa.canon.com", "downloads.canon.com" }, "canon"),
                new VendorDriverProfile("Epson", "Epson exact-model printer driver", "https://epson.com/Support/sl/s", "Prefer an exact-model PCL/PCL6 package when Epson labels one. Otherwise, use Epson's exact-model Printer Driver even when its package or Windows driver name omits PCL. Avoid universal, generic, class, and v4 packages.", new[] { "epson.com", "ftp.epson.com" }, "epson"),
                new VendorDriverProfile("Fujifilm", "FUJIFILM model-specific PCL6 print driver", "https://support-fb.fujifilm.com/", "Use the exact FUJIFILM Business Innovation model page and select the model-specific PCL6 package. Avoid universal and v4 packages.", new[] { "fujifilm.com", "support-fb.fujifilm.com" }, "fujifilm", "fuji xerox"),
                new VendorDriverProfile("Fujitsu", "Fujitsu PCL Printer Driver", "https://www.fujitsu.com/global/support/products/computing/peripheral/printers/", "Use the model-specific PCL driver when available. Fujitsu support varies heavily by printer family.", new[] { "fujitsu.com" }, "fujitsu"),
                new VendorDriverProfile("HP", "HP model-specific PCL6, or HP Universal Printing PCL 6 fallback", "https://support.hp.com/drivers", "Prefer an installed exact-model HP PCL6 driver. When HP offers no model-specific PCL6 package, HP Universal Printing PCL 6 Type 3 is allowed. Avoid HP Smart Universal v4, IPP class, and all non-PCL packages.", new[] { "hp.com", "support.hp.com", "ftp.hp.com", "hpe.com" }, "hewlett-packard", "hewlett packard", "hp"),
                new VendorDriverProfile("Konica Minolta", "Konica Minolta model-specific PCL driver", "https://kmbs.konicaminolta.us/support-downloads/", "Use the exact model page and select the model-specific PCL/PCL6 package. Avoid Universal PCL, PS-only, class, and v4 packages.", new[] { "konicaminolta.us", "kmbs.konicaminolta.us", "konicaminolta.com" }, "konica", "minolta", "bizhub"),
                new VendorDriverProfile("Kyocera", "Kyocera model-specific KX/PCL driver", "https://www.kyoceradocumentsolutions.us/en/support/downloads.html", "Use the exact model page and select the model-specific KX/PCL or PCL6 package. Avoid Classic Universal, class, and v4 packages.", new[] { "kyoceradocumentsolutions.us", "kyoceradocumentsolutions.com", "kyocera.com" }, "kyocera", "ecosys", "taskalfa"),
                new VendorDriverProfile("Lexmark", "Lexmark model-specific PCL XL driver", "https://www.lexmark.com/en_us/support/download-search.html", "Use the exact model page and select the model-specific PCL/PCL XL package. Avoid Universal Print Driver, class, and v4 packages.", new[] { "lexmark.com", "downloads.lexmark.com" }, "lexmark"),
                new VendorDriverProfile("OKI", "OKI PCL6 Printer Driver", "https://www.oki.com/us/printing/support/drivers-and-utilities/", "Use the model-specific PCL6 driver when available. Avoid PS-only and v4 packages.", new[] { "oki.com" }, "oki", "okidata"),
                new VendorDriverProfile("Panasonic", "Panasonic PCL Printer Driver", "https://help.na.panasonic.com/support/", "Use the model-specific PCL driver when available. Panasonic printer support is model-dependent.", new[] { "panasonic.com", "help.na.panasonic.com" }, "panasonic"),
                new VendorDriverProfile("Pantum", "Pantum PCL6 Printer Driver", "https://global.pantum.com/support/download/driver/", "Use Pantum model-specific PCL6 packages. Avoid v4 packages.", new[] { "pantum.com", "global.pantum.com" }, "pantum"),
                new VendorDriverProfile("Ricoh", "Ricoh model-specific PCL6 printer driver", "https://www.ricoh-usa.com/en/support-and-download", "Use the exact model page and select the model-specific PCL6 package. Avoid PCL6 Driver for Universal Print, class, and v4 packages. Ricoh-family packages may register matching drivers under Savin, Lanier, or Gestetner names.", new[] { "ricoh.com", "support.ricoh.com", "ricoh-usa.com" }, "ricoh", "aficio", "gestetner", "lanier", "nashuatec", "rex-rotary"),
                new VendorDriverProfile("Riso", "RISO PCL Printer Driver", "https://www.riso.com/support/", "Use the model-specific PCL driver when the device supports PCL. Avoid GDI-only and v4 packages.", new[] { "riso.com" }, "riso"),
                new VendorDriverProfile("Savin", "Savin model-specific PCL6 printer driver", "https://www.ricoh-usa.com/en/support-and-download", "Savin devices usually share Ricoh driver families. Use the exact model page and select the model-specific PCL6 package. Avoid universal and v4 packages.", new[] { "ricoh.com", "support.ricoh.com", "ricoh-usa.com" }, "savin", "ricoh", "gestetner", "lanier", "nashuatec", "rex-rotary", "aficio"),
                new VendorDriverProfile("Sharp", "Sharp model-specific PCL6 printer driver", "https://global.sharp/restricted/products/copier/downloads/search/us/detail/018282/download.html", "Use the exact model page and select the model-specific PCL6 package. Avoid Universal Print Driver, class, and v4 packages.", new[] { "sharpusa.com", "sharp.com", "global.sharp" }, "sharp"),
                new VendorDriverProfile("Toshiba", "Toshiba model-specific PCL6 printer driver", "https://business.toshiba.com/support/downloads", "Use the exact model page and select the model-specific PCL6 package. Avoid Universal Printer 2, class, and v4 packages.", new[] { "toshiba.com", "business.toshiba.com" }, "toshiba", "e-studio"),
                new VendorDriverProfile("Xerox", "Xerox model-specific V3 PCL6 printer driver", "https://www.support.xerox.com/en-us/search-results#t=DriversDownloads", "Use the exact Xerox product page and select a model-specific V3 PCL6 package. Avoid Smart Start, Global Print Driver, class, generic, and V4 packages. Some newer Xerox models offer only V4 or Global packages and cannot be installed under these rules.", new[] { "xerox.com", "support.xerox.com", "download.support.xerox.com" }, "xerox", "versalink", "altalink", "workcentre", "phaser", "primelink")
            };
        }
    }

    internal sealed class VendorDriverProfile
    {
        public VendorDriverProfile(string displayName, string recommendedDriver, string supportUrl, string notes, string[] authorizedDomains, params string[] aliases)
        {
            DisplayName = displayName;
            RecommendedDriver = recommendedDriver;
            SupportUrl = supportUrl;
            Notes = notes;
            AuthorizedDomains = authorizedDomains ?? new string[0];
            Aliases = aliases.Concat(new[] { displayName }).ToArray();
        }

        public string DisplayName { get; private set; }
        public string RecommendedDriver { get; private set; }
        public string SupportUrl { get; private set; }
        public string Notes { get; private set; }
        public string[] AuthorizedDomains { get; private set; }
        public string[] Aliases { get; private set; }

        public bool Matches(string text)
        {
            return Aliases.Any(alias => text.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public bool IsAuthorizedUrl(string url)
        {
            Uri parsed;
            if (!Uri.TryCreate(url, UriKind.Absolute, out parsed))
            {
                return false;
            }

            return IsAuthorizedHost(parsed.Host);
        }

        private bool IsAuthorizedHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            return AuthorizedDomains.Any(domain =>
                host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal sealed class DriverRecommendation
    {
        public DriverRecommendation(VendorDriverProfile profile, string queryModel)
        {
            Vendor = profile.DisplayName;
            ModelQuery = queryModel;
            RecommendedDriver = profile.RecommendedDriver;
            SupportUrl = BuildSupportUrl(profile, queryModel);
            Notes = profile.Notes;
            AuthorizedDomains = profile.AuthorizedDomains;
            IsKnownVendor = true;
        }

        private DriverRecommendation(string model)
        {
            Vendor = "Unknown";
            ModelQuery = model;
            RecommendedDriver = "Unknown PCL6 printer driver";
            SupportUrl = "https://www.google.com/search?q=" + Uri.EscapeDataString((model ?? "printer") + " model-specific PCL6 driver -v4 -universal");
            Notes = "No catalog match found. Use the vendor's official support site and select a model-specific PCL/PCL6 package that is not universal and not v4.";
            AuthorizedDomains = new string[0];
        }

        public string Vendor { get; private set; }
        public string ModelQuery { get; private set; }
        public string RecommendedDriver { get; private set; }
        public string SupportUrl { get; private set; }
        public string Notes { get; private set; }
        public string[] AuthorizedDomains { get; private set; }
        public bool IsKnownVendor { get; private set; }

        public string AuthorizedDomainDisplay
        {
            get
            {
                return AuthorizedDomains.Length == 0
                    ? "No vendor matched yet"
                    : string.Join(", ", AuthorizedDomains);
            }
        }

        public bool IsAuthorizedUrl(string url)
        {
            Uri parsed;
            if (!Uri.TryCreate(url, UriKind.Absolute, out parsed))
            {
                return false;
            }

            return AuthorizedDomains.Any(domain =>
                parsed.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                parsed.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
        }

        public static DriverRecommendation Unknown(string model)
        {
            return new DriverRecommendation(model);
        }

        private static string BuildSupportUrl(VendorDriverProfile profile, string model)
        {
            if (profile.DisplayName.Equals("Canon", StringComparison.OrdinalIgnoreCase))
            {
                var canonSlug = BuildCanonModelSlug(model);
                if (!string.IsNullOrWhiteSpace(canonSlug))
                {
                    return "https://www.usa.canon.com/support/p/" + canonSlug;
                }
            }

            if (profile.DisplayName.Equals("Brother", StringComparison.OrdinalIgnoreCase))
            {
                return "https://support.brother.com/g/b/productlist.aspx?c=us&lang=en&content=dl&q=" +
                    Uri.EscapeDataString(model ?? string.Empty);
            }

            if (profile.DisplayName.Equals("Epson", StringComparison.OrdinalIgnoreCase))
            {
                return "https://epson.com/Search?q=" + Uri.EscapeDataString(model ?? string.Empty);
            }

            if (profile.DisplayName.Equals("Ricoh", StringComparison.OrdinalIgnoreCase) ||
                profile.DisplayName.Equals("Savin", StringComparison.OrdinalIgnoreCase))
            {
                var ricohSlug = BuildRicohModelSlug(model);
                if (!string.IsNullOrWhiteSpace(ricohSlug))
                {
                    return "https://support.ricoh.com/bb/html/dr_ut_e/re1/model/" + ricohSlug + "/" + ricohSlug + "es.htm";
                }
            }

            if (profile.DisplayName.Equals("Xerox", StringComparison.OrdinalIgnoreCase))
            {
                var xeroxSlug = BuildXeroxModelSlug(model);
                if (!string.IsNullOrWhiteSpace(xeroxSlug))
                {
                    return "https://www.support.xerox.com/en-us/product/" + xeroxSlug + "/downloads";
                }
            }

            return profile.SupportUrl;
        }

        private static string BuildXeroxModelSlug(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return string.Empty;
            }

            var normalized = Regex.Replace(model, @"\b(Xerox|Color|Multifunction|Copier|Printer|MFP)\b", " ", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            if (Regex.IsMatch(normalized, @"\bPrimeLink\s+C(?:9065|9070)\b", RegexOptions.IgnoreCase))
            {
                return "primelink-c9065-c9070";
            }

            if (Regex.IsMatch(normalized, @"\bPrimeLink\s+B(?:9100|9110|9125|9136)\b", RegexOptions.IgnoreCase))
            {
                return "primelink-b9100-b9110-b9125-b9136";
            }

            var product = Regex.Match(normalized,
                @"\b(?<family>VersaLink|Phaser|PrimeLink)\s+(?<models>[A-Z]?\d{3,5}(?:\s*[/]\s*[A-Z]?\d{3,5})*)\b",
                RegexOptions.IgnoreCase);
            if (product.Success)
            {
                var family = product.Groups["family"].Value.ToLowerInvariant();
                var models = Regex.Replace(product.Groups["models"].Value, @"\s*/\s*", "-").ToLowerInvariant();
                return family + "-" + models;
            }

            var altaLink = Regex.Match(normalized, @"\bAltaLink\s+(?<model>[BC](?<series>80|81)\d{2})\b", RegexOptions.IgnoreCase);
            if (altaLink.Success)
            {
                return "altalink-" + altaLink.Groups["model"].Value.Substring(0, 1).ToLowerInvariant() +
                    altaLink.Groups["series"].Value + "00-series";
            }

            var workCentre = Regex.Match(normalized, @"\bWorkCentre\s+(?<model>\d{4})\b", RegexOptions.IgnoreCase);
            if (workCentre.Success)
            {
                return BuildXeroxWorkCentreSlug(workCentre.Groups["model"].Value);
            }

            if (Regex.IsMatch(normalized, @"\bD(?:95|110|125)\b", RegexOptions.IgnoreCase))
            {
                return "xerox-d95-d110-d125";
            }

            var xeroxModel = Regex.Match(normalized, @"\b(?<model>[BC]\d{3})\b", RegexOptions.IgnoreCase);
            if (xeroxModel.Success)
            {
                var value = xeroxModel.Groups["model"].Value.ToLowerInvariant();
                return "xerox-" + value + (value.StartsWith("c", StringComparison.Ordinal) ? "-color-printer" : "-printer");
            }

            return string.Empty;
        }

        private static string BuildXeroxWorkCentreSlug(string model)
        {
            var groups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "5325", "workcentre-5300-series" }, { "5330", "workcentre-5300-series" }, { "5335", "workcentre-5300-series" },
                { "5945", "workcentre-5945-5955" }, { "5955", "workcentre-5945-5955" },
                { "7120", "workcentre-7120-7125" }, { "7125", "workcentre-7120-7125" },
                { "7220", "workcentre-7220-7225" }, { "7225", "workcentre-7220-7225" },
                { "7525", "workcentre-7525-7530-7535-7545-7556" }, { "7530", "workcentre-7525-7530-7535-7545-7556" },
                { "7535", "workcentre-7525-7530-7535-7545-7556" }, { "7545", "workcentre-7525-7530-7535-7545-7556" },
                { "7556", "workcentre-7525-7530-7535-7545-7556" },
                { "7830", "workcentre-7800-series" }, { "7835", "workcentre-7800-series" },
                { "7845", "workcentre-7800-series" }, { "7855", "workcentre-7800-series" }
            };

            string slug;
            return groups.TryGetValue(model, out slug) ? slug : "workcentre-" + model;
        }

        private static string BuildCanonModelSlug(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return string.Empty;
            }

            // Canon's SNMP name omits "ADVANCE DX" and the trailing "i" for this family.
            var c5800Model = System.Text.RegularExpressions.Regex.Match(model, @"\bC58(?<speed>40|50|60|70)i?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (c5800Model.Success)
            {
                return "imagerunner-advance-dx-c58" + c5800Model.Groups["speed"].Value + "i";
            }

            return string.Empty;
        }

        private static string BuildRicohModelSlug(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return string.Empty;
            }

            var lowered = model.ToLowerInvariant();
            var marker = lowered.IndexOf("ricoh ", StringComparison.Ordinal);
            if (marker >= 0)
            {
                lowered = lowered.Substring(marker + "ricoh ".Length);
            }

            var slash = lowered.IndexOf("/", StringComparison.Ordinal);
            if (slash >= 0)
            {
                lowered = lowered.Substring(0, slash);
            }

            var version = System.Text.RegularExpressions.Regex.Match(lowered, @"\b\d+(\.\d+)+\b");
            if (version.Success)
            {
                lowered = lowered.Substring(0, version.Index);
            }

            var slug = new string(lowered.Where(char.IsLetterOrDigit).ToArray());
            return slug.Contains("mp") || slug.Contains("im") || slug.Contains("sp") ? slug : string.Empty;
        }

        public void OpenSupportPage()
        {
            if (IsKnownVendor && !IsAuthorizedUrl(SupportUrl))
            {
                throw new InvalidOperationException("The support URL is not on the authorized vendor domain list.");
            }

            Process.Start(new ProcessStartInfo(SupportUrl) { UseShellExecute = true });
        }
    }
}
