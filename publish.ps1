<#
.SYNOPSIS
    Packages ArcadeShellSelector + ArcadeShellConfigurator for deployment.

    REQUIRES PowerShell 7+ (pwsh). Run with:  pwsh .\publish.ps1
    (Windows PowerShell 5.1 is not supported)

.DESCRIPTION
    Two modes:
      - Default (framework-dependent): builds then copies bin\Release output to deploy\ArcadeShell\.
        Target machine must have .NET 10 Runtime installed.
      - -SelfContained: runs dotnet publish for both apps (win-x64).
        Target machine needs NO .NET runtime pre-installed (~80 MB larger).

.PARAMETER SelfContained
    Produce a self-contained package (no .NET runtime required on target).

.PARAMETER SkipBuild
    Skip the dotnet build/publish step; copies from whatever is already in the
    build output folder. Use this after you have already built manually.

.PARAMETER StripPdb
    Remove *.pdb debug symbol files from the deploy folder before zipping.

.PARAMETER Version
    Version string embedded in the ZIP filename.
    Defaults to the <Version> value in ArcadeShellSelector.csproj + short git commit hash.
    Override with e.g. -Version "1.2.0" to force a specific string.

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -SelfContained
    .\publish.ps1 -SelfContained -StripPdb -Version "1.0.0"
    .\publish.ps1 -SkipBuild
#>

[CmdletBinding()]
param(
    [switch] $SelfContained,
    [switch] $SkipBuild,
    [switch] $StripPdb,
    [string] $Version = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root       = $PSScriptRoot
$mainCsproj = Join-Path $root "ArcadeShellSelector.csproj"
$cfgCsproj  = Join-Path $root "ArcadeShellConfigurator\ArcadeShellConfigurator.csproj"
$buildOut   = Join-Path $root "bin\Release\net10.0-windows"

# --- Resolve version ---
if ([string]::IsNullOrWhiteSpace($Version)) {
    # Read <Version> from csproj
    [xml]$csprojXml = Get-Content $mainCsproj
    $csprojVersion = $csprojXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($csprojVersion)) { $csprojVersion = "0.0.0" }

    # Append short git commit hash if inside a git repo
    $gitHash = ""
    try {
        $gitHash = (git -C $root rev-parse --short HEAD 2>$null)
        if ($LASTEXITCODE -ne 0) { $gitHash = "" }
    } catch { $gitHash = "" }

    $Version = if ($gitHash) { "$csprojVersion+$gitHash" } else { $csprojVersion }
}

Write-Host "Version : $Version" -ForegroundColor Cyan

$deployDir  = Join-Path $root "deploy\ArcadeShell"
$zipName    = "ArcadeShell-v$Version-win-x64.zip"
$zipPath    = Join-Path $root "deploy\$zipName"

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "=== $msg" -ForegroundColor Cyan
}

function Assert-DotNet {
    if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
        Write-Error "dotnet SDK not found. Please install the .NET SDK."
    }
}

# --- 1. Prepare deploy folder ---
Write-Step "Preparing deploy folder"
if (Test-Path $deployDir) { Remove-Item $deployDir -Recurse -Force }
New-Item -ItemType Directory -Force $deployDir | Out-Null
$deployParent = Split-Path $zipPath
if (-not (Test-Path $deployParent)) { New-Item -ItemType Directory -Force $deployParent | Out-Null }

# --- 2. Build (skipped if -SkipBuild) ---
if (-not $SkipBuild) {
    Assert-DotNet
    if ($SelfContained) {
        Write-Step "Publishing ArcadeShellSelector (self-contained win-x64)"
        dotnet publish $mainCsproj -p:PublishProfile=win-x64 -c Release --nologo
        if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed for ArcadeShellSelector." }

        Write-Step "Publishing ArcadeShellConfigurator (self-contained win-x64)"
        dotnet publish $cfgCsproj -c Release -r win-x64 --self-contained -o $deployDir --nologo
        if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed for ArcadeShellConfigurator." }
    } else {
        Write-Step "Building solution (Release, framework-dependent)"
        dotnet build $root -c Release --nologo
        if ($LASTEXITCODE -ne 0) { Write-Error "dotnet build failed." }

        Write-Step "Publishing ArcadeShellConfigurator (framework-dependent)"
        dotnet publish $cfgCsproj -c Release --no-self-contained -o $deployDir --nologo
        if ($LASTEXITCODE -ne 0) { Write-Warning "dotnet publish for ArcadeShellConfigurator failed — configurator may be missing from package." }
    }
}

# --- 3. Populate deploy folder ---
# Self-contained publish writes directly to deployDir via the publish profile.
# Framework-dependent (build + copy) always reads from buildOut.
if (-not $SelfContained) {
    if (-not (Test-Path $buildOut)) {
        Write-Error "Build output not found: $buildOut`nRun the script without -SkipBuild, or build manually first."
    }
    Write-Step "Copying build output to deploy folder"

    # Copy only known app files/folders (skip stray files like old installers)
    $itemsToCopy = @(
        "ArcadeShellSelector.exe",
        "ArcadeShellSelector.dll",
        "ArcadeShellSelector.runtimeconfig.json",
        "ArcadeShellSelector.deps.json",
        "ArcadeShellConfigurator.exe",
        "ArcadeShellConfigurator.dll",
        "ArcadeShellConfigurator.runtimeconfig.json",
        "ArcadeShellConfigurator.deps.json",
        "config.json",
        "app.ico",
        "lib",
        "libvlc",
        "Media"
    )
    foreach ($item in $itemsToCopy) {
        $src = Join-Path $buildOut $item
        if (Test-Path $src) {
            Copy-Item -Path $src -Destination $deployDir -Recurse -Force
        }
    }

    # Fallback: copy Media folder from source root if absent from build output
    $deployMedia = Join-Path $deployDir "Media"
    if (-not (Test-Path $deployMedia)) {
        $srcMedia = Join-Path $root "Media"
        if (Test-Path $srcMedia) {
            Copy-Item -Path $srcMedia -Destination $deployDir -Recurse -Force
            Write-Host "  Copied Media from source root (fallback)." -ForegroundColor DarkYellow
        } else {
            foreach ($sub in @("Bkg", "Img", "Music")) {
                New-Item -ItemType Directory -Force (Join-Path $deployMedia $sub) | Out-Null
            }
            Write-Host "  Created empty Media\{Bkg,Img,Music} folders." -ForegroundColor DarkYellow
        }
    }
}

# --- 4. Ensure libvlc native DLLs are present ---
# VideoLAN.LibVLC.Windows NuGet package targets copy native files to libvlc\win-x64\.
# Safety fallback: copy from regular build output if missing from deploy.
$deployVlc = Join-Path $deployDir "libvlc"
$buildVlc  = Join-Path $buildOut "libvlc"

if (-not (Test-Path $deployVlc)) {
    if (Test-Path $buildVlc) {
        Write-Step "Copying libvlc native folder from build output (safety fallback)"
        Copy-Item -Path $buildVlc -Destination $deployDir -Recurse -Force
    } else {
        Write-Warning "libvlc folder not found in deploy or build output. Video playback may not work."
    }
}

$vlcDll = Join-Path $deployVlc "win-x64\libvlc.dll"
if (-not (Test-Path $vlcDll)) {
    Write-Warning "libvlc.dll not found at: $vlcDll — VLC native DLLs may be missing."
}

# --- 5. Strip PDB files ---
if ($StripPdb) {
    Write-Step "Stripping *.pdb debug symbols"
    $pdbs = @(Get-ChildItem $deployDir -Recurse -Filter "*.pdb")
    foreach ($pdb in $pdbs) { Remove-Item $pdb.FullName -Force }
    Write-Host "  Removed $($pdbs.Count) PDB file(s)."
}

# --- 6. Verify required files ---
Write-Step "Verifying required files"
$requiredFiles = @("ArcadeShellSelector.exe", "ArcadeShellConfigurator.exe", "config.json", "app.ico",
                   "Media\Bkg", "Media\Img", "Media\Music")
$allOk = $true
foreach ($f in $requiredFiles) {
    $p = Join-Path $deployDir $f
    if (Test-Path $p) {
        Write-Host "  [OK]      $f" -ForegroundColor Green
    } else {
        Write-Host "  [MISSING] $f" -ForegroundColor Yellow
        $allOk = $false
    }
}
if (-not $allOk) {
    Write-Warning "Some required files are missing. The package may be incomplete."
}

# --- 7. Create ZIP ---
Write-Step "Creating ZIP archive"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# Use .NET's ZipFile API directly for robustness (handles files that Compress-Archive struggles with)
Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
    [System.IO.Compression.ZipFile]::CreateFromDirectory($deployDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
} catch {
    Write-Warning "ZipFile API failed, falling back to Compress-Archive: $_"
    Compress-Archive -Path "$deployDir\*" -DestinationPath $zipPath -CompressionLevel Optimal -Force
}

$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)

Write-Host ""
Write-Host "[DONE] Package ready!" -ForegroundColor Green
Write-Host "  Deploy folder : $deployDir"
Write-Host "  ZIP file      : $zipPath ($zipSize MB)"
Write-Host ""
Write-Host "To deploy:"
Write-Host "  1. Copy and extract the ZIP to the target machine."
if ($SelfContained) {
    Write-Host "  2. Run ArcadeShellSelector.exe  (no .NET runtime required)"
} else {
    Write-Host "  2. Install .NET 10 Desktop Runtime: https://dotnet.microsoft.com/download"
    Write-Host "  3. Run ArcadeShellSelector.exe"
}

