param(
    [string]$OutputRoot,
    [string]$Version = "1.0.0.0"
)

$ErrorActionPreference = "Stop"

$helperRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $helperRoot "out"
}

function Get-WindowsKitTool {
    param([string]$ToolName)

    $kitRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    $tool = Get-ChildItem -Path $kitRoot -Recurse -Filter $ToolName -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\x64\\$([regex]::Escape($ToolName))$" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if (-not $tool) {
        throw "Could not find $ToolName under $kitRoot"
    }

    return $tool.FullName
}

function Get-WindowsWinmd {
    $metadataRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\UnionMetadata"
    $winmd = Get-ChildItem -Path $metadataRoot -Recurse -Filter Windows.winmd -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch "\\Facade\\" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if (-not $winmd) {
        throw "Could not find Windows.winmd under $metadataRoot"
    }

    return $winmd.FullName
}

function New-HelperAsset {
    param(
        [string]$Path,
        [int]$Width,
        [int]$Height
    )

    Add-Type -AssemblyName System.Drawing
    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(0, 102, 204))
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Convert-PackageVersion {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "Version cannot be empty"
    }

    $parts = $Value.Split(".")
    if ($parts.Count -eq 3) {
        $parts += "0"
    }

    if ($parts.Count -ne 4) {
        throw "Package version must have three or four numeric parts"
    }

    foreach ($part in $parts) {
        $number = 0
        if (-not [int]::TryParse($part, [ref]$number) -or $number -lt 0 -or $number -gt 65535) {
            throw "Invalid package version part: $part"
        }
    }

    return ($parts -join ".")
}

$packageDir = Join-Path $OutputRoot "package"
$assetsDir = Join-Path $packageDir "Assets"
$source = Join-Path $helperRoot "src\VpnPackagedHelper.cs"
$manifestSource = Join-Path $helperRoot "manifests\AppxManifest.xml"
$exe = Join-Path $OutputRoot "VpnPackagedHelper.exe"
$msix = Join-Path $OutputRoot "VpnPackagedHelper.msix"
$packageVersion = Convert-PackageVersion $Version

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null

$framework = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$csc = Join-Path $framework "csc.exe"
$winmd = Get-WindowsWinmd
$makeappx = Get-WindowsKitTool "makeappx.exe"
$systemRuntime = Join-Path $framework "System.Runtime.dll"
$windowsRuntime = Join-Path $framework "System.Runtime.WindowsRuntime.dll"
$interopRuntime = Join-Path $framework "System.Runtime.InteropServices.WindowsRuntime.dll"

& $csc /nologo /platform:x64 /target:exe /out:$exe `
    /r:$winmd `
    /r:$systemRuntime `
    /r:$windowsRuntime `
    /r:$interopRuntime `
    $source

if ($LASTEXITCODE -ne 0) {
    throw "csc failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath $exe -Destination (Join-Path $packageDir "VpnPackagedHelper.exe") -Force

$manifestDestination = Join-Path $packageDir "AppxManifest.xml"
[xml]$manifest = Get-Content -LiteralPath $manifestSource -Raw
$manifest.Package.Identity.Version = $packageVersion
$manifest.Save($manifestDestination)

New-HelperAsset -Path (Join-Path $assetsDir "StoreLogo.png") -Width 50 -Height 50
New-HelperAsset -Path (Join-Path $assetsDir "Square44x44Logo.png") -Width 44 -Height 44
New-HelperAsset -Path (Join-Path $assetsDir "Square150x150Logo.png") -Width 150 -Height 150

& $makeappx pack /d $packageDir /p $msix /o
if ($LASTEXITCODE -ne 0) {
    throw "makeappx failed with exit code $LASTEXITCODE"
}

[pscustomobject]@{
    Version = $packageVersion
    PackageDirectory = $packageDir
    Msix = $msix
}
