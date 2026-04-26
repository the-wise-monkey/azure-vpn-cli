# AzureVPN-CLI

[![CI](https://github.com/the-wise-monkey/azure-vpn-cli/actions/workflows/ci.yml/badge.svg)](https://github.com/the-wise-monkey/azure-vpn-cli/actions/workflows/ci.yml)

A command-line interface for the [Azure VPN Client](https://apps.microsoft.com/detail/9np355qt2sqb) on Windows.

Version 1.0 removes UI Automation entirely. The unpackaged `vpn.exe` talks to a packaged MSIX helper named `VpnPackagedHelper`, and that helper owns the VPN profiles it creates through `Windows.Networking.Vpn.VpnManagementAgent`.

## Product Model

- Profiles must be imported into the packaged helper from a `.AzureVpnProfile.xml` file.
- Existing profiles that already live inside the Azure VPN Client app are not controlled directly by this CLI unless you export their XML first and import that XML into the helper.
- Connect, disconnect, status, list, import, export, and delete are helper-backed operations. The CLI does not click buttons, inspect the Azure VPN Client window, read `rasphone.pbk`, or call `rasdial`.
- The helper creates `VpnPlugInProfile` entries that target the Azure VPN Client plug-in package `Microsoft.AzureVpn_8wekyb3d8bbwe`.

See [Internal API investigation](docs/internal-api-investigation.md) for the tested alternatives and the reason this architecture replaced UI Automation.

## Requirements

- Windows 10/11.
- Azure VPN Client installed from Microsoft Store.
- `VpnPackagedHelper` installed or registered for the current user.
- For development installs, Windows Developer Mode must be enabled before registering the loose MSIX package.

## Install

```powershell
winget install TheWiseMonkey.AzureVPN-CLI
```

Or download the installer from [Releases](https://github.com/the-wise-monkey/azure-vpn-cli/releases).

The installer includes `vpn.exe` and the packaged helper. Helper registration can still be blocked by Windows policy or Microsoft capability restrictions for `networkingVpnProvider`; `vpn setup` reports that state explicitly.

## Commands

```text
vpn import <profile.AzureVpnProfile.xml> [name]  Import XML into the packaged helper
vpn import <name>                                Import Desktop\<name>.AzureVpnProfile.xml
vpn list                                         List helper-owned profiles
vpn <name> connect                               Connect a helper-owned profile
vpn <name> disconnect                            Disconnect a helper-owned profile
vpn <name> status                                Show helper-reported connection status
vpn export <name> [profile.AzureVpnProfile.xml]  Export stored XML
vpn delete <name>                                Delete a helper-owned profile
vpn setup                                        Re-check prerequisites
```

## Examples

```powershell
vpn import "$env:USERPROFILE\Desktop\my-vpn.AzureVpnProfile.xml"
# Imported my-vpn

vpn list
#   my-vpn                               Disconnected

vpn my-vpn connect
# Connected

vpn my-vpn status
# Connected

vpn my-vpn disconnect
# Disconnected

vpn export my-vpn
# Exported to C:\Users\me\Desktop\my-vpn.AzureVpnProfile.xml

vpn delete my-vpn
# Deleted my-vpn
```

## Debug Mode

Add `-d` to any command to print timestamped CLI/helper invocation details:

```powershell
vpn my-vpn connect -d
```

## Development Build

Build the packaged helper:

```powershell
.\tools\vpn-packaged-helper\scripts\Build-Helper.ps1 -Version 1.0.0
```

Register the helper for local development:

```powershell
.\tools\vpn-packaged-helper\scripts\Install-Helper.ps1 -RegisterLoose
```

Build the CLI:

```powershell
dotnet build src/vpn.csproj -c Release
```

The compiled executable is written to `src/bin/Release/net48/vpn.exe`.

Run a local prerequisite check:

```powershell
src\bin\Release\net48\vpn.exe setup
```

## Testing

Run the unit tests with npm:

```powershell
npm test
```

Or call `dotnet` directly:

```powershell
dotnet test tests/VpnCli.Tests.csproj -c Release
```

The tests cover command routing, helper response parsing, import/export path handling, and error paths. They use a fake helper, so the Azure VPN Client and MSIX helper are not required for unit tests.

## Project Structure

```text
src/Program.cs                    CLI entry point and prerequisite checks
src/VpnCommands.cs                Testable command logic
src/VpnHelperClient.cs            JSON/Process client for VpnPackagedHelper.exe
src/vpn.csproj                    .NET Framework 4.8 executable project
tests/*.cs                        xUnit test suite
tests/VpnCli.Tests.csproj         Test project
docs/                             Technical notes and investigation docs
tools/vpn-packaged-helper/        MSIX helper source, manifest, and scripts
package.json                      npm scripts
installer/vpn-cli.iss             Inno Setup installer script
.github/workflows/ci.yml          CI build/test workflow
.github/workflows/release.yml     Release workflow and winget manifest generation
winget-manifest/                  Historical winget package manifests
```

## Releasing

Tag a version to trigger the release workflow:

```bash
git tag vX.Y.Z
git push origin vX.Y.Z
```

This automatically builds `vpn.exe`, builds the packaged helper, builds the Inno Setup installer, creates a GitHub Release, generates the winget manifest for the tagged version, and submits it to winget when `WINGET_PAT` is configured.

## License

MIT
