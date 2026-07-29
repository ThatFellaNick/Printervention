/*
  Printervention
  Main Windows Forms interface for discovery, driver recommendation, and queue setup.
*/

using System;
using System.Collections.Generic;
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
        private Button _stageDriverFolderButton;
        private Button _addToInstallListButton;
        private Button _refreshDriversButton;
        private Button _testPlanButton;
        private Button _installAllButton;
        private Button _loadSelectedButton;
        private Button _removeSelectedButton;
        private Button _clearListButton;
        private DataGridView _installListGrid;
        private DriverRecommendation _currentRecommendation;
        private bool _settingSuggestedQueueName;
        private string _lastSuggestedQueueName;

        public MainForm()
        {
            _downloadService = new DriverDownloadService(_installer);
            Text = "Printervention";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            MinimumSize = new Size(900, 650);
            ClientSize = new Size(1080, 720);
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
                RowCount = 7
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var title = new Label
            {
                Text = "Printervention",
                AutoSize = true,
                Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 2)
            };

            var securityContextNotice = new Label
            {
                Text = "Installation requires Run as administrator or ScreenConnect Backstage (SYSTEM).",
                AutoSize = true,
                ForeColor = Color.DarkRed,
                Margin = new Padding(0, 0, 0, 10)
            };

            var heading = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = Padding.Empty
            };
            heading.Controls.Add(title);
            heading.Controls.Add(securityContextNotice);
            root.Controls.Add(heading);

            var inputGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 4,
                AutoSize = true
            };
            inputGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            inputGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            inputGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            inputGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.Controls.Add(inputGrid);

            _ipAddressTextBox = AddLabeledTextBox(inputGrid, "Printer IP", 0, 0);
            _discoverButton = new Button { Text = "Find Printer", AutoSize = true, Margin = new Padding(8, 2, 0, 8) };
            _discoverButton.Click += async (sender, args) => await DiscoverPrinterAsync();
            inputGrid.Controls.Add(_discoverButton, 3, 0);

            _modelTextBox = AddLabeledTextBox(inputGrid, "Model", 0, 1);
            _modelTextBox.TextChanged += (sender, args) =>
            {
                UpdateSuggestedQueueName(_modelTextBox.Text);
                UpdateRecommendation();
                ClearIncompatibleInstalledDriverSelection();
            };

            _vendorComboBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 8, 8) };
            _vendorComboBox.SelectedIndexChanged += (sender, args) =>
            {
                UpdateRecommendation();
                ClearIncompatibleInstalledDriverSelection();
            };
            inputGrid.Controls.Add(new Label { Text = "Brand", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 8) }, 2, 1);
            inputGrid.Controls.Add(_vendorComboBox, 3, 1);

            _printerNameTextBox = AddLabeledTextBox(inputGrid, "Queue Name (editable)", 0, 2);
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
            _stageDriverFolderButton = new Button { Text = "Stage Extracted Driver Folder", AutoSize = true, Margin = new Padding(0, 6, 8, 8) };
            _stageDriverFolderButton.Click += async (sender, args) => await StageExtractedDriverFolderAsync();
            _addToInstallListButton = new Button { Text = "Add to Install List", AutoSize = true, Margin = new Padding(0, 6, 8, 8) };
            _addToInstallListButton.Click += (sender, args) => AddCurrentPrinterToInstallList();
            _refreshDriversButton = new Button { Text = "Refresh Installed Drivers", AutoSize = true, Margin = new Padding(0, 6, 8, 8) };
            _refreshDriversButton.Click += (sender, args) => RefreshInstalledDrivers();
            _testPlanButton = new Button { Text = "Test Plan", AutoSize = true, Margin = new Padding(0, 6, 8, 8) };
            _testPlanButton.Click += (sender, args) => TestPlan();
            actionBar.Controls.Add(_openSupportButton);
            actionBar.Controls.Add(_stageDriverFolderButton);
            actionBar.Controls.Add(_addToInstallListButton);
            actionBar.Controls.Add(_refreshDriversButton);
            actionBar.Controls.Add(_testPlanButton);
            root.Controls.Add(actionBar);

            var listBar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Margin = new Padding(0, 4, 0, 0)
            };
            listBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            listBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            listBar.Controls.Add(new Label
            {
                Text = "Install List",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(0, 8, 0, 4)
            }, 0, 0);

            var listActions = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Anchor = AnchorStyles.Right
            };
            _installAllButton = new Button { Text = "Install All", AutoSize = true, Margin = new Padding(8, 2, 0, 4) };
            _installAllButton.Click += async (sender, args) => await InstallAllAsync();
            _loadSelectedButton = new Button { Text = "Load Selected", AutoSize = true, Margin = new Padding(8, 2, 0, 4) };
            _loadSelectedButton.Click += (sender, args) => LoadSelectedInstallItem();
            _removeSelectedButton = new Button { Text = "Remove Selected", AutoSize = true, Margin = new Padding(8, 2, 0, 4) };
            _removeSelectedButton.Click += (sender, args) => RemoveSelectedInstallItems();
            _clearListButton = new Button { Text = "Clear List", AutoSize = true, Margin = new Padding(8, 2, 0, 4) };
            _clearListButton.Click += (sender, args) => ClearInstallList();
            listActions.Controls.Add(_installAllButton);
            listActions.Controls.Add(_loadSelectedButton);
            listActions.Controls.Add(_removeSelectedButton);
            listActions.Controls.Add(_clearListButton);
            listBar.Controls.Add(listActions, 1, 0);
            root.Controls.Add(listBar);

            _installListGrid = BuildInstallListGrid();
            _installListGrid.CellDoubleClick += (sender, args) =>
            {
                if (args.RowIndex >= 0)
                {
                    LoadSelectedInstallItem();
                }
            };
            root.Controls.Add(_installListGrid);

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

        private static DataGridView BuildInstallListGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.Fixed3D,
                MultiSelect = true,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                Margin = new Padding(0, 0, 0, 4)
            };

            grid.Columns.Add(CreateInstallColumn("IP Address", 14));
            grid.Columns.Add(CreateInstallColumn("Model", 23));
            grid.Columns.Add(CreateInstallColumn("Brand", 11));
            grid.Columns.Add(CreateInstallColumn("Queue Name", 22));
            grid.Columns.Add(CreateInstallColumn("Driver", 25));
            grid.Columns.Add(CreateInstallColumn("Status", 13));
            grid.Columns.Add(CreateInstallColumn("Details", 28));
            return grid;
        }

        private static DataGridViewTextBoxColumn CreateInstallColumn(string heading, float fillWeight)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = heading,
                FillWeight = fillWeight,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
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

                RefreshInstalledDrivers(identity.Model);
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
            RefreshInstalledDrivers(_modelTextBox.Text);
        }

        private void RefreshInstalledDrivers(string preferredModel)
        {
            try
            {
                var drivers = _installer.GetInstalledPclDrivers(preferredModel, GetPreferredVendor());
                _installedDriverComboBox.Items.Clear();
                foreach (var driver in drivers)
                {
                    _installedDriverComboBox.Items.Add(driver);
                }

                if (_installedDriverComboBox.Items.Count > 0)
                {
                    SelectPreferredDriver(preferredModel, Convert.ToString(_vendorComboBox.SelectedItem));
                    SetStatus("Found " + _installedDriverComboBox.Items.Count + " approved non-v4 PCL/KX driver(s).");
                }
                else
                {
                    var modelContext = DriverCatalog.HasPreferredModelTerms(preferredModel)
                        ? " matching " + preferredModel.Trim()
                        : string.Empty;
                    SetStatus("No approved installed non-v4 PCL/KX drivers" + modelContext + " were found. Add the printer to the install list to stage one automatically.");
                }
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        private void SelectPreferredDriver(string preferredModel, string preferredVendor)
        {
            var preferredTerms = GetPreferredModelTerms(preferredModel);

            var bestIndex = 0;
            var bestScore = int.MinValue;
            for (var index = 0; index < _installedDriverComboBox.Items.Count; index++)
            {
                var driverName = Convert.ToString(_installedDriverComboBox.Items[index]);
                var score = ScoreInstalledDriver(driverName, preferredTerms, preferredVendor);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = index;
                }
            }

            _installedDriverComboBox.SelectedIndex = bestIndex;
        }

        private static string[] GetPreferredModelTerms(string preferredModel)
        {
            return (preferredModel ?? string.Empty)
                .Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(term => term.Any(char.IsDigit))
                .ToArray();
        }

        private static int ScoreInstalledDriver(string driverName, string[] preferredTerms, string preferredVendor)
        {
            var score = 0;
            foreach (var term in preferredTerms)
            {
                if (driverName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 20;
                }
            }

            if (DriverCatalog.IsExactVendorMatch(preferredVendor, driverName))
            {
                score += 80;
            }
            else if (DriverCatalog.IsVendorFamilyMatch(preferredVendor, driverName))
            {
                score += 35;
            }

            if (driverName.IndexOf("PCL", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 10;
            }

            return score;
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
                "- Prefer PCL/PCL6 when the vendor offers it, or an exact-model Kyocera KX driver." + Environment.NewLine +
                "- Brother and Epson exact-model printer drivers are allowed when their names omit PCL." + Environment.NewLine +
                "- Canon Generic Plus PCL6 and HP Universal Printing PCL 6 are allowed only as vendor-specific fallbacks." + Environment.NewLine +
                "- Avoid all other universal, global, and generic drivers." + Environment.NewLine +
                "- Do not use PCL v4, class drivers, IPP class drivers, or vendor app-only packages." + Environment.NewLine +
                "- Download installers only from the authorized vendor domains shown above." + Environment.NewLine +
                "- Add each discovered printer to the install list, then use Install All." + Environment.NewLine +
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

        private void CopyModelSearchText()
        {
            if (!string.IsNullOrWhiteSpace(_currentRecommendation.ModelQuery))
            {
                Clipboard.SetText(_currentRecommendation.ModelQuery);
                SetStatus("Copied the model/search text to the clipboard, then opened the model driver page.");
            }
        }

        private void AddCurrentPrinterToInstallList()
        {
            IPAddress parsedAddress;
            if (!IPAddress.TryParse(_ipAddressTextBox.Text.Trim(), out parsedAddress))
            {
                ShowListValidation("Enter a valid printer IP address before adding it to the install list.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_modelTextBox.Text))
            {
                ShowListValidation("Find the printer or enter its model before adding it to the install list.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_printerNameTextBox.Text))
            {
                ShowListValidation("Enter a queue name before adding the printer to the install list.");
                return;
            }

            if (_currentRecommendation == null || !_currentRecommendation.IsKnownVendor)
            {
                ShowListValidation("Select the printer brand before adding it to the install list.");
                return;
            }

            var queueName = _printerNameTextBox.Text.Trim();
            var existingQueue = GetInstallRows().FirstOrDefault(row =>
                !string.Equals(GetInstallItem(row).IpAddress, parsedAddress.ToString(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(GetInstallItem(row).QueueName, queueName, StringComparison.OrdinalIgnoreCase));
            if (existingQueue != null)
            {
                ShowListValidation("Another printer in the install list already uses the queue name '" + queueName + "'.");
                return;
            }

            var driverName = Convert.ToString(_installedDriverComboBox.SelectedItem);
            if (!DriverCatalog.IsCompatibleDriverName(driverName, _modelTextBox.Text, _currentRecommendation.Vendor))
            {
                driverName = string.Empty;
            }

            var item = new PrinterInstallItem
            {
                IpAddress = parsedAddress.ToString(),
                Model = _modelTextBox.Text.Trim(),
                Vendor = _currentRecommendation.Vendor,
                QueueName = queueName,
                DriverName = driverName,
                Status = "Ready",
                Details = string.Empty,
                Recommendation = _catalog.Recommend(_currentRecommendation.Vendor, _modelTextBox.Text.Trim())
            };

            var existingAddress = GetInstallRows().FirstOrDefault(row =>
                string.Equals(GetInstallItem(row).IpAddress, item.IpAddress, StringComparison.OrdinalIgnoreCase));
            if (existingAddress != null)
            {
                var existingItem = GetInstallItem(existingAddress);
                if (string.Equals(existingItem.Status, "Installed", StringComparison.OrdinalIgnoreCase))
                {
                    ShowListValidation("That printer is already marked Installed. Remove its row before adding it again.");
                    return;
                }

                existingAddress.Tag = item;
                UpdateInstallRow(existingAddress, item);
                SetStatus("Updated " + item.IpAddress + " in the install list.");
            }
            else
            {
                var rowIndex = _installListGrid.Rows.Add();
                var row = _installListGrid.Rows[rowIndex];
                row.Tag = item;
                UpdateInstallRow(row, item);
                SetStatus("Added " + item.IpAddress + " to the install list.");
            }

            ResetPrinterEntry();
        }

        private async Task InstallAllAsync()
        {
            var pendingRows = GetInstallRows()
                .Where(row => !string.Equals(GetInstallItem(row).Status, "Installed", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (pendingRows.Count == 0)
            {
                ShowListValidation(_installListGrid.Rows.Count == 0
                    ? "Add at least one printer to the install list first."
                    : "Every printer in the list is already installed.");
                return;
            }

            var installedCount = 0;
            var failedCount = 0;
            SetBusy(true, "Installing " + pendingRows.Count + " printer(s)...");
            try
            {
                // A failed vendor package or queue should not prevent later rows from being attempted.
                foreach (var row in pendingRows)
                {
                    var item = GetInstallItem(row);
                    try
                    {
                        UpdateInstallProgress(row, "Checking driver", string.Empty);
                        item.DriverName = await InstallQueuedPrinterAsync(item, row);
                        UpdateInstallProgress(row, "Installed", "Black-and-white and one-sided defaults applied where supported.");
                        installedCount++;
                    }
                    catch (Exception ex)
                    {
                        UpdateInstallProgress(row, "Failed", CondenseMessage(ex.Message));
                        failedCount++;
                    }
                }
            }
            finally
            {
                SetBusy(false, null);
            }

            var summary = "Installed " + installedCount + " printer(s).";
            if (failedCount > 0)
            {
                summary += Environment.NewLine + failedCount + " printer(s) failed. Review the Details column, correct the issue, then click Install All to retry failed rows.";
            }

            SetStatus(summary.Replace(Environment.NewLine, " "));
            MessageBox.Show(this, summary, "Batch install finished", MessageBoxButtons.OK,
                failedCount == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private async Task<string> InstallQueuedPrinterAsync(PrinterInstallItem item, DataGridViewRow row)
        {
            var driverName = item.DriverName;
            if (!DriverCatalog.IsCompatibleDriverName(driverName, item.Model, item.Vendor))
            {
                driverName = await FindBestInstalledDriverAsync(item);
            }

            if (string.IsNullOrWhiteSpace(driverName))
            {
                UpdateInstallProgress(row, "Installing driver", item.Recommendation.RecommendedDriver);
                await Task.Run(() => _downloadService.InstallFromRecommendation(item.Recommendation));
                driverName = await FindBestInstalledDriverAsync(item);
            }

            if (ShouldTryExactVendorDriver(item.Vendor, driverName))
            {
                UpdateInstallProgress(row, "Matching brand", "Registering the exact " + item.Vendor + " driver name.");
                await Task.Run(() => _downloadService.InstallFromRecommendation(item.Recommendation));
                driverName = await FindBestInstalledDriverAsync(item);
            }

            if (string.IsNullOrWhiteSpace(driverName))
            {
                throw new InvalidOperationException("Windows did not expose an approved driver name after staging the vendor package.");
            }

            UpdateInstallProgress(row, "Creating queue", driverName);
            await Task.Run(() => _installer.CreateQueue(item.IpAddress, item.QueueName, driverName, item.Model, item.Vendor));
            return driverName;
        }

        private async Task StageExtractedDriverFolderAsync()
        {
            UpdateRecommendation();
            if (_currentRecommendation == null || string.IsNullOrWhiteSpace(_modelTextBox.Text))
            {
                MessageBox.Show(this, "Find a printer or enter its model and brand first.", "Driver folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dialog = new FolderBrowserDialog
            {
                Description = "Choose the folder containing the vendor driver's extracted INF files.",
                ShowNewFolderButton = false
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                SetBusy(true, "Staging the extracted driver folder...");
                try
                {
                    var model = _modelTextBox.Text.Trim();
                    var vendor = _currentRecommendation.Vendor;
                    var result = await Task.Run(() => _installer.StageDriverFolder(dialog.SelectedPath, model, vendor));
                    RefreshInstalledDrivers(model);
                    SetStatus("Driver folder staged. Select the matching driver or add the printer to the install list.");
                    MessageBox.Show(this, result, "Driver folder staged", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    SetStatus("Driver folder staging failed.");
                    MessageBox.Show(this, ex.Message, "Driver staging failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally
                {
                    SetBusy(false, null);
                }
            }
        }

        private async Task<string> FindBestInstalledDriverAsync(PrinterInstallItem item)
        {
            var drivers = await Task.Run(() => _installer.GetInstalledPclDrivers(item.Model, item.Vendor));
            var preferredTerms = GetPreferredModelTerms(item.Model);
            return drivers
                .OrderByDescending(driver => ScoreInstalledDriver(driver, preferredTerms, item.Vendor))
                .FirstOrDefault();
        }

        private static bool ShouldTryExactVendorDriver(string vendor, string driverName)
        {
            return !string.IsNullOrWhiteSpace(driverName) &&
                DriverCatalog.IsVendorFamilyMatch(vendor, driverName) &&
                !DriverCatalog.IsExactVendorMatch(vendor, driverName);
        }

        private IEnumerable<DataGridViewRow> GetInstallRows()
        {
            return _installListGrid.Rows.Cast<DataGridViewRow>()
                .Where(row => row.Tag is PrinterInstallItem);
        }

        private static PrinterInstallItem GetInstallItem(DataGridViewRow row)
        {
            return (PrinterInstallItem)row.Tag;
        }

        private static void UpdateInstallRow(DataGridViewRow row, PrinterInstallItem item)
        {
            row.Cells[0].Value = item.IpAddress;
            row.Cells[1].Value = item.Model;
            row.Cells[2].Value = item.Vendor;
            row.Cells[3].Value = item.QueueName;
            row.Cells[4].Value = item.DriverName;
            row.Cells[5].Value = item.Status;
            row.Cells[6].Value = item.Details;
        }

        private void UpdateInstallProgress(DataGridViewRow row, string status, string details)
        {
            var item = GetInstallItem(row);
            item.Status = status;
            item.Details = details ?? string.Empty;
            UpdateInstallRow(row, item);
            _installListGrid.ClearSelection();
            row.Selected = true;
            _installListGrid.FirstDisplayedScrollingRowIndex = row.Index;
            SetStatus(item.QueueName + ": " + status + (string.IsNullOrWhiteSpace(details) ? string.Empty : " - " + details));
        }

        private void RemoveSelectedInstallItems()
        {
            var selectedRows = _installListGrid.SelectedRows.Cast<DataGridViewRow>()
                .OrderByDescending(row => row.Index)
                .ToList();
            foreach (var row in selectedRows)
            {
                _installListGrid.Rows.Remove(row);
            }

            SetStatus(selectedRows.Count == 0
                ? "Select one or more install-list rows to remove."
                : "Removed " + selectedRows.Count + " printer(s) from the install list.");
        }

        private void ClearInstallList()
        {
            if (_installListGrid.Rows.Count == 0)
            {
                SetStatus("The install list is already empty.");
                return;
            }

            if (MessageBox.Show(this, "Clear every printer from the install list?", "Clear install list",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _installListGrid.Rows.Clear();
            SetStatus("Install list cleared.");
        }

        private void LoadSelectedInstallItem()
        {
            if (_installListGrid.SelectedRows.Count != 1)
            {
                ShowListValidation("Select one printer row to load back into the entry fields.");
                return;
            }

            var item = GetInstallItem(_installListGrid.SelectedRows[0]);
            _ipAddressTextBox.Text = item.IpAddress;
            _modelTextBox.Text = item.Model;
            _vendorComboBox.SelectedItem = item.Vendor;
            _printerNameTextBox.Text = item.QueueName;
            RefreshInstalledDrivers(item.Model);

            if (!string.IsNullOrWhiteSpace(item.DriverName) && _installedDriverComboBox.Items.Contains(item.DriverName))
            {
                _installedDriverComboBox.SelectedItem = item.DriverName;
            }

            SetStatus("Loaded " + item.IpAddress + ". Edit it, then use Add to Install List to update the row.");
        }

        private void ResetPrinterEntry()
        {
            _settingSuggestedQueueName = true;
            try
            {
                _ipAddressTextBox.Clear();
                _modelTextBox.Clear();
                _printerNameTextBox.Clear();
                _lastSuggestedQueueName = null;
                _vendorComboBox.SelectedIndex = 0;
                _installedDriverComboBox.Items.Clear();
            }
            finally
            {
                _settingSuggestedQueueName = false;
            }

            _ipAddressTextBox.Focus();
        }

        private void ShowListValidation(string message)
        {
            SetStatus(message);
            MessageBox.Show(this, message, "Install list", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static string CondenseMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return "The operation failed without an error message.";
            }

            var singleLine = string.Join(" ", message
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim()));
            return singleLine.Length > 300 ? singleLine.Substring(0, 297) + "..." : singleLine;
        }

        private string GetPreferredVendor()
        {
            if (_currentRecommendation != null && _currentRecommendation.IsKnownVendor)
            {
                return _currentRecommendation.Vendor;
            }

            return _vendorComboBox.SelectedIndex > 0 ? Convert.ToString(_vendorComboBox.SelectedItem) : string.Empty;
        }

        private void ClearIncompatibleInstalledDriverSelection()
        {
            var selectedDriver = Convert.ToString(_installedDriverComboBox.SelectedItem);
            if (!string.IsNullOrWhiteSpace(selectedDriver) &&
                !DriverCatalog.IsCompatibleDriverName(selectedDriver, _modelTextBox.Text, GetPreferredVendor()))
            {
                _installedDriverComboBox.SelectedIndex = -1;
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

            var usingRecommendationPlaceholder = string.Equals(driverName, _currentRecommendation.RecommendedDriver, StringComparison.OrdinalIgnoreCase);
            if (!usingRecommendationPlaceholder &&
                !DriverCatalog.IsCompatibleDriverName(driverName, _modelTextBox.Text, GetPreferredVendor()))
            {
                throw new InvalidOperationException("The selected driver is not an approved exact-model, non-v4 printer driver for this brand and model.");
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
            _stageDriverFolderButton.Enabled = !busy;
            _addToInstallListButton.Enabled = !busy;
            _refreshDriversButton.Enabled = !busy;
            _testPlanButton.Enabled = !busy;
            _installAllButton.Enabled = !busy;
            _loadSelectedButton.Enabled = !busy;
            _removeSelectedButton.Enabled = !busy;
            _clearListButton.Enabled = !busy;
            _installListGrid.Enabled = !busy;

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
