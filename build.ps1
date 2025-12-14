# Build Script for OsuMappingHelper
# This script builds both the C# project and the Rust msd-calculator,
# then copies the required tools to the output directory.

param(
    [string]$Configuration = "Release",
    [switch]$SkipRust = $false
)

$ErrorActionPreference = "Stop"

$ProjectRoot = $PSScriptRoot
$CSharpProject = Join-Path $ProjectRoot "OsuMappingHelper\OsuMappingHelper.csproj"
$RustProject = Join-Path $ProjectRoot "msd-calculator"
$BpmScript = Join-Path $ProjectRoot "bpm.py"

Write-Host "=== OsuMappingHelper Build Script ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "Project Root: $ProjectRoot"

# Step 1: Build Rust msd-calculator (if not skipped)
if (-not $SkipRust) {
    Write-Host "`n[1/3] Building msd-calculator (Rust)..." -ForegroundColor Yellow
    
    Push-Location $RustProject
    try {
        if ($Configuration -eq "Release") {
            cargo build --release
        } else {
            cargo build
        }
        if ($LASTEXITCODE -ne 0) {
            throw "Rust build failed with exit code $LASTEXITCODE"
        }
        Write-Host "Rust build completed successfully." -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
} else {
    Write-Host "`n[1/3] Skipping Rust build (--SkipRust specified)" -ForegroundColor Yellow
}

# Step 2: Build C# project
Write-Host "`n[2/3] Building OsuMappingHelper (C#)..." -ForegroundColor Yellow
dotnet build $CSharpProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "C# build failed with exit code $LASTEXITCODE"
}
Write-Host "C# build completed successfully." -ForegroundColor Green

# Step 3: Copy tools to output directory
Write-Host "`n[3/3] Copying tools to output directory..." -ForegroundColor Yellow

# Determine output directory
$OutputDir = Join-Path $ProjectRoot "OsuMappingHelper\bin\$Configuration\net8.0-windows"

# Create tools subdirectory
$ToolsDir = Join-Path $OutputDir "tools"
if (-not (Test-Path $ToolsDir)) {
    New-Item -ItemType Directory -Path $ToolsDir | Out-Null
}

# Copy bpm.py
Write-Host "  Copying bpm.py..."
Copy-Item $BpmScript -Destination $ToolsDir -Force

# Copy msd-calculator.exe
$MsdCalcExe = if ($Configuration -eq "Release") {
    Join-Path $RustProject "target\release\msd-calculator.exe"
} else {
    Join-Path $RustProject "target\debug\msd-calculator.exe"
}

if (Test-Path $MsdCalcExe) {
    Write-Host "  Copying msd-calculator.exe..."
    Copy-Item $MsdCalcExe -Destination $ToolsDir -Force
} else {
    Write-Host "  WARNING: msd-calculator.exe not found at $MsdCalcExe" -ForegroundColor Red
    Write-Host "  Run build without -SkipRust to build it first." -ForegroundColor Red
}

Write-Host "`n=== Build Complete ===" -ForegroundColor Green
Write-Host "Output directory: $OutputDir"
Write-Host "Tools directory: $ToolsDir"

# List copied tools
Write-Host "`nCopied tools:"
Get-ChildItem $ToolsDir | ForEach-Object { Write-Host "  - $($_.Name)" }
