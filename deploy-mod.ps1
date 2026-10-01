# Deployment script for LessMenusMoreImmersion
# Usage:
#   .\deploy-mod.ps1          - Build Release, deploy to game, create zip
#   .\deploy-mod.ps1 -Debug   - Build Debug, deploy to game (no zip)
#   .\deploy-mod.ps1 -NoBuild - Skip build, just deploy + zip from last build

param(
    [switch]$Debug,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$ProjectDir = $PSScriptRoot
$ModName = "LessMenusMoreImmersion"
$BannerlordPath = $env:BANNERLORD_GAME_DIR
if (-not $BannerlordPath) {
    $BannerlordPath = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
}
$ModulePath = "$BannerlordPath\Modules\$ModName"
$BinTarget = "$ModulePath\bin\Win64_Shipping_Client"

$Config = if ($Debug) { "Debug" } else { "Release" }
$OutputPath = "$ProjectDir\bin\$Config\net472"

# Read version from csproj
$Version = ([xml](Get-Content "$ProjectDir\$ModName.csproj")).Project.PropertyGroup[0].Version

Write-Host "======================================"
Write-Host " $ModName v$Version ($Config)"
Write-Host "======================================"
Write-Host ""

# 1. Build
if (-not $NoBuild) {
    Write-Host "[1/4] Building $Config..." -ForegroundColor Cyan
    dotnet build -c $Config "$ProjectDir\$ModName.csproj"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Build failed." -ForegroundColor Red
        exit 1
    }
    Write-Host ""
}

# 2. Verify output
if (-not (Test-Path "$OutputPath\$ModName.dll")) {
    Write-Host "ERROR: DLL not found at $OutputPath\$ModName.dll" -ForegroundColor Red
    Write-Host "Run without -NoBuild to compile first."
    exit 1
}

if (-not (Test-Path "$BannerlordPath\bin\Win64_Shipping_Client\Bannerlord.exe")) {
    Write-Host "ERROR: Bannerlord not found at $BannerlordPath" -ForegroundColor Red
    Write-Host "Set BANNERLORD_GAME_DIR env var to your game install path."
    exit 1
}

# 3. Deploy to Modules folder
Write-Host "[2/4] Deploying to Modules folder..." -ForegroundColor Cyan

New-Item -ItemType Directory -Force -Path $BinTarget | Out-Null
Copy-Item "$OutputPath\$ModName.dll" -Destination $BinTarget -Force
Write-Host "  DLL -> $BinTarget"

# Copy McmBridge if it exists
if (Test-Path "$OutputPath\$ModName.McmBridge.dll") {
    Copy-Item "$OutputPath\$ModName.McmBridge.dll" -Destination $BinTarget -Force
    Write-Host "  McmBridge DLL -> $BinTarget"
}

# SubModule.xml — stamp version from csproj
$SubModuleContent = Get-Content "$ProjectDir\_Module\SubModule.xml" -Raw
$SubModuleContent = $SubModuleContent -replace '(<Version value="v)[^"]*(")', "`${1}$Version`${2}"
$SubModuleContent | Set-Content "$ModulePath\SubModule.xml" -Encoding utf8
Write-Host "  SubModule.xml -> $ModulePath (v$Version)"

# ModuleData
$ModuleDataSrc = "$ProjectDir\_Module\ModuleData"
$ModuleDataDst = "$ModulePath\ModuleData"
if (Test-Path $ModuleDataSrc) {
    Copy-Item $ModuleDataSrc -Destination $ModulePath -Recurse -Force
    Write-Host "  ModuleData -> $ModuleDataDst"
}

Write-Host ""
Write-Host "[3/4] Deploy complete." -ForegroundColor Green
Write-Host ""

# 4. Create distribution zip (Release only)
if (-not $Debug) {
    Write-Host "[4/4] Creating distribution zip..." -ForegroundColor Cyan
    $ZipName = "$ModName-v$Version.zip"
    $ZipPath = "$ProjectDir\$ZipName"

    if (Test-Path $ZipPath) {
        Remove-Item $ZipPath -Force
    }

    Compress-Archive -Path $ModulePath -DestinationPath $ZipPath -CompressionLevel Optimal
    $ZipSize = [math]::Round((Get-Item $ZipPath).Length / 1KB, 1)
    Write-Host "  $ZipName ($ZipSize KB)" -ForegroundColor Green
    Write-Host "  Path: $ZipPath"
} else {
    Write-Host "[4/4] Skipping zip (Debug build)." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
