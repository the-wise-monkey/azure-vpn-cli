param(
    [switch]$RegisterLoose,
    [switch]$AllowUnsigned,
    [string]$OutputRoot
)

$ErrorActionPreference = "Stop"

$helperRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $helperRoot "out"
}

$manifest = Join-Path $OutputRoot "package\AppxManifest.xml"
$msix = Join-Path $OutputRoot "VpnPackagedHelper.msix"

try {
    if ($RegisterLoose) {
        Add-AppxPackage -Register $manifest -ForceApplicationShutdown -ErrorAction Stop
    }
    elseif ($AllowUnsigned) {
        Add-AppxPackage -Path $msix -AllowUnsigned -ForceApplicationShutdown -ErrorAction Stop
    }
    else {
        Add-AppxPackage -Path $msix -ForceApplicationShutdown -ErrorAction Stop
    }

    Get-AppxPackage VpnPackagedHelper | Select-Object Name, PackageFullName, PackageFamilyName, InstallLocation
}
catch {
    Write-Error $_
    Write-Host ""
    Write-Host "If PowerShell printed an ActivityId, inspect it with:"
    Write-Host "  Get-AppPackageLog -ActivityID <activity-id> | Format-List Time,Id,Level,Message"
    throw
}
