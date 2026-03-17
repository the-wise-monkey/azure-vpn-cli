# vpn-cli

A command-line interface for the [Azure VPN Client](https://apps.microsoft.com/detail/9np355qt2sqb) on Windows. Automates connect, disconnect, import, and export operations that would otherwise require clicking through the GUI.

## How it works

- Reads VPN profiles directly from the Azure VPN Client's phonebook (`rasphone.pbk`)
- Checks live connection status via `rasdial`
- Automates the GUI using [UI Automation](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/) (no external dependencies like AutoHotkey)
- Detects sign-in prompts during connect and brings the window to the foreground for authentication
- The Azure VPN Client window is kept fully transparent during automation and restored after

## Prerequisites

- Windows 10/11
- [Azure VPN Client](https://apps.microsoft.com/detail/9np355qt2sqb) (auto-installed on first run via `winget`)

## Setup

Add a `vpn` alias to your PowerShell profile for convenience:

```powershell
# Add to $PROFILE
function vpn { & "D:\Code\vpn-cli\vpn.ps1" @args }
```

Or run directly:

```powershell
.\vpn.ps1 <command> [args]
```

## Commands

```
vpn list                      List all profiles in Azure VPN Client
vpn <name> connect            Connect a profile
vpn <name> disconnect         Disconnect a profile
vpn <name> status             Show connection status
vpn export <name>             Export profile XML to Desktop
vpn import <alias>            Import from Desktop (vnet-bkly-<alias>.AzureVpnProfile.xml)
vpn setup                     Re-check and install prerequisites
```

## Examples

```powershell
# List all VPN profiles with live status
vpn list

# Connect (detects sign-in prompts automatically)
vpn vnet-bkly-cert connect

# Disconnect (no-op if already disconnected)
vpn vnet-bkly-cert disconnect

# Check status
vpn vnet-bkly-cert status

# Export a profile to Desktop
vpn export vnet-bkly-cert

# Import a profile from Desktop
vpn import cert
```

## Debug mode

Add `-d` to any command to see what's happening:

```powershell
vpn vnet-bkly-cert connect -d
```

This enables:
- Timestamped log lines for every step
- The Azure VPN Client window stays visible at (100, 100)
- 1.5s pauses between UI Automation steps so you can follow along

## Testing

```
npm test
```

Runs 28 Pester tests covering all CLI commands and error paths. UI Automation is mocked so tests run without the Azure VPN Client.

## Project structure

```
vpn.ps1           Main script (PowerShell + UI Automation)
vpn.tests.ps1     Pester test suite
package.json      npm scripts (test runner)
```
