BeforeAll {
    $script:here = Split-Path -Parent $PSCommandPath

    # ── Test infrastructure ───────────────────────────────────
    $script:testDir = Join-Path $env:TEMP "vpn-cli-tests-$(Get-Random)"
    New-Item $testDir -ItemType Directory -Force | Out-Null

    function Patch-Script([string]$src, [string]$actionMock) {
        $src = $src -replace '(?ms)function Invoke-VpnAction\([^)]*\)\s*\{.*?^    return 1\r?\n\}', $actionMock
        $src = $src -replace [regex]::Escape('Join-Path $env:LOCALAPPDATA "Packages\Microsoft.AzureVpn_8wekyb3d8bbwe\LocalState\rasphone.pbk"'),
            'Join-Path $scriptDir "test-rasphone.pbk"'
        $src = $src -replace '(?s)function Get-ActiveConnections\s*\{.*?\}',
            'function Get-ActiveConnections { $f = Join-Path $scriptDir "test-active.txt"; if (Test-Path $f) { return Get-Content $f -Raw }; return "No connections" }'
        $src = $src -replace '(?ms)function Ensure-Prerequisites\s*\{.*?^\}',
            'function Ensure-Prerequisites { }'
        $src = $src -replace '(?ms)function Restore-VpnWindow\s*\{.*?^\}',
            'function Restore-VpnWindow { }'
        $src = $src -replace 'Add-Type -AssemblyName UIAutomation\w+', '# (mocked)'
        return $src
    }

    $raw = Get-Content "$here\vpn.ps1" -Raw
    Set-Content "$testDir\vpn.ps1"      (Patch-Script $raw 'function Invoke-VpnAction([string]$action, [string]$name) { return 0 }')
    Set-Content "$testDir\vpn-fail.ps1" (Patch-Script $raw 'function Invoke-VpnAction([string]$action, [string]$name) { return 1 }')

    # ── Helpers ───────────────────────────────────────────────

    function Encode-ProfileXml([string]$xml) {
        $bytes = [System.Text.Encoding]::Unicode.GetBytes($xml)
        return ($bytes | ForEach-Object { $_.ToString("X2") }) -join ''
    }

    function Set-TestPhonebook([string[]]$names, [hashtable]$xmlData) {
        $content = ""
        foreach ($n in $names) {
            $content += "[$n]`nEncoding=1`n"
            if ($xmlData -and $xmlData[$n]) {
                $hex = Encode-ProfileXml $xmlData[$n]
                $content += "ThirdPartyProfileInfo=$hex`n"
            }
            $content += "`n"
        }
        Set-Content "$testDir\test-rasphone.pbk" $content
    }

    function Clear-TestPhonebook {
        if (Test-Path "$testDir\test-rasphone.pbk") { Remove-Item "$testDir\test-rasphone.pbk" }
    }

    function Set-TestActive([string[]]$names) {
        $content = "Connected to`n"
        foreach ($n in $names) { $content += "$n`n" }
        $content += "Command completed successfully."
        Set-Content "$testDir\test-active.txt" $content
    }

    function Set-TestNoActive {
        Set-Content "$testDir\test-active.txt" "No connections"
    }

    function Invoke-Vpn([string]$a1, [string]$a2, [switch]$Fail) {
        $script = if ($Fail) { "$testDir\vpn-fail.ps1" } else { "$testDir\vpn.ps1" }
        $cmd = "& '$script'"
        if ($a1) { $cmd += " '$a1'" }
        if ($a2) { $cmd += " '$a2'" }
        $out = powershell -ExecutionPolicy Bypass -NoProfile -Command $cmd 2>&1 | Out-String
        @{ Output = $out.Trim(); ExitCode = $LASTEXITCODE }
    }
}

AfterAll {
    if ($testDir -and (Test-Path $testDir)) {
        Remove-Item $testDir -Recurse -Force
    }
}

# ── Tests ─────────────────────────────────────────────────

Describe "vpn help" {
    BeforeAll { Set-TestPhonebook @() }
    It "shows usage text" {
        $r = Invoke-Vpn
        $r.Output | Should -Match "Usage:"
    }
    It "exits 0" {
        $r = Invoke-Vpn
        $r.ExitCode | Should -Be 0
    }
    It "lists all available commands" {
        $r = Invoke-Vpn
        $r.Output | Should -Match "connect"
        $r.Output | Should -Match "disconnect"
        $r.Output | Should -Match "status"
        $r.Output | Should -Match "list"
        $r.Output | Should -Match "import"
        $r.Output | Should -Match "export"
        $r.Output | Should -Match "setup"
    }
}

Describe "vpn list" {
    It "says no profiles when phonebook is missing" {
        Clear-TestPhonebook
        $r = Invoke-Vpn "list"
        $r.Output   | Should -Match "No VPN profiles found"
        $r.ExitCode | Should -Be 0
    }

    It "shows profiles from phonebook with live status" {
        Set-TestPhonebook @("vpn-prod", "my-vpn")
        Set-TestActive @("my-vpn")
        $r = Invoke-Vpn "list"
        $r.Output | Should -Match "vpn-prod"
        $r.Output | Should -Match "Disconnected"
        $r.Output | Should -Match "my-vpn"
        $r.Output | Should -Match "Connected"
    }

    It "shows all disconnected when no active connections" {
        Set-TestPhonebook @("vpn-prod", "my-vpn")
        Set-TestNoActive
        $r = Invoke-Vpn "list"
        $r.Output | Should -Match "vpn-prod.*Disconnected"
        $r.Output | Should -Match "my-vpn.*Disconnected"
    }

    It "shows multiple profiles" {
        Set-TestPhonebook @("vpn-prod", "my-vpn", "vpn-dev")
        Set-TestNoActive
        $r = Invoke-Vpn "list"
        $r.Output | Should -Match "vpn-prod"
        $r.Output | Should -Match "my-vpn"
        $r.Output | Should -Match "vpn-dev"
    }

    It "sorts profiles alphabetically" {
        Set-TestPhonebook @("vpn-zulu", "vpn-alpha")
        Set-TestNoActive
        $r = Invoke-Vpn "list"
        $r.Output | Should -Match "(?s)alpha.*zulu"
    }
}

Describe "vpn export" {
    It "errors when name is missing" {
        $r = Invoke-Vpn "export"
        $r.Output   | Should -Match "Usage:"
        $r.ExitCode | Should -Be 1
    }

    It "errors when profile not found" {
        Set-TestPhonebook @("my-vpn")
        $r = Invoke-Vpn "export" "nope"
        $r.Output   | Should -Match "not found"
        $r.ExitCode | Should -Be 1
    }

    It "exports profile XML to Desktop" {
        $testXml = '<azvpnprofile><name>my-vpn</name></azvpnprofile>'
        Set-TestPhonebook @("my-vpn") @{ "my-vpn" = $testXml }
        $r = Invoke-Vpn "export" "my-vpn"
        $r.Output   | Should -Match "Exported to"
        $r.ExitCode | Should -Be 0

        $outPath = Join-Path ([Environment]::GetFolderPath('Desktop')) "my-vpn.AzureVpnProfile.xml"
        Test-Path $outPath | Should -Be $true
        $content = Get-Content $outPath -Raw
        $content | Should -Match '<azvpnprofile>'
        $content | Should -Match 'my-vpn'
        Remove-Item $outPath
    }

    It "fails when profile has no ThirdPartyProfileInfo" {
        Set-TestPhonebook @("my-vpn")
        $r = Invoke-Vpn "export" "my-vpn"
        $r.Output   | Should -Match "Failed to extract"
        $r.ExitCode | Should -Be 1
    }

    It "fails when phonebook is missing" {
        Clear-TestPhonebook
        $r = Invoke-Vpn "export" "my-vpn"
        $r.Output   | Should -Match "not found"
        $r.ExitCode | Should -Be 1
    }
}

Describe "vpn import" {
    It "errors when alias is missing" {
        $r = Invoke-Vpn "import"
        $r.Output   | Should -Match "Usage:"
        $r.ExitCode | Should -Be 1
    }

    It "imports a new profile" {
        Set-TestPhonebook @()
        $r = Invoke-Vpn "import" "my-vpn"
        $r.Output   | Should -Match "Imported"
        $r.ExitCode | Should -Be 0
    }

    It "skips when profile already in phonebook" {
        Set-TestPhonebook @("my-vpn")
        $r = Invoke-Vpn "import" "my-vpn"
        $r.Output   | Should -Match "already exists"
        $r.ExitCode | Should -Be 0
    }

    It "reports failure when action fails" {
        Set-TestPhonebook @()
        $r = Invoke-Vpn "import" "my-vpn" -Fail
        $r.Output   | Should -Match "Import failed"
        $r.ExitCode | Should -Be 1
    }
}

Describe "vpn [name] connect" {
    It "succeeds with exit code 0" {
        Set-TestPhonebook @("my-vpn")
        $r = Invoke-Vpn "my-vpn" "connect"
        $r.ExitCode | Should -Be 0
    }

    It "reports failure when action fails" {
        Set-TestPhonebook @("my-vpn")
        $r = Invoke-Vpn "my-vpn" "connect" -Fail
        $r.Output   | Should -Match "Action failed"
        $r.ExitCode | Should -Be 1
    }
}

Describe "vpn [name] disconnect" {
    It "succeeds with exit code 0" {
        Set-TestPhonebook @("my-vpn")
        $r = Invoke-Vpn "my-vpn" "disconnect"
        $r.ExitCode | Should -Be 0
    }

    It "reports failure when action fails" {
        Set-TestPhonebook @("my-vpn")
        $r = Invoke-Vpn "my-vpn" "disconnect" -Fail
        $r.Output   | Should -Match "Action failed"
        $r.ExitCode | Should -Be 1
    }
}

Describe "vpn [name] status" {
    It "shows Connected when profile is active" {
        Set-TestPhonebook @("my-vpn")
        Set-TestActive @("my-vpn")
        $r = Invoke-Vpn "my-vpn" "status"
        $r.Output   | Should -Match "Connected"
        $r.ExitCode | Should -Be 0
    }

    It "shows Disconnected when profile is not active" {
        Set-TestPhonebook @("my-vpn")
        Set-TestNoActive
        $r = Invoke-Vpn "my-vpn" "status"
        $r.Output   | Should -Match "Disconnected"
        $r.ExitCode | Should -Be 0
    }
}

Describe "vpn setup" {
    It "outputs setup complete" {
        Set-TestPhonebook @()
        $r = Invoke-Vpn "setup"
        $r.Output   | Should -Match "Setup complete"
        $r.ExitCode | Should -Be 0
    }
}

Describe "vpn error handling" {
    It "errors when profile does not exist in phonebook" {
        Set-TestPhonebook @("my-vpn")
        $r = Invoke-Vpn "nope" "connect"
        $r.Output   | Should -Match "not found"
        $r.ExitCode | Should -Be 1
    }

    It "errors on unknown action" {
        Set-TestPhonebook @("my-vpn")
        $r = Invoke-Vpn "my-vpn" "restart"
        $r.Output   | Should -Match "Unknown action"
        $r.ExitCode | Should -Be 1
    }

    It "errors for status on unknown profile" {
        Set-TestPhonebook @("my-vpn")
        $r = Invoke-Vpn "nope" "status"
        $r.Output   | Should -Match "not found"
        $r.ExitCode | Should -Be 1
    }

    It "errors for disconnect on unknown profile" {
        Set-TestPhonebook @("my-vpn")
        $r = Invoke-Vpn "nope" "disconnect"
        $r.Output   | Should -Match "not found"
        $r.ExitCode | Should -Be 1
    }
}
