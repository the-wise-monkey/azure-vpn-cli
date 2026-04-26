$ErrorActionPreference = "Stop"

Get-AppxPackage VpnPackagedHelper -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Removing $($_.PackageFullName)"
    Remove-AppxPackage -Package $_.PackageFullName
}
