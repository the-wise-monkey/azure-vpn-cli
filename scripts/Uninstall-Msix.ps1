$ErrorActionPreference = "Stop"

Get-AppxPackage TheWiseMonkey.AzureVPN-CLI -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction Stop
Write-Host "Removed TheWiseMonkey.AzureVPN-CLI."
