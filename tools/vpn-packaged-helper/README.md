# VPN Packaged Helper

`VpnPackagedHelper.exe` is the MSIX-packaged component that owns VPN profiles and calls `Windows.Networking.Vpn.VpnManagementAgent`.

The unpackaged `vpn.exe` CLI must not use UI Automation. It talks to this helper through the app execution alias `VpnPackagedHelper.exe`.

The helper creates `VpnPlugInProfile` entries that target the Azure VPN Client plug-in package `Microsoft.AzureVpn_8wekyb3d8bbwe`. Existing Azure VPN Client profiles are not visible here unless their `.AzureVpnProfile.xml` file is imported into this helper.

## Build

```powershell
.\tools\vpn-packaged-helper\scripts\Build-Helper.ps1 -Version 1.0.0
```

## Install For Development

Enable Developer Mode, then run:

```powershell
.\tools\vpn-packaged-helper\scripts\Install-Helper.ps1 -RegisterLoose
```

Uninstall the development helper with:

```powershell
.\tools\vpn-packaged-helper\scripts\Uninstall-Helper.ps1
```

## Commands

The helper prints JSON for every command:

```powershell
VpnPackagedHelper.exe health
VpnPackagedHelper.exe list
VpnPackagedHelper.exe import C:\path\profile.AzureVpnProfile.xml
VpnPackagedHelper.exe export "profile" C:\path\profile.AzureVpnProfile.xml
VpnPackagedHelper.exe status "profile"
VpnPackagedHelper.exe connect "profile"
VpnPackagedHelper.exe disconnect "profile"
VpnPackagedHelper.exe delete "profile"
```

Profiles are owned by this package. Existing Azure VPN Client profiles are not visible to this package unless imported from `.AzureVpnProfile.xml`.

`vpn.exe` is the stable user-facing surface. Call the helper directly only when debugging package identity or VPN capability behavior.
