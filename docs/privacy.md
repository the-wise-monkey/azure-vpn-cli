# AzureVPN-CLI Privacy Policy

_Last updated: 2026-04-26_

AzureVPN-CLI is a command-line tool for managing Azure VPN profiles on Windows. It runs entirely on the user's device.

## Data we collect

**None.** AzureVPN-CLI does not collect, transmit, or store any personal information, telemetry, usage data, crash reports, or analytics. The tool has no servers, no remote endpoints, and no network calls of its own.

## Data the tool reads and writes locally

- **VPN profile XML files** the user explicitly imports or exports. These files stay on the user's device.
- **Profile entries managed by the tool** are stored by Windows in the user's local VPN profile store via `Windows.Networking.Vpn.VpnManagementAgent`. Windows manages the lifetime and security of those entries.

No part of this data leaves the device because of AzureVPN-CLI.

## Third-party services

AzureVPN-CLI does not use third-party services, advertising networks, SDKs, or trackers.

## VPN connections

When the user connects a profile, the actual VPN tunnel is established by the Microsoft Azure VPN Client app, not by AzureVPN-CLI. Any data handling related to the VPN connection itself is governed by Microsoft and the user's VPN provider, not by this tool. AzureVPN-CLI only invokes the operating system API that triggers the connection.

## Children

The tool is not directed at children and does not knowingly collect any information from any user.

## Changes

Updates to this policy will be published in the AzureVPN-CLI GitHub repository at https://github.com/the-wise-monkey/azure-vpn-cli.

## Contact

Questions or concerns: https://github.com/the-wise-monkey/azure-vpn-cli/issues
