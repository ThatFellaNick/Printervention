# Printervention

Printervention is a lightweight Windows printer setup helper. Enter a printer IP address, let the app identify the device when possible, then choose a supported non-v4 PCL or Kyocera KX driver path for the vendor.

The first version targets .NET Framework 4.8 so it can run on commonly managed Windows systems without requiring a modern .NET runtime install.

## Current behavior

- Discovers printer identity by SNMP first, then HTTP as a fallback.
- Uses the standard Windows application manifest to request administrator rights before startup; ScreenConnect `SYSTEM` sessions already satisfy this requirement.
- Updates app-suggested queue names when a new printer is discovered.
- Defaults the queue name to the detected or entered model while preserving any custom queue name the user enters.
- Supports the requested major printer brands with a driver catalog.
- Blocks PCL v4, class-driver, universal, global, and generic driver recommendations, except Canon Generic Plus PCL6 and HP Universal Printing PCL 6 when an exact-model package is unavailable.
- Resolves detected Canon C5800-series names to the exact Canon model support page and installs Canon's approved Generic Plus PCL6 fallback.
- Resolves Brother and Epson models through their official product searches and selects exact-model printer packages, preferring PCL/PCL6 when the vendor labels it.
- Accepts exact-model Brother and Epson package and Windows driver names that omit `PCL`, while continuing to reject generic, universal, XPS, class, v4, and conflicting-brand drivers. Brother BR-Script and PostScript names also remain blocked.
- Prefers installed exact-model HP PCL6 drivers and uses HP's signed Type 3 Universal Printing PCL 6 package only as the HP fallback.
- Shows an authorized vendor domain allowlist for driver downloads.
- Creates a Standard TCP/IP printer port.
- Can create a printer queue with a selected installed driver.
- Lets operators discover several printers, add each one to an install list, and install the full batch in sequence.
- Tracks driver resolution, queue creation, success, and failure separately for every printer so one failure does not stop the remaining installs.
- Lets operators load a selected row back into the entry fields for correction and retry.
- The `Install All` flow tries automatic official download, extraction, driver staging, and Windows queue creation for each pending row.
- Driver staging tolerates partial package failures when at least one printer INF is added successfully.
- Driver staging selects the native Windows architecture and registers duplicate INF driver names only once.
- After staging, Printervention attempts to register the matching Windows print driver from the INF before creating the queue.
- Cleans Windows driver-store names before calling `Add-Printer`.
- Includes a Test Plan button for no-printer validation.
- Attempts to default the queue to black-and-white and one-sided printing.
- Disables Canon's separate Auto Color Detection setting so Canon preferences show `Black and White` rather than `Auto [Color/B&W]`.
- Uses fully qualified Windows system-tool paths and avoids PowerShell execution-policy overrides.

## Antivirus and signing

Printervention is intentionally transparent about behaviors that security products inspect closely: it downloads driver packages from cataloged vendor domains, extracts them, stages signed printer INFs with Windows `pnputil`, and creates print queues. The source code and authorized-domain catalog are included in this repository.

Release executables should be signed consistently with a trusted code-signing identity and timestamped. Unsigned builds receive no transferable publisher reputation, so every changed file hash can initially be treated as unknown by Microsoft Defender SmartScreen or antivirus machine-learning systems. A self-signed certificate does not provide public reputation.

If Microsoft Defender Antivirus incorrectly detects a release, submit that exact executable through the [Microsoft Security Intelligence file submission portal](https://www.microsoft.com/en-us/wdsi/filesubmission) as a software developer and classify it as incorrectly detected. Do not work around a detection by adding broad Defender exclusions.

Driver package installation still depends on vendor-provided packages or drivers already staged/installed in Windows. Many vendor downloads require model-specific pages, EULAs, or package extraction, so the app intentionally follows official vendor locations instead of scraping arbitrary installers. URLs are checked against the vendor allowlist. Canon Generic Plus PCL6 and HP Universal Printing PCL 6 are narrow vendor-approved fallbacks; other universal, global, and generic packages remain blocked. Kyocera's official KX package is supported only for the exact model registration.

## Build

Build on Windows:

```powershell
dotnet build -c Release
```

The executable is created under:

```text
bin\Release\net48\Printervention.exe
```

## Testing without a printer

You can validate most of the app without owning a printer:

1. Open the app.
2. Enter a documentation-only test IP such as `192.0.2.10`.
3. Choose a brand, such as `HP`.
4. Enter a realistic model, such as `HP Color LaserJet Pro MFP M479fdw`.
5. Enter a queue name.
6. Click `Test Plan`.

The test plan does not create a port, install a driver, create a printer queue, or send network traffic. It checks the planned queue name, TCP/IP port name, model-specific PCL/KX non-v4 driver rule, vendor support URL, authorized domains, and the intended black-and-white/one-sided defaults.

Hardware is still needed later to verify live SNMP/HTTP discovery, actual driver installation, test-page printing, and whether a specific vendor driver honors color and duplex defaults exactly as Windows reports them.

## Real printer workflow

1. Enter the printer IP and click `Find Printer`.
2. Confirm or correct the brand and model.
3. Edit the queue name if the model-based default is not what you want.
4. Click `Add to Install List`.
5. Repeat discovery and list entry for every printer you want to set up.
6. Click `Install All`.
7. Printervention processes each pending row in sequence and records its result in the Status and Details columns.
8. Use `Load Selected` to correct a failed row and add the updated entry back to the list, then click `Install All` again to retry only rows that are not already installed.

If `Installed Driver` is empty when a printer is added, the batch installer attempts to obtain and stage the approved vendor driver automatically. A manually selected installed driver is captured with that printer's list entry only when it is compatible with the selected brand and model.
