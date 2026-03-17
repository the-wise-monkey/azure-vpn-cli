# AzureVPN-CLI

A command-line interface for the [Azure VPN Client](https://apps.microsoft.com/detail/9np355qt2sqb) on Windows. Automates connect, disconnect, import, export, and status operations that would otherwise require clicking through the GUI.

## Install

```powershell
winget install TheWiseMonkey.AzureVPN-CLI
```

Or download the installer from [Releases](https://github.com/the-wise-monkey/azure-vpn-cli/releases).

## How it works

- Reads VPN profiles directly from the Azure VPN Client's phonebook (`rasphone.pbk`), sorted alphabetically to match the UI
- Checks live connection status via `rasdial`
- Automates the GUI using [UI Automation](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/) — finds elements by name, no pixel coordinates
- Detects sign-in prompts during connect, brings the window to the foreground centered on screen
- The Azure VPN Client window is kept fully transparent during automation and always restored to full opacity on exit (even on errors or Ctrl+C)
- Connect/disconnect are idempotent — running `connect` on an already-connected profile exits 0 with "Already connected."
- Auto-installs the Azure VPN Client via `winget` on first run if missing

## Commands

```
vpn list                      List all profiles with live connection status
vpn <name> connect            Connect (waits for sign-in if needed)
vpn <name> disconnect         Disconnect
vpn <name> status             Show connection status via rasdial
vpn export <name>             Export profile XML to Desktop
vpn import <alias>            Import from Desktop (vnet-bkly-<alias>.AzureVpnProfile.xml)
vpn setup                     Re-check and install prerequisites
```

## Examples

```powershell
vpn list
#   vnet-bkly-cert                       Connected
#   vnet-bkly-prod                       Disconnected

vpn vnet-bkly-cert connect
# Connected

vpn vnet-bkly-cert connect
# Already connected.

vpn vnet-bkly-cert disconnect
# Disconnected

vpn vnet-bkly-cert status
# Disconnected

vpn export vnet-bkly-cert
# Exported to C:\Users\me\Desktop\vnet-bkly-cert.AzureVpnProfile.xml

vpn import cert
# Importing vnet-bkly-cert from Desktop...
# Imported
```

## Debug mode

Add `-d` to any command to see what's happening:

```powershell
vpn vnet-bkly-cert connect -d
# [12:00:09.627] Args: Arg1='vnet-bkly-cert' Arg2='connect'
# [12:00:09.691] Invoke-VpnAction: action=connect name=vnet-bkly-cert
# [12:00:10.386] UI Automation window: True
# [12:00:10.395] Looking for profile 'vnet-bkly-cert'...
# [12:00:11.964] Found ListItem: 'vnet-bkly-certDisconnected'
# [12:00:12.886] Selecting profile...
# [12:00:16.489] Found button: 'Connect Connects the VPN connection'
# [12:00:16.491] Clicking 'connect' button...
# [12:00:20.542] Connected after 3s
# Connected
```

Debug mode:
- Shows timestamped log lines for every step
- Keeps the Azure VPN Client window visible at (100, 100) instead of transparent
- Adds 1.5s pauses between UI Automation steps so you can follow along

## Testing

```
npm test
```

Runs 28 [Pester 5](https://pester.dev/) tests covering all CLI commands and error paths. UI Automation is mocked so tests run without the Azure VPN Client.

## Project structure

```
vpn.ps1                          Main script (PowerShell + UI Automation)
vpn.tests.ps1                    Pester 5 test suite (28 tests)
package.json                     npm scripts (test runner)
installer/vpn-cli.iss            Inno Setup installer script
installer/vpn.bat                Batch wrapper for PATH usage
.github/workflows/ci.yml         CI: runs tests on push/PR
.github/workflows/release.yml    Release: builds installer, creates GitHub release, submits to winget
winget-manifest/                 Winget package manifest
```

## Releasing

Tag a version to trigger the release workflow:

```bash
git tag v0.0.2
git push origin v0.0.2
```

This automatically:
1. Runs tests
2. Builds the Inno Setup installer
3. Creates a GitHub Release with the `.exe` attached
4. Submits to winget (requires `WINGET_PAT` repo secret)

## License

MIT
