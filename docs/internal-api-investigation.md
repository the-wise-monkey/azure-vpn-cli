# Internal API Investigation

This document records why AzureVPN-CLI 1.0 moved from UI Automation to a packaged helper.

## Current Decision

The CLI no longer automates the Azure VPN Client UI. Product behavior is now:

- `vpn.exe` is an unpackaged .NET Framework CLI.
- `VpnPackagedHelper` is an MSIX-packaged full-trust helper with `runFullTrust` and `networkingVpnProvider`.
- The helper owns its own `VpnPlugInProfile` entries through `Windows.Networking.Vpn.VpnManagementAgent`.
- Imported profiles come from `.AzureVpnProfile.xml` files exported by Azure VPN Client.
- Existing Azure VPN Client profiles are not visible to the helper unless their XML is exported and imported into the helper.

This is a major-version behavior change because the CLI no longer controls legacy Azure VPN Client profiles directly.

## Alternatives Tested

### Windows RAS

RAS can enumerate and read Azure VPN Client phonebook entries, but it cannot initiate these VPN plug-in profiles directly from an unpackaged process. `rasdial` and `RasDial` reject dial attempts because Windows requires the packaged VPN app to handle the connection.

Result: useful as evidence, not a viable connect/disconnect path for this product.

### Public Windows VPN WinRT APIs

Azure VPN Client itself uses `Windows.Networking.Vpn.VpnManagementAgent` internally. Calling the API from an unpackaged desktop process does not provide a usable product path because VPN management operations require package identity and the VPN capability context.

Result: viable only from a correctly packaged helper.

### Azure VPN Client Private WinRT Classes

The Azure VPN Client package exposes private WinRT metadata in `AzVpnAppBg.winmd`, including:

- `AzVpnAppBg.VpnActionStatics.GetVpnConnectionList`
- `AzVpnAppBg.VpnActionStatics.DisconnectConnection`
- `AzVpnAppBg.VpnActionStatics.ContinueConnection`
- `AzVpnAppBg.VpnActionStatics.ResetReconnection`
- `AzVpnAppBg.VpnPlugInImpl.GetVpnConnections`
- `AzVpnAppBg.VpnPlugInImpl.FindVpnConnectionById`
- `AzVpnAppBg.VpnPlugInImpl.DisconnectConnectionById`
- `AzVpnAppBg.VpnPlugInImpl.ContinueConnectionById`
- `AzVpnAppBg.VpnPlugInImpl.ResetReconnectionById`

A native probe can load copied private DLLs and call `DllGetActivationFactory`, but that does not make the contract product-safe:

- connection-list methods returned empty results outside the live package context;
- command calls no-op, return `E_NOTIMPL`, or return `0x80073D54` (`The process has no package identity`);
- shipping copied Microsoft package DLLs would be brittle, version-specific, and legally/policy sensitive.

Result: not used.

### Azure VPN Client Named Pipe

The app uses `AzureVPNClientPipe` between packaged components. Observed messages are status-oriented and include fields such as command, connection ID, connection status, failure metadata, route data, and gateway data.

The real pipe is scoped to the Azure VPN Client AppContainer. A normal desktop process cannot connect to it directly.

Result: architecture evidence only, not a supported command channel.

### `azurevpn:` Protocol

The package registers an `azurevpn:` protocol handler. Static strings and launch tests show it is used for `continueAuth` toast/authentication callbacks, not for generic connect/disconnect/import/export commands.

Result: not a command API.

### AppService And Package Identity

The package declares an internal AppService and a full-trust systray process. Attempts to connect from an unpackaged process fail because the service is package-scoped. Attempts to execute probes inside the Azure VPN Client package context are blocked without the necessary package identity and permissions.

Result: not reachable as a third-party CLI contract.

## Packaged Helper Proof

A minimal MSIX package was built with:

- `runFullTrust`
- `networkingVpnProvider`
- an app execution alias
- a .NET Framework full-trust executable

Validation results outside the sandbox with Developer Mode enabled:

- The control package installed and ran.
- The `networkingVpnProvider` package installed and ran.
- `GetProfilesAsync()` returned zero existing Azure VPN Client profiles, confirming that a separate package cannot directly enumerate profiles owned by Azure VPN Client.
- `AddProfileFromXmlAsync()` returned `Other` for raw Azure VPN Client export XML because that API expects Windows VPN ProfileXML, not Azure VPN Client export XML.
- `AddProfileFromObjectAsync(VpnPlugInProfile)` succeeded when the helper built a `VpnPlugInProfile`, set `VpnPluginPackageFamilyName` to `Microsoft.AzureVpn_8wekyb3d8bbwe`, copied the Azure VPN Client export XML into `CustomConfiguration`, and populated `ServerUris` from XML `fqdn` entries.
- After creation, `GetProfilesAsync()` returned the package-owned profile.
- `ConnectProfileAsync()` returned `Ok`, and the profile status changed to `Connected`.
- `DisconnectProfileAsync()` returned `Ok`, and the profile status changed to `Disconnected`.

Result: a packaged helper can create and operate helper-owned Azure VPN plug-in profiles without UI Automation.

## Implemented Architecture

The reproducible implementation lives in `tools/vpn-packaged-helper/`.

The helper command surface is JSON over process stdout:

```text
VpnPackagedHelper.exe health
VpnPackagedHelper.exe list
VpnPackagedHelper.exe import <xml-path> [name]
VpnPackagedHelper.exe export <name> <xml-path>
VpnPackagedHelper.exe status <name>
VpnPackagedHelper.exe connect <name>
VpnPackagedHelper.exe disconnect <name>
VpnPackagedHelper.exe delete <name>
```

`vpn.exe` invokes that app execution alias and exposes the user-facing CLI.

## Remaining Product Constraints

- Distribution depends on Windows accepting the helper package and its restricted VPN capability.
- Developer installs require Developer Mode or a signed package trusted by the current user/machine.
- The helper does not migrate existing Azure VPN Client profiles automatically. Users need the `.AzureVpnProfile.xml` source file or an export from the Azure VPN Client app.
- The helper delegates the actual VPN plug-in behavior to the installed Azure VPN Client package.

## Conclusion

The technically viable no-UI path is the packaged helper. The old UI Automation path has been removed instead of kept as a fallback, because keeping both models would preserve two incompatible ownership stories: legacy Azure VPN Client profiles versus helper-owned profiles.
