/*
  Printervention
  Main Windows Forms interface for discovery, driver recommendation, and queue setup.
*/

using System;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Printervention
{
    internal sealed class MainForm : Form
    {
        private readonly DriverCatalog _catalog = new DriverCatalog();
        private readonly PrinterDiscovery _discovery = new PrinterDiscovery();
        private readonly PrinterInstaller _installer = new PrinterInstaller();
        private readonly DriverDownloadService _downloadService;

        private TextBox _ipAddressTextBox;
        private TextBox _modelTextBox;
        private ComboBox _vendorComboBox;
        private ComboBox _installedDriverComboBox;
        private TextBox _printerNameTextBox;
        private Label _statusLabel;
        private TextBox _recommendationTextBox;
        private Button _discoverButton;
        private Button _openSupportButton;
        private Button _installDriverButton;
        private Button _refreshDriversButton;
        private Button _testPlanButton;
        private Button _createQueueButton;
        private DriverRecommendation _currentRecommendation;
        private bool _settingSuggestedQueueName;
        private string _lastSuggestedQueueName;

        public MainForm()
        {
            _downloadService = new DriverDownloadService(_installer);
            Text = "Printervention";
            MinimumSize = new Size(780, 560);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            BuildInterface();
            LoadVendors();
            RefreshInstalledDrivers();
            UpdateRecommendation();
        }

        private void BuildInterface()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                ColumnCount = 1,
                RowCount = 5
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var title = new Label
            {
                Text = "Printervention",
                AutoSize = true,
                Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 10)
            };
            root.Controls.Add(title);

            var inputGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 4,
                AutoSize = true
            };
            inputGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            inputGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            inputGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            inputGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.Controls.Add(inputGrid);

            _ipAddressTextBox = AddLabeledTextBox(inputGrid, "Printer IP", 0, 0);
            _discoverButton = new Button { Text = "Find Printer", AutoSize = true, Margin = new Padding(8, 2, 0, 8) };
            _discoverButton.Click += async (sender, args) => await DiscoverPrinterAsync();
            inputGrid.Controls.Add(_discoverButton, 3, 0);

            _modelTextBox = AddLabeledTextBox(inputGrid, "Model", 0, 1);
            _modelTextBox.TextChanged += (sender, args) => UpdateRecommendation();

            _vendorComboBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 8, 8) };
            _vendorComboBox.SelectedIndexChanged += (sender, args) => UpdateRecommendation();
            inputGrid.Controls.Add(new Label { Text = "Brand", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 8) }, 2, 1);
            inputGrid.Controls.Add(_vendorComboBox, 3, 1);

            _printerNameTextBox = AddLabeledTextBox(inputGrid, "Queue Name", 0, 2);
            _printerNameTextBox.TextChanged += (sender, args) =>
            {
                if (!_settingSuggestedQueueName)
                {
                    _lastSuggestedQueueName = null;
                }
            };

            _installedDriverComboBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 8, 8) };
            inputGrid.Controls.Add(new Label { Text = "Installed Driver", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 8) }, 2, 2);
            inputGrid.Controls.Add(_installedDriverComboBox, 3, 2);

            var actionBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            _openSupportButton = new Button { Text = "Open Model Driver Page", AutoSize = true, Margin = new Padding(0, 6, 8, 8) };
            _openSupportButton.Click += (sender, args) => OpenSupportPage();
            _installDriverButton = new Button { Text = "Install Driver", AutoSize = true, Margin = new Padding(0, 6, 8, 8) };
            _installDriverButton.Click += (sender, args) => InstallDriver();
            _refreshDriversButton = new Button { Text = "Refresh Installed Drivers", AutoSize = true, Margin = new Padding(0, 6, 8, 8) };
            _refreshDriversButton.Click += (sender, args) => RefreshInstalledDrivers();
            _testPlanButton = new Button { Text = "Test Plan", AutoSize = true, Margin = new Padding(0, 6, 8, 8) };
            _testPlanButton.Click += (sender, args) => TestPlan();
            _createQueueButton = new Button { Text = "Install Printer", AutoSize = true, Margin = new Padding(0, 6, 8, 8) };
            _createQueueButton.Click += (sender, args) => CreateQueue();
            actionBar.Controls.Add(_openSupportButton);
            actionBar.Controls.Add(_installDriverButton);
            actionBar.Controls.Add(_refreshDriversButton);
            actionBar.Controls.Add(_testPlanButton);
            actionBar.Controls.Add(_createQueueButton);
            root.Controls.Add(actionBar);

            _recommendationTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = SystemColors.Window,
                Margin = new Padding(0, 8, 0, 8)
            };
            root.Controls.Add(_recommendationTextBox);

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ForeColor = SystemColors.ControlDarkDark,
                Text = "Ready."
            };
            root.Controls.Add(_statusLabel);
        }

        private TextBox AddLabeledTextBox(TableLayoutPanel grid, string label, int column, int row)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 8) }, column, row);
            var textBox = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 8, 8) };
            grid.Controls.Add(textBox, column + 1, row);
            return textBox;
        }

        private void LoadVendors()
        {
            _vendorComboBox.Items.Clear();
            _vendorComboBox.Items.Add("Auto detect");
            foreach (var profile in _catalog.Profiles.OrderBy(profile => profile.DisplayName))
            {
                _vendorComboBox.Items.Add(profile.DisplayName);
            }

            _vendorComboBox.SelectedIndex = 0;
        }

        private async Task DiscoverPrinterAsync()
        {
            SetBusy(true, "Checking the printer IP...");
            try
            {
                var identity = await _discovery.DiscoverAsync(_ipAddressTextBox.Text);
                _modelTextBox.Text = identity.Model;
                UpdateSuggestedQueueName(identity.Model);

                var matchedVendor = _catalog.MatchVendor(identity.RawDescription + " " + identity.Model);
                if (matchedVendor != null)
                {
                    _vendorComboBox.SelectedItem = matchedVendor.DisplayName;
                }

                SetStatus("Discovery finished using " + identity.Source + ".");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, "Discovery failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private void UpdateSuggestedQueueName(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return;
            }

            var suggestedName = BuildQueueName(model);
            if (!ShouldReplaceQueueName())
            {
                return;
            }

            _settingSuggestedQueueName = true;
            try
            {
                _printerNameTextBox.Text = suggestedName;
                _lastSuggestedQueueName = suggestedName;
            }
            finally
            {
                _settingSuggestedQueueName = false;
            }
        }

        private bool ShouldReplaceQueueName()
        {
            return string.IsNullOrWhiteSpace(_printerNameTextBox.Text) ||
                string.Equals(_printerNameTextBox.Text, _lastSuggestedQueueName, StringComparison.Ordinal);
        }

        private static string BuildQueueName(string model)
        {
            var invalidChars = System.IO.Path.GetInvalidFileNameChars();
            var cleaned = new string(model.Where(character => !invalidChars.Contains(character)).ToArray()).Trim();
            return cleaned.Length > 80 ? cleaned.Substring(0, 80).Trim() : cleaned;
        }

        private void RefreshInstalledDrivers()
        {
            RefreshInstalledDrivers(null);
        }

        private void RefreshInstalledDrivers(string preferredModel)
        {
            try
            {
                var drivers = _installer.GetInstalledPclDrivers();
                _installedDriverComboBox.Items.Clear();
                foreach (var driver in drivers)
                {
                    _installedDriverComboBox.Items.Add(driver);
                }

                if (_installedDriverComboBox.Items.Count > 0)
                {
                    SelectPreferredDriver(preferredModel);
                    SetStatus("Found " + _installedDriverComboBox.Items.Count + " installed model-specific non-v4 PCL driver(s).");
                }
                else
                {
                    SetStatus("No installed model-specific non-v4 PCL drivers were found. Open the official driver page and install or stage one first.");
                }
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        private void SelectPreferredDriver(string preferredModel)
        {
            var preferredTerms = (preferredModel ?? string.Empty)
                .Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(term => term.Any(char.IsDigit))
                .ToArray();

            for (var index = 0; index < _installedDriverComboBox.Items.Count; index++)
            {
                var driverName = Convert.ToString(_installedDriverComboBox.Items[index]);
                if (preferredTerms.Any(term => driverName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    _installedDriverComboBox.SelectedIndex = index;
                    return;
                }
            }

            _installedDriverComboBox.SelectedIndex = 0;
        }

        private void UpdateRecommendation()
        {
            var selectedVendor = _vendorComboBox.SelectedIndex > 0 ? Convert.ToString(_vendorComboBox.SelectedItem) : string.Empty;
            _currentRecommendation = _catalog.Recommend(selectedVendor, _modelTextBox.Text);

            _recommendationTextBox.Text =
                "Vendor: " + _currentRecommendation.Vendor + Environment.NewLine +
                "Model/Search: " + _currentRecommendation.ModelQuery + Environment.NewLine +
                "Recommended driver family: " + _currentRecommendation.RecommendedDriver + Environment.NewLine +
                "Official driver page: " + _currentRecommendation.SupportUrl + Environment.NewLine + Environment.NewLine +
                "Authorized vendor domains: " + _currentRecommendation.AuthorizedDomainDisplay + Environment.NewLine + Environment.NewLine +
                "Rules:" + Environment.NewLine +
                "- Use PCL or PCL6 only." + Environment.NewLine +
                "- Use model-specific drivers when available; avoid universal, global, and generic drivers." + Environment.NewLine +
                "- Do not use PCL v4, class drivers, IPP class drivers, or vendor app-only packages." + Environment.NewLine +
                "- Download installers only from the authorized vendor domains shown above." + Environment.NewLine +
                "- Use Install Driver to stage the extracted vendor driver before Install Printer." + Environment.NewLine +
                "- Use Test Plan when you do not have a printer connected." + Environment.NewLine +
                "- After queue creation, Printervention attempts to set black-and-white and one-sided defaults." + Environment.NewLine + Environment.NewLine +
                _currentRecommendation.Notes;
        }

        private void OpenSupportPage()
        {
            try
            {
                CopyModelSearchText();
                _currentRecommendation.OpenSupportPage();
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, "Blocked URL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void InstallDriver()
        {
            InstallDriverWorkflow(true);
        }

        private bool InstallDriverWorkflow(bool allowManualFallback)
        {
            SetBusy(true, "Trying automatic driver install...");
            try
            {
                var result = _downloadService.InstallFromRecommendation(_currentRecommendation);
                RefreshInstalledDrivers(_modelTextBox.Text);
                SetStatus("Driver installed automatically. Pick the driver, then install the printer.");
                MessageBox.Show(this, BuildInstallResultMessage(result), "Driver installed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return _installedDriverComboBox.Items.Count > 0;
            }
            catch (Exception ex)
            {
                SetStatus("Automatic install needs help: " + ex.Message);
                return allowManualFallback && PromptManualDriverInstall(ex.Message);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private bool PromptManualDriverInstall(string reason)
        {
            CopyModelSearchText();
            _currentRecommendation.OpenSupportPage();
            var response = MessageBox.Show(
                this,
                "I could not finish the driver install automatically." + Environment.NewLine + Environment.NewLine +
                reason + Environment.NewLine + Environment.NewLine +
                "Download the model-specific PCL/PCL6 driver from the official page, extract it if needed, then click OK to choose the extracted driver folder.",
                "Manual driver install",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);

            if (response == DialogResult.OK)
            {
                return StageDriverFolder();
            }

            return false;
        }

        private static string BuildInstallResultMessage(DriverInstallResult result)
        {
            return
                "Driver package downloaded and staged." + Environment.NewLine + Environment.NewLine +
                "Downloaded from:" + Environment.NewLine +
                result.PackageUrl + Environment.NewLine + Environment.NewLine +
                "Extracted to:" + Environment.NewLine +
                result.ExtractedFolder + Environment.NewLine + Environment.NewLine +
                "Refresh/select the model-specific PCL driver, then click Install Printer.";
        }

        private bool StageDriverFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose the extracted vendor driver folder that contains INF files.";
                dialog.ShowNewFolderButton = false;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return false;
                }

                SetBusy(true, "Staging driver files...");
                try
                {
                    var result = _installer.StageDriverFolder(dialog.SelectedPath);
                    RefreshInstalledDrivers(_modelTextBox.Text);
                    SetStatus("Driver installed. Pick the model-specific non-v4 PCL driver, then install the printer.");
                    MessageBox.Show(this, string.IsNullOrWhiteSpace(result) ? "Driver installed." : result, "Driver install finished", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return _installedDriverComboBox.Items.Count > 0;
                }
                catch (Exception ex)
                {
                    SetStatus(ex.Message);
                    MessageBox.Show(this, ex.Message, "Driver staging failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                finally
                {
                    SetBusy(false, null);
                }
            }
        }

        private void CopyModelSearchText()
        {
            if (!string.IsNullOrWhiteSpace(_currentRecommendation.ModelQuery))
            {
                Clipboard.SetText(_currentRecommendation.ModelQuery);
                SetStatus("Copied the model/search text to the clipboard, then opened the model driver page.");
            }
        }

        private void CreateQueue()
        {
            try
            {
                if (_installedDriverComboBox.SelectedItem == null && !InstallDriverWorkflow(true))
                {
                    SetStatus("Install stopped before a model-specific driver was selected.");
                    return;
                }

                var driverName = Convert.ToString(_installedDriverComboBox.SelectedItem);
                _installer.CreateQueue(_ipAddressTextBox.Text, _printerNameTextBox.Text, driverName);
                SetStatus("Queue created. Check printing preferences if this driver exposes vendor-specific color or duplex defaults.");
                MessageBox.Show(this, "Printer queue created. Defaults were set to black-and-white and one-sided where Windows allowed it.", "Queue created", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, "Queue creation failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void TestPlan()
        {
            try
            {
                var driverName = Convert.ToString(_installedDriverComboBox.SelectedItem);
                if (string.IsNullOrWhiteSpace(driverName))
                {
                    driverName = _currentRecommendation.RecommendedDriver;
                }

                var report = BuildTestPlanReport(driverName);
                SetStatus("Test plan passed. No printer or Windows queue was changed.");
                MessageBox.Show(this, report, "Printervention test plan", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, "Test plan failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private string BuildTestPlanReport(string driverName)
        {
            IPAddress parsed;
            if (!IPAddress.TryParse(_ipAddressTextBox.Text.Trim(), out parsed))
            {
                throw new InvalidOperationException("Enter any valid test IP address, such as 192.0.2.10.");
            }

            if (string.IsNullOrWhiteSpace(_printerNameTextBox.Text))
            {
                throw new InvalidOperationException("Enter a queue name for the test plan.");
            }

            if (!DriverCatalog.IsAllowedDriverName(driverName))
            {
                throw new InvalidOperationException("The selected or recommended driver is not a model-specific non-v4 PCL/PCL6 driver.");
            }

            if (_currentRecommendation.IsKnownVendor && !_currentRecommendation.IsAuthorizedUrl(_currentRecommendation.SupportUrl))
            {
                throw new InvalidOperationException("The support URL is not on the authorized vendor domain list.");
            }

            // The dry run intentionally mirrors the create path without creating ports or queues.
            return
                "No changes were made." + Environment.NewLine + Environment.NewLine +
                "Printer IP: " + parsed + Environment.NewLine +
                "Queue name: " + _printerNameTextBox.Text.Trim() + Environment.NewLine +
                "Port to create: IP_" + parsed + Environment.NewLine +
                "Driver to use: " + driverName + Environment.NewLine +
                "Vendor: " + _currentRecommendation.Vendor + Environment.NewLine +
                "Authorized domains: " + _currentRecommendation.AuthorizedDomainDisplay + Environment.NewLine +
                "Defaults to apply: black-and-white, one-sided" + Environment.NewLine + Environment.NewLine +
                "Hardware still needed for final validation: discovery response, test page output, and driver-specific color/duplex behavior.";
        }

        private void SetBusy(bool busy, string status)
        {
            _discoverButton.Enabled = !busy;
            _openSupportButton.Enabled = !busy;
            _installDriverButton.Enabled = !busy;
            _refreshDriversButton.Enabled = !busy;
            _testPlanButton.Enabled = !busy;
            _createQueueButton.Enabled = !busy;

            if (!string.IsNullOrWhiteSpace(status))
            {
                SetStatus(status);
            }
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text;
        }
    }
}
