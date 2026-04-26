$ErrorActionPreference = "Stop"

Get-AppxPackage TheWiseMonkey.AzureVPNCLI -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction Stop
Write-Host "Removed TheWiseMonkey.AzureVPNCLI."
