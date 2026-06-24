# Printervention

Printervention is a lightweight Windows printer setup helper. Enter a printer IP address, let the app identify the device when possible, then choose a supported non-v4 PCL driver path for the vendor.

The first version targets .NET Framework 4.8 so it can run on commonly managed Windows systems without requiring a modern .NET runtime install.

## Current behavior

- Discovers printer identity by SNMP first, then HTTP as a fallback.
- Supports the requested major printer brands with a driver catalog.
- Blocks PCL v4 / class-driver style recommendations.
- Shows an authorized vendor domain allowlist for driver downloads.
- Creates a Standard TCP/IP printer port.
- Can create a printer queue with a selected installed driver.
- Attempts to default the queue to black-and-white and one-sided printing.

Driver package installation still depends on vendor-provided packages or drivers already staged/installed in Windows. Many vendor downloads require model-specific pages, EULAs, or package extraction, so the app intentionally points users to official vendor driver locations instead of silently scraping arbitrary installers. Any future direct-download work should validate URLs against the vendor allowlist in the catalog first.

## Build

Build on Windows:

```powershell
dotnet build -c Release
```

The executable is created under:

```text
bin\Release\net48\Printervention.exe
```
