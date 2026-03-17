param(
    [Parameter(Position=0)][string]$Arg1,
    [Parameter(Position=1)][string]$Arg2,
    [Alias("d")][switch]$Dbg
)

$scriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$pbkPath    = Join-Path $env:LOCALAPPDATA "Packages\Microsoft.AzureVpn_8wekyb3d8bbwe\LocalState\rasphone.pbk"

# ── Debug helper ───────────────────────────────────────────
function Log([string]$msg) {
    if ($Dbg) {
        $ts = Get-Date -Format "HH:mm:ss.fff"
        Write-Host "[$ts] $msg" -ForegroundColor DarkGray
    }
}

# ── Prerequisites ──────────────────────────────────────────
$setupDone = Join-Path $scriptDir ".setup-done"

function Ensure-Prerequisites {
    if (Test-Path $setupDone) {
        Log "Setup flag found, skipping prerequisites check"
        return
    }
    Log "First run - checking prerequisites"

    $azureVpn = Get-AppxPackage Microsoft.AzureVpn* -ErrorAction SilentlyContinue
    if ($azureVpn) {
        Log "Prerequisites OK, writing setup flag"
        Set-Content $setupDone "$(Get-Date -Format o)"
        return
    }

    Write-Host ""
    Write-Host "  Missing prerequisite: Azure VPN Client" -ForegroundColor Yellow
    Write-Host "  Installing Azure VPN Client..." -ForegroundColor Cyan
    $result = winget install "9NP355QT2SQB" --source msstore --accept-package-agreements --accept-source-agreements 2>&1 | Out-String
    $azureVpn = Get-AppxPackage Microsoft.AzureVpn* -ErrorAction SilentlyContinue
    if ($azureVpn) {
        Write-Host "  Azure VPN Client installed." -ForegroundColor Green
        Set-Content $setupDone "$(Get-Date -Format o)"
    } else {
        Write-Host "  Failed to install Azure VPN Client. Install from the Microsoft Store." -ForegroundColor Red
        Log "winget output: $result"
        exit 1
    }
    Write-Host ""
}

# ── Azure VPN Client helpers ───────────────────────────────
function Get-VpnProfiles {
    Log "Reading phonebook: $pbkPath"
    if (-not (Test-Path $pbkPath)) {
        Log "Phonebook not found"
        return @()
    }
    $names = @()
    foreach ($line in Get-Content $pbkPath) {
        if ($line -match '^\[(.+)\]$') {
            $names += $Matches[1]
        }
    }
    $names = $names | Sort-Object
    Log "Phonebook profiles (sorted): $($names -join ', ')"
    return $names
}

function Export-VpnProfile([string]$profileName) {
    Log "Exporting profile: $profileName"
    if (-not (Test-Path $pbkPath)) {
        Log "Phonebook not found"
        return $null
    }
    $lines = Get-Content $pbkPath
    $inSection = $false
    $hex = ''
    foreach ($line in $lines) {
        if ($line -match "^\[$([regex]::Escape($profileName))\]$") { $inSection = $true; continue }
        if ($inSection -and $line -match '^\[') { break }
        if ($inSection -and $line -match '^ThirdPartyProfileInfo=(.+)') {
            $hex += $Matches[1]
        }
    }
    if (-not $hex) {
        Log "No ThirdPartyProfileInfo found for '$profileName'"
        return $null
    }
    Log "Hex data length: $($hex.Length) chars"
    $bytes = [byte[]]::new($hex.Length / 2)
    for ($i = 0; $i -lt $hex.Length; $i += 2) {
        $bytes[$i/2] = [Convert]::ToByte($hex.Substring($i, 2), 16)
    }
    $decoded = [System.Text.Encoding]::Unicode.GetString($bytes)
    $xmlStart = $decoded.IndexOf('<azvpnprofile>')
    if ($xmlStart -lt 0) {
        Log "No <azvpnprofile> found in decoded data"
        return $null
    }
    $xml = $decoded.Substring($xmlStart).TrimEnd([char]0)
    Log "Extracted XML ($($xml.Length) chars)"
    return $xml
}

function Get-ActiveConnections {
    Log "Checking active connections via rasdial"
    $output = rasdial 2>&1 | Out-String
    Log "rasdial output: $($output.Trim())"
    return $output
}

# ── UI Automation ──────────────────────────────────────────
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Get-VpnWindow {
    # Launch app if not running
    $proc = Get-Process | Where-Object { $_.MainWindowTitle -match 'Azure VPN Client' } | Select-Object -First 1
    if (-not $proc) {
        Log "Launching Azure VPN Client"
        Start-Process "shell:AppsFolder\Microsoft.AzureVpn_8wekyb3d8bbwe!App"
        for ($i = 0; $i -lt 20; $i++) {
            Start-Sleep -Milliseconds 500
            $proc = Get-Process | Where-Object { $_.MainWindowTitle -match 'Azure VPN Client' } | Select-Object -First 1
            if ($proc) { break }
        }
        if (-not $proc) { return $null }
        Start-Sleep -Milliseconds 1500
    }

    # Position and visibility
    if (-not ([System.Management.Automation.PSTypeName]'VpnW32').Type) {
        Add-Type @'
        using System;
        using System.Runtime.InteropServices;
        public class VpnW32 {
            [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
            [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int W, int H, bool repaint);
            [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
            [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
            [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
            [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);
            [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
            [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
            public const int GWL_EXSTYLE = -20;
            public const int WS_EX_LAYERED = 0x80000;
            public const uint LWA_ALPHA = 0x2;
        }
'@
    }
    $hwnd = $proc.MainWindowHandle
    [VpnW32]::ShowWindow($hwnd, 9) | Out-Null  # SW_RESTORE

    if ($Dbg) {
        # Debug: visible window at a fixed position
        $rect = New-Object VpnW32+RECT
        [VpnW32]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
        $w = $rect.Right - $rect.Left
        $h = $rect.Bottom - $rect.Top
        [VpnW32]::MoveWindow($hwnd, 100, 100, $w, $h, $true) | Out-Null
        # Ensure fully opaque
        $style = [VpnW32]::GetWindowLong($hwnd, [VpnW32]::GWL_EXSTYLE)
        if ($style -band [VpnW32]::WS_EX_LAYERED) {
            [VpnW32]::SetLayeredWindowAttributes($hwnd, 0, 255, [VpnW32]::LWA_ALPHA) | Out-Null
        }
    } else {
        # Normal: fully transparent
        $style = [VpnW32]::GetWindowLong($hwnd, [VpnW32]::GWL_EXSTYLE)
        [VpnW32]::SetWindowLong($hwnd, [VpnW32]::GWL_EXSTYLE, $style -bor [VpnW32]::WS_EX_LAYERED) | Out-Null
        [VpnW32]::SetLayeredWindowAttributes($hwnd, 0, 0, [VpnW32]::LWA_ALPHA) | Out-Null
        Log "Window set to transparent"
    }
    Start-Sleep -Milliseconds 400

    # Find via UI Automation
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, 'Azure VPN Client')
    $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
    Log "UI Automation window: $($win -ne $null)"
    return $win
}

function Find-Element($parent, $type, $namePattern) {
    $typeProp = [System.Windows.Automation.AutomationElement]::ControlTypeProperty
    $cond = New-Object System.Windows.Automation.PropertyCondition($typeProp, $type)
    $all = $parent.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    foreach ($el in $all) {
        if ($el.Current.Name -match $namePattern) {
            return $el
        }
    }
    return $null
}

function Invoke-Button($el) {
    $pattern = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function DbgStep([string]$msg) {
    if ($Dbg) {
        Log $msg
        Start-Sleep -Milliseconds 1500
    }
}

function Invoke-VpnAction([string]$action, [string]$name) {
    Log "Invoke-VpnAction: action=$action name=$name"

    $win = Get-VpnWindow
    if (-not $win) {
        Write-Host "Failed to open Azure VPN Client."
        return 1
    }

    $listItemType = [System.Windows.Automation.ControlType]::ListItem
    $buttonType   = [System.Windows.Automation.ControlType]::Button

    if ($action -eq "connect" -or $action -eq "disconnect") {
        # Find the list item for this profile
        $escapedName = [regex]::Escape($name)
        DbgStep "Looking for profile '$name'..."
        $item = Find-Element $win $listItemType "^${escapedName}"
        if (-not $item) {
            Log "ListItem for '$name' not found"
            return 1
        }
        Log "Found ListItem: '$($item.Current.Name)'"

        # Already in desired state?
        $itemName = $item.Current.Name
        $isDisconnected = $itemName -match 'Disconnected$'
        $isConnected    = (-not $isDisconnected) -and ($itemName -match 'Connected$')
        Log "State: connected=$isConnected disconnected=$isDisconnected"
        if ($action -eq "connect" -and $isConnected) {
            Write-Host "Already connected."
            return 0
        }
        if ($action -eq "disconnect" -and $isDisconnected) {
            Write-Host "Already disconnected."
            return 0
        }

        # Select the profile by clicking it
        DbgStep "Selecting profile..."
        try {
            $selectPattern = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            $selectPattern.Select()
        } catch {
            Log "SelectionItemPattern failed, trying Invoke"
            try {
                Invoke-Button $item
            } catch {
                Log "Invoke also failed: $_"
            }
        }
        Start-Sleep -Milliseconds 500

        # Check if already in the desired transitional state (e.g. already Connecting)
        $alreadyInProgress = $false
        if ($action -eq "connect" -and $item.Current.Name -match 'Connecting') {
            Log "Profile already connecting (sign-in may be pending)"
            $alreadyInProgress = $true
        }

        if (-not $alreadyInProgress) {
            # Find and click the Connect or Disconnect button
            $buttonName = if ($action -eq "connect") { "^Connect " } else { "^Disconnect " }
            DbgStep "Looking for '$action' button..."
            $btn = Find-Element $win $buttonType $buttonName
            if (-not $btn) {
                Log "Button matching '$buttonName' not found"
                return 1
            }
            Log "Found button: '$($btn.Current.Name)'"

            DbgStep "Clicking '$action' button..."
            Invoke-Button $btn
        }

        if ($action -eq "connect") {
            # Wait for connection, detecting sign-in prompts
            Log "Waiting for connection..."
            $signInNotified = $false
            for ($wait = 0; $wait -lt 60; $wait++) {
                Start-Sleep -Milliseconds 1000

                # Check if connected via rasdial
                $ras = rasdial 2>&1 | Out-String
                if ($ras -match [regex]::Escape($name)) {
                    Log "Connected after ${wait}s"
                    break
                }

                # Check for sign-in prompt (account picker list items)
                $signInItem = Find-Element $win $listItemType "account"
                if ($signInItem -and -not $signInNotified) {
                    Log "Sign-in prompt detected"
                    Write-Host "Waiting for sign-in..." -ForegroundColor Yellow
                    # Make visible, center on screen, and bring to front
                    $proc = Get-Process | Where-Object { $_.MainWindowTitle -match 'Azure VPN Client' } | Select-Object -First 1
                    if ($proc) {
                        $h = $proc.MainWindowHandle
                        # Restore opacity
                        [VpnW32]::SetLayeredWindowAttributes($h, 0, 255, [VpnW32]::LWA_ALPHA) | Out-Null
                        [VpnW32]::ShowWindow($h, 9) | Out-Null  # SW_RESTORE
                        # Center on screen
                        Add-Type -AssemblyName System.Windows.Forms
                        $screen = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
                        $rect = New-Object VpnW32+RECT
                        [VpnW32]::GetWindowRect($h, [ref]$rect) | Out-Null
                        $ww = $rect.Right - $rect.Left
                        $wh = $rect.Bottom - $rect.Top
                        $cx = [int](($screen.Width - $ww) / 2 + $screen.X)
                        $cy = [int](($screen.Height - $wh) / 2 + $screen.Y)
                        [VpnW32]::MoveWindow($h, $cx, $cy, $ww, $wh, $true) | Out-Null
                        Log "Window centered at ($cx, $cy)"
                        # Bring to front
                        [VpnW32]::SetForegroundWindow($h) | Out-Null
                    }
                    $signInNotified = $true
                }

                # Check if profile went back to Disconnected (connect failed)
                $profileItem = Find-Element $win $listItemType "^$($escapedName)Disconnected"
                if ($profileItem) {
                    Log "Profile reverted to Disconnected"
                    return 1
                }
            }
        } else {
            DbgStep "Waiting for action to complete..."
            Start-Sleep -Milliseconds 1000
        }

        $result = if ($action -eq "connect") { "Connected" } else { "Disconnected" }
        Write-Host $result

        if (-not $Dbg) {
            $proc = Get-Process | Where-Object { $_.MainWindowTitle -match 'Azure VPN Client' } | Select-Object -First 1
            if ($proc) {
                [VpnW32]::ShowWindow($proc.MainWindowHandle, 6) | Out-Null  # SW_MINIMIZE
            }
        }
        return 0
    }

    if ($action -eq "import") {
        DbgStep "Looking for Add/Import button..."
        $btn = Find-Element $win $buttonType "Add or Import"
        if (-not $btn) {
            Log "Add/Import button not found"
            return 1
        }
        Log "Found button: '$($btn.Current.Name)'"

        DbgStep "Opening menu..."
        Invoke-Button $btn
        Start-Sleep -Milliseconds 500

        # Click the "Import" menu item
        $menuItemType = [System.Windows.Automation.ControlType]::MenuItem
        DbgStep "Looking for Import menu item..."
        $importItem = Find-Element $win $menuItemType "^Import$"
        if (-not $importItem) {
            Log "Import menu item not found"
            return 1
        }
        Log "Found menu item: '$($importItem.Current.Name)'"

        DbgStep "Clicking Import..."
        Invoke-Button $importItem
        Start-Sleep -Milliseconds 1000

        # Wait for the Open file dialog (hosted inside the VPN window as a descendant)
        DbgStep "Waiting for file dialog..."
        $winType = [System.Windows.Automation.ControlType]::Window
        $openDlg = $null
        for ($i = 0; $i -lt 10; $i++) {
            $openDlg = Find-Element $win $winType "^Open$"
            if ($openDlg) { break }
            Start-Sleep -Milliseconds 500
        }
        if (-not $openDlg) {
            Log "Open dialog not found"
            return 1
        }
        Log "Found file dialog"

        # Type the file path and press Enter (standard Win32 file dialog)
        $filePath = Join-Path ([Environment]::GetFolderPath('Desktop')) "$name.AzureVpnProfile.xml"
        DbgStep "Typing path: $filePath"

        Add-Type -AssemblyName System.Windows.Forms
        # Focus the dialog, clear any existing text, type path, press Enter
        try { $openDlg.SetFocus() } catch { Log "SetFocus failed: $_" }
        Start-Sleep -Milliseconds 300
        [System.Windows.Forms.SendKeys]::SendWait($filePath)
        Start-Sleep -Milliseconds 300
        DbgStep "Pressing Enter..."
        [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")

        # Wait for the profile editor to appear with Save button
        DbgStep "Waiting for profile editor..."
        $saveBtn = $null
        for ($i = 0; $i -lt 10; $i++) {
            Start-Sleep -Milliseconds 500
            $saveBtn = Find-Element $win $buttonType "^Save$"
            if ($saveBtn) { break }
        }
        if (-not $saveBtn) {
            Log "Save button not found"
            return 1
        }
        Log "Found Save button"

        DbgStep "Clicking Save..."
        Invoke-Button $saveBtn
        DbgStep "Waiting for import to complete..."
        Start-Sleep -Milliseconds 1000

        if (-not $Dbg) {
            $proc = Get-Process | Where-Object { $_.MainWindowTitle -match 'Azure VPN Client' } | Select-Object -First 1
            if ($proc) {
                [VpnW32]::ShowWindow($proc.MainWindowHandle, 6) | Out-Null  # SW_MINIMIZE
            }
        }
        return 0
    }

    return 1
}

# ── Help text ───────────────────────────────────────────────
function Show-Help {
    Write-Host ""
    Write-Host "  Usage:"
    Write-Host "    vpn <name> connect        Connect a profile"
    Write-Host "    vpn <name> disconnect     Disconnect a profile"
    Write-Host "    vpn <name> status         Show connection status"
    Write-Host "    vpn list                  List all profiles in Azure VPN Client"
    Write-Host "    vpn import <name>         Import <name>.AzureVpnProfile.xml from Desktop"
    Write-Host "    vpn export <name>         Export profile XML to Desktop"
    Write-Host "    vpn setup                 Re-check and install prerequisites"
    Write-Host ""
    Write-Host "  Examples:"
    Write-Host "    vpn list"
    Write-Host "    vpn my-vpn connect"
    Write-Host "    vpn my-vpn status"
    Write-Host "    vpn import my-vpn"
    Write-Host ""
}

# ── Restore window opacity ─────────────────────────────────
function Restore-VpnWindow {
    $proc = Get-Process | Where-Object { $_.MainWindowTitle -match 'Azure VPN Client' } | Select-Object -First 1
    if ($proc -and ([System.Management.Automation.PSTypeName]'VpnW32').Type) {
        Log "Restoring window opacity"
        [VpnW32]::SetLayeredWindowAttributes($proc.MainWindowHandle, 0, 255, [VpnW32]::LWA_ALPHA) | Out-Null
    }
}

# ── Main ────────────────────────────────────────────────────
Log "Args: Arg1='$Arg1' Arg2='$Arg2'"
Ensure-Prerequisites

$exitCode = 0
try {

switch ($Arg1) {

    # ── vpn setup ─────────────────────────────────────────
    "setup" {
        Log "Command: setup"
        if (Test-Path $setupDone) { Remove-Item $setupDone }
        Ensure-Prerequisites
        Write-Host "Setup complete."
    }

    # ── vpn list ───────────────────────────────────────────
    "list" {
        Log "Command: list"
        $profiles = @(Get-VpnProfiles)
        if (-not $profiles) {
            Write-Host "No VPN profiles found in Azure VPN Client."
            break
        }
        $active = Get-ActiveConnections
        Write-Host ""
        foreach ($name in $profiles) {
            $status = if ($active -match [regex]::Escape($name)) { "Connected" } else { "Disconnected" }
            Log "  Profile '$name': status=$status"
            Write-Host ("  {0,-36} {1}" -f $name, $status)
        }
        Write-Host ""
    }

    # ── vpn export <name> ─────────────────────────────────
    "export" {
        Log "Command: export"
        if (-not $Arg2) { Write-Host "Usage: vpn export <name>"; $exitCode = 1; break }
        $name = $Arg2

        $profiles = @(Get-VpnProfiles)
        if ($profiles -notcontains $name) {
            Write-Host "Profile '$name' not found. Run: vpn list"
            $exitCode = 1; break
        }

        $xml = Export-VpnProfile $name
        if (-not $xml) {
            Write-Host "Failed to extract profile data for '$name'."
            $exitCode = 1; break
        }

        $outPath = Join-Path ([Environment]::GetFolderPath('Desktop')) "$name.AzureVpnProfile.xml"
        Set-Content $outPath $xml -Encoding UTF8
        Write-Host "Exported to $outPath"
    }

    # ── vpn import <name> ─────────────────────────────────
    "import" {
        Log "Command: import"
        if (-not $Arg2) { Write-Host "Usage: vpn import <name>"; $exitCode = 1; break }
        $importName = $Arg2
        Log "Import name: $importName"

        $profiles = @(Get-VpnProfiles)
        if ($profiles -contains $importName) {
            Log "Profile '$importName' already in phonebook, skipping"
            Write-Host "Profile '$importName' already exists."
            break
        }

        Write-Host "Importing $importName from Desktop..."
        $code = Invoke-VpnAction "import" $importName
        if ($code -ne 0) {
            Log "Import failed with code $code"
            Write-Host "Import failed. Make sure '$importName.AzureVpnProfile.xml' is on your Desktop."
            $exitCode = 1; break
        }

        Write-Host "Imported"
    }

    # ── vpn <name> <action> ───────────────────────────────
    default {
        $name   = $Arg1
        $action = $Arg2
        Log "Command: default branch, name='$name' action='$action'"

        if (-not $name -or -not $action) {
            Log "Missing name or action, showing help"
            Show-Help
            break
        }

        # Resolve profile from phonebook
        $profiles = @(Get-VpnProfiles)
        if ($profiles -notcontains $name) {
            Log "Profile '$name' not found in phonebook"
            Write-Host "Profile '$name' not found. Run: vpn list"
            $exitCode = 1; break
        }

        # status - check live via rasdial
        if ($action -eq "status") {
            $active = Get-ActiveConnections
            $s = if ($active -match [regex]::Escape($name)) { "Connected" } else { "Disconnected" }
            Log "Status lookup (live): $s"
            Write-Host $s
            break
        }

        if ($action -notin @("connect", "disconnect")) {
            Log "Unknown action: $action"
            Write-Host "Unknown action '$action'. Use: connect, disconnect, status"
            $exitCode = 1; break
        }

        $code = Invoke-VpnAction $action $name
        if ($code -ne 0) {
            Log "Action '$action' failed with code $code"
            Write-Host "Action failed."
            $exitCode = 1; break
        }

        Log "Action '$action' completed"
    }
}

} finally {
    Restore-VpnWindow
}

exit $exitCode
