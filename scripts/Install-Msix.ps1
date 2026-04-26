param(
    [switch]$RegisterLoose,
    [switch]$AllowUnsigned,
    [string]$OutputRoot
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repoRoot "out"
}

$manifest = Join-Path $OutputRoot "package\AppxManifest.xml"
$msix = Join-Path $OutputRoot "AzureVPN-CLI.msix"

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

    Get-AppxPackage TheWiseMonkey.AzureVPNCLI | Select-Object Name, PackageFullName, PackageFamilyName, InstallLocation
}
catch {
    Write-Error $_
    Write-Host ""
    Write-Host "If PowerShell printed an ActivityId, inspect it with:"
    Write-Host "  Get-AppPackageLog -ActivityID <activity-id> | Format-List Time,Id,Level,Message"
    throw
}
