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

            return name.Contains("pcl") || name.Contains("universal printing");
        }

        private static List<VendorDriverProfile> BuildProfiles()
        {
            return new List<VendorDriverProfile>
            {
                new VendorDriverProfile("Brother", "Brother Universal Printer Driver PCL", "https://support.brother.com/", "Use the model page or Brother universal PCL package. Avoid BR-Script and any v4 class driver.", "brother"),
                new VendorDriverProfile("Canon", "Canon Generic Plus PCL6 Printer Driver", "https://www.usa.canon.com/support", "Use Generic Plus PCL6 for supported office devices. Avoid UFR II-only, PS-only, and v4 packages.", "canon"),
                new VendorDriverProfile("Epson", "Epson Universal Print Driver PCL6", "https://epson.com/Support/sl/s", "Use Epson Universal Print Driver PCL6 when the device supports PCL. Avoid ESC/P-R-only and v4 packages.", "epson"),
                new VendorDriverProfile("Fujifilm", "FUJIFILM Universal PCL6 Print Driver", "https://support-fb.fujifilm.com/", "Use the FUJIFILM Business Innovation PCL6 package for the model.", "fujifilm", "fuji xerox"),
                new VendorDriverProfile("Fujitsu", "Fujitsu PCL Printer Driver", "https://www.fujitsu.com/global/support/products/computing/peripheral/printers/", "Use the model-specific PCL driver when available. Fujitsu support varies heavily by printer family.", "fujitsu"),
                new VendorDriverProfile("HP", "HP Universal Print Driver PCL6", "https://support.hp.com/drivers", "Use HP Universal Print Driver PCL6 or a model-specific PCL6 package. Avoid HP Smart, IPP class, and v4 packages.", "hewlett-packard", "hewlett packard", "hp"),
                new VendorDriverProfile("Konica Minolta", "Konica Minolta Universal PCL", "https://kmbs.konicaminolta.us/support-downloads/", "Use Universal PCL or model-specific PCL. Avoid PS-only and v4 packages.", "konica", "minolta", "bizhub"),
                new VendorDriverProfile("Kyocera", "Kyocera Classic Universal Driver PCL", "https://www.kyoceradocumentsolutions.us/en/support/downloads.html", "Use Classic Universal Driver PCL or model-specific KX/PCL. Avoid v4 packages.", "kyocera", "ecosys", "taskalfa"),
                new VendorDriverProfile("Lexmark", "Lexmark Universal Print Driver PCL XL", "https://www.lexmark.com/en_us/support/download-search.html", "Use Lexmark Universal Print Driver PCL XL. Avoid v4 packages.", "lexmark"),
                new VendorDriverProfile("OKI", "OKI PCL6 Printer Driver", "https://www.oki.com/us/printing/support/drivers-and-utilities/", "Use the model-specific PCL6 driver when available. Avoid PS-only and v4 packages.", "oki", "okidata"),
                new VendorDriverProfile("Panasonic", "Panasonic PCL Printer Driver", "https://help.na.panasonic.com/support/", "Use the model-specific PCL driver when available. Panasonic printer support is model-dependent.", "panasonic"),
                new VendorDriverProfile("Pantum", "Pantum PCL6 Printer Driver", "https://global.pantum.com/support/download/driver/", "Use Pantum model-specific PCL6 packages. Avoid v4 packages.", "pantum"),
                new VendorDriverProfile("Ricoh", "Ricoh PCL6 Driver for Universal Print", "https://support.ricoh.com/bb/html/dr_ut_e/rc3/model/p_i/p_i.htm", "Use Ricoh PCL6 Driver for Universal Print or model-specific PCL6. Avoid v4 packages.", "ricoh", "aficio"),
                new VendorDriverProfile("Riso", "RISO PCL Printer Driver", "https://www.riso.com/support/", "Use the model-specific PCL driver when the device supports PCL. Avoid GDI-only and v4 packages.", "riso"),
                new VendorDriverProfile("Savin", "Savin PCL6 Driver for Universal Print", "https://support.ricoh.com/bb/html/dr_ut_e/rc3/model/p_i/p_i.htm", "Savin devices usually share Ricoh driver families. Use PCL6 Universal or model-specific PCL6.", "savin"),
                new VendorDriverProfile("Sharp", "Sharp Universal Print Driver PCL6", "https://global.sharp/restricted/products/copier/downloads/search/us/detail/018282/download.html", "Use Sharp Universal Print Driver PCL6 or model-specific PCL6. Avoid v4 packages.", "sharp"),
                new VendorDriverProfile("Toshiba", "Toshiba Universal Printer 2 PCL6", "https://business.toshiba.com/support/downloads", "Use Toshiba Universal Printer 2 PCL6 or model-specific PCL6. Avoid v4 packages.", "toshiba", "e-studio"),
                new VendorDriverProfile("Xerox", "Xerox Global Print Driver PCL6", "https://www.support.xerox.com/", "Use Xerox Global Print Driver PCL6 or model-specific PCL6. Avoid v4 packages.", "xerox")
            };
        }
    }

    internal sealed class VendorDriverProfile
    {
        public VendorDriverProfile(string displayName, string recommendedDriver, string supportUrl, string notes, params string[] aliases)
        {
            DisplayName = displayName;
            RecommendedDriver = recommendedDriver;
            SupportUrl = supportUrl;
            Notes = notes;
            Aliases = aliases.Concat(new[] { displayName }).ToArray();
        }

        public string DisplayName { get; private set; }
        public string RecommendedDriver { get; private set; }
        public string SupportUrl { get; private set; }
        public string Notes { get; private set; }
        public string[] Aliases { get; private set; }

        public bool Matches(string text)
        {
            return Aliases.Any(alias => text.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }

    internal sealed class DriverRecommendation
    {
        public DriverRecommendation(VendorDriverProfile profile, string queryModel)
        {
            Vendor = profile.DisplayName;
            ModelQuery = queryModel;
            RecommendedDriver = profile.RecommendedDriver;
            SupportUrl = profile.SupportUrl;
            Notes = profile.Notes;
            IsKnownVendor = true;
        }

        private DriverRecommendation(string model)
        {
            Vendor = "Unknown";
            ModelQuery = model;
            RecommendedDriver = "Unknown PCL6 printer driver";
            SupportUrl = "https://www.google.com/search?q=" + Uri.EscapeDataString((model ?? "printer") + " PCL6 driver -v4");
            Notes = "No catalog match found. Use the vendor's official support site and select a PCL/PCL6 package that is not v4.";
        }

        public string Vendor { get; private set; }
        public string ModelQuery { get; private set; }
        public string RecommendedDriver { get; private set; }
        public string SupportUrl { get; private set; }
        public string Notes { get; private set; }
        public bool IsKnownVendor { get; private set; }

        public static DriverRecommendation Unknown(string model)
        {
            return new DriverRecommendation(model);
        }

        public void OpenSupportPage()
        {
            Process.Start(new ProcessStartInfo(SupportUrl) { UseShellExecute = true });
        }
    }
}
