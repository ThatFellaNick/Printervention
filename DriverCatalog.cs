/*
  Printervention
  Vendor driver catalog and matching rules for non-v4 PCL driver guidance.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

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

            return name.Contains("pcl");
        }

        private static List<VendorDriverProfile> BuildProfiles()
        {
            return new List<VendorDriverProfile>
            {
                new VendorDriverProfile("Brother", "Brother model-specific PCL6 printer driver", "https://support.brother.com/", "Use the exact model page and select the model-specific PCL/PCL6 package. Avoid universal, BR-Script, class, and v4 drivers.", new[] { "brother.com", "support.brother.com" }, "brother"),
                new VendorDriverProfile("Canon", "Canon model-specific PCL6 printer driver", "https://www.usa.canon.com/support", "Use the exact model page and select a model-specific PCL6 package. Avoid Generic Plus, UFR II-only, PS-only, class, universal, and v4 packages.", new[] { "canon.com", "usa.canon.com", "downloads.canon.com" }, "canon"),
                new VendorDriverProfile("Epson", "Epson model-specific PCL6 printer driver", "https://epson.com/Support/sl/s", "Use the exact model page and select the model-specific PCL/PCL6 package when the device supports PCL. Avoid Universal Print Driver, ESC/P-R-only, class, and v4 packages.", new[] { "epson.com", "ftp.epson.com" }, "epson"),
                new VendorDriverProfile("Fujifilm", "FUJIFILM model-specific PCL6 print driver", "https://support-fb.fujifilm.com/", "Use the exact FUJIFILM Business Innovation model page and select the model-specific PCL6 package. Avoid universal and v4 packages.", new[] { "fujifilm.com", "support-fb.fujifilm.com" }, "fujifilm", "fuji xerox"),
                new VendorDriverProfile("Fujitsu", "Fujitsu PCL Printer Driver", "https://www.fujitsu.com/global/support/products/computing/peripheral/printers/", "Use the model-specific PCL driver when available. Fujitsu support varies heavily by printer family.", new[] { "fujitsu.com" }, "fujitsu"),
                new VendorDriverProfile("HP", "HP model-specific PCL6 printer driver", "https://support.hp.com/drivers", "Use the exact model page and select the model-specific PCL6 package. Avoid HP Universal Print Driver, HP Smart, IPP class, and v4 packages.", new[] { "hp.com", "support.hp.com", "ftp.hp.com", "hpe.com" }, "hewlett-packard", "hewlett packard", "hp"),
                new VendorDriverProfile("Konica Minolta", "Konica Minolta model-specific PCL driver", "https://kmbs.konicaminolta.us/support-downloads/", "Use the exact model page and select the model-specific PCL/PCL6 package. Avoid Universal PCL, PS-only, class, and v4 packages.", new[] { "konicaminolta.us", "kmbs.konicaminolta.us", "konicaminolta.com" }, "konica", "minolta", "bizhub"),
                new VendorDriverProfile("Kyocera", "Kyocera model-specific KX/PCL driver", "https://www.kyoceradocumentsolutions.us/en/support/downloads.html", "Use the exact model page and select the model-specific KX/PCL or PCL6 package. Avoid Classic Universal, class, and v4 packages.", new[] { "kyoceradocumentsolutions.us", "kyoceradocumentsolutions.com", "kyocera.com" }, "kyocera", "ecosys", "taskalfa"),
                new VendorDriverProfile("Lexmark", "Lexmark model-specific PCL XL driver", "https://www.lexmark.com/en_us/support/download-search.html", "Use the exact model page and select the model-specific PCL/PCL XL package. Avoid Universal Print Driver, class, and v4 packages.", new[] { "lexmark.com", "downloads.lexmark.com" }, "lexmark"),
                new VendorDriverProfile("OKI", "OKI PCL6 Printer Driver", "https://www.oki.com/us/printing/support/drivers-and-utilities/", "Use the model-specific PCL6 driver when available. Avoid PS-only and v4 packages.", new[] { "oki.com" }, "oki", "okidata"),
                new VendorDriverProfile("Panasonic", "Panasonic PCL Printer Driver", "https://help.na.panasonic.com/support/", "Use the model-specific PCL driver when available. Panasonic printer support is model-dependent.", new[] { "panasonic.com", "help.na.panasonic.com" }, "panasonic"),
                new VendorDriverProfile("Pantum", "Pantum PCL6 Printer Driver", "https://global.pantum.com/support/download/driver/", "Use Pantum model-specific PCL6 packages. Avoid v4 packages.", new[] { "pantum.com", "global.pantum.com" }, "pantum"),
                new VendorDriverProfile("Ricoh", "Ricoh model-specific PCL6 printer driver", "https://www.ricoh-usa.com/en/support-and-download", "Use the exact model page and select the model-specific PCL6 package. Avoid PCL6 Driver for Universal Print, class, and v4 packages.", new[] { "ricoh.com", "support.ricoh.com", "ricoh-usa.com" }, "ricoh", "aficio"),
                new VendorDriverProfile("Riso", "RISO PCL Printer Driver", "https://www.riso.com/support/", "Use the model-specific PCL driver when the device supports PCL. Avoid GDI-only and v4 packages.", new[] { "riso.com" }, "riso"),
                new VendorDriverProfile("Savin", "Savin model-specific PCL6 printer driver", "https://www.ricoh-usa.com/en/support-and-download", "Savin devices usually share Ricoh driver families. Use the exact model page and select the model-specific PCL6 package. Avoid universal and v4 packages.", new[] { "ricoh.com", "support.ricoh.com", "ricoh-usa.com" }, "savin"),
                new VendorDriverProfile("Sharp", "Sharp model-specific PCL6 printer driver", "https://global.sharp/restricted/products/copier/downloads/search/us/detail/018282/download.html", "Use the exact model page and select the model-specific PCL6 package. Avoid Universal Print Driver, class, and v4 packages.", new[] { "sharpusa.com", "sharp.com", "global.sharp" }, "sharp"),
                new VendorDriverProfile("Toshiba", "Toshiba model-specific PCL6 printer driver", "https://business.toshiba.com/support/downloads", "Use the exact model page and select the model-specific PCL6 package. Avoid Universal Printer 2, class, and v4 packages.", new[] { "toshiba.com", "business.toshiba.com" }, "toshiba", "e-studio"),
                new VendorDriverProfile("Xerox", "Xerox model-specific PCL6 printer driver", "https://www.support.xerox.com/", "Use the exact model page and select the model-specific PCL6 package. Avoid Global Print Driver, class, and v4 packages.", new[] { "xerox.com", "support.xerox.com" }, "xerox")
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
            if (profile.DisplayName.Equals("Ricoh", StringComparison.OrdinalIgnoreCase) ||
                profile.DisplayName.Equals("Savin", StringComparison.OrdinalIgnoreCase))
            {
                var ricohSlug = BuildRicohModelSlug(model);
                if (!string.IsNullOrWhiteSpace(ricohSlug))
                {
                    return "https://support.ricoh.com/bb/html/dr_ut_e/re1/model/" + ricohSlug + "/" + ricohSlug + "es.htm";
                }
            }

            return profile.SupportUrl;
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
