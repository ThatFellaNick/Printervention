# Printervention

Printervention is a lightweight Windows printer setup helper. Enter a printer IP address, let the app identify the device when possible, then choose a supported non-v4 PCL driver path for the vendor.

The first version targets .NET Framework 4.8 so it can run on commonly managed Windows systems without requiring a modern .NET runtime install.

## Current behavior

- Discovers printer identity by SNMP first, then HTTP as a fallback.
- Supports the requested major printer brands with a driver catalog.
- Blocks PCL v4, class-driver, universal, global, and generic driver recommendations.
- Shows an authorized vendor domain allowlist for driver downloads.
- Creates a Standard TCP/IP printer port.
- Can create a printer queue with a selected installed driver.
- Includes a Test Plan button for no-printer validation.
- Attempts to default the queue to black-and-white and one-sided printing.

Driver package installation still depends on vendor-provided packages or drivers already staged/installed in Windows. Many vendor downloads require model-specific pages, EULAs, or package extraction, so the app intentionally points users to official vendor driver locations instead of silently scraping arbitrary installers. Any future direct-download work should validate URLs against the vendor allowlist in the catalog first and should prefer model-specific PCL/PCL6 drivers over universal, global, or generic packages.

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

The test plan does not create a port, install a driver, create a printer queue, or send network traffic. It checks the planned queue name, TCP/IP port name, model-specific PCL/non-v4 driver rule, vendor support URL, authorized domains, and the intended black-and-white/one-sided defaults.

Hardware is still needed later to verify live SNMP/HTTP discovery, actual driver installation, test-page printing, and whether a specific vendor driver honors color and duplex defaults exactly as Windows reports them.
