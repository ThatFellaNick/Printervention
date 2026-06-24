/*
  Printervention
  Main Windows Forms interface for discovery, driver recommendation, and queue setup.
*/

using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Printervention
{
    internal sealed class MainForm : Form
    {
        private readonly DriverCatalog _catalog = new DriverCatalog();
        private readonly PrinterDiscovery _discovery = new PrinterDiscovery();
        private readonly PrinterInstaller _installer = new PrinterInstaller();

        private TextBox _ipAddressTextBox;
        private TextBox _modelTextBox;
        private ComboBox _vendorComboBox;
        private ComboBox _installedDriverComboBox;
        private TextBox _printerNameTextBox;
        private Label _statusLabel;
        private TextBox _recommendationTextBox;
        private Button _discoverButton;
        private Button _openSupportButton;
        private Button _refreshDriversButton;
        private Button _createQueueButton;
        private DriverRecommendation _currentRecommendation;

        public MainForm()
        {
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

            _installedDriverComboBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 8, 8) };
            inputGrid.Controls.Add(new Label { Text = "Installed Driver", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 8) }, 2, 2);
            inputGrid.Controls.Add(_installedDriverComboBox, 3, 2);

            var actionBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            _openSupportButton = new Button { Text = "Open Driver Page", AutoSize = true, Margin = new Padding(0, 6, 8, 8) };
            _openSupportButton.Click += (sender, args) => _currentRecommendation.OpenSupportPage();
            _refreshDriversButton = new Button { Text = "Refresh Installed Drivers", AutoSize = true, Margin = new Padding(0, 6, 8, 8) };
            _refreshDriversButton.Click += (sender, args) => RefreshInstalledDrivers();
            _createQueueButton = new Button { Text = "Create Queue", AutoSize = true, Margin = new Padding(0, 6, 8, 8) };
            _createQueueButton.Click += (sender, args) => CreateQueue();
            actionBar.Controls.Add(_openSupportButton);
            actionBar.Controls.Add(_refreshDriversButton);
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
                if (string.IsNullOrWhiteSpace(_printerNameTextBox.Text) && !string.IsNullOrWhiteSpace(identity.Model))
                {
                    _printerNameTextBox.Text = identity.Model;
                }

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

        private void RefreshInstalledDrivers()
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
                    _installedDriverComboBox.SelectedIndex = 0;
                    SetStatus("Found " + _installedDriverComboBox.Items.Count + " installed non-v4 PCL driver(s).");
                }
                else
                {
                    SetStatus("No installed non-v4 PCL drivers were found. Open the official driver page and install or stage one first.");
                }
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
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
                "Rules:" + Environment.NewLine +
                "- Use PCL or PCL6 only." + Environment.NewLine +
                "- Do not use PCL v4, class drivers, IPP class drivers, or vendor app-only packages." + Environment.NewLine +
                "- After queue creation, Printervention attempts to set black-and-white and one-sided defaults." + Environment.NewLine + Environment.NewLine +
                _currentRecommendation.Notes;
        }

        private void CreateQueue()
        {
            try
            {
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

        private void SetBusy(bool busy, string status)
        {
            _discoverButton.Enabled = !busy;
            _openSupportButton.Enabled = !busy;
            _refreshDriversButton.Enabled = !busy;
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
