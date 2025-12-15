# Build Script for OsuMappingHelper
# This script builds both the C# project and the Rust msd-calculator,
# then copies the required tools to the output directory.
#
# For bpm.exe: PyInstaller automatically detects dependencies from bpm.py imports.
# Only librosa, numpy, and scipy are needed (see requirements-bpm.txt).
# The script excludes common unnecessary modules to keep the executable size small.

param(
    [string]$Configuration = "Release",
    [switch]$SkipRust = $false,
    [switch]$SkipBpm = $false
)

$ErrorActionPreference = "Stop"

$ProjectRoot = $PSScriptRoot
$CSharpProject = Join-Path $ProjectRoot "OsuMappingHelper\OsuMappingHelper.csproj"
$RustProject = Join-Path $ProjectRoot "msd-calculator"
$BpmScript = Join-Path $ProjectRoot "bpm.py"
$DansConfig = Join-Path $ProjectRoot "dans.json"

Write-Host "=== OsuMappingHelper Build Script ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "Project Root: $ProjectRoot"

# Step 1: Build Rust msd-calculator (if not skipped)
if (-not $SkipRust) {
    Write-Host "`n[1/4] Building msd-calculator (Rust)..." -ForegroundColor Yellow
    
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
    Write-Host "`n[1/4] Skipping Rust build (--SkipRust specified)" -ForegroundColor Yellow
}

    if (-not $SkipBpm) {
    # Step 2: Build bpm.exe from bpm.py using PyInstaller
    Write-Host "`n[2/4] Building bpm.exe from bpm.py..." -ForegroundColor Yellow

    # Check if Python is available
    $pythonCmd = $null
    if (Get-Command py -ErrorAction SilentlyContinue) {
        $pythonCmd = "py"
    } elseif (Get-Command python3 -ErrorAction SilentlyContinue) {
        $pythonCmd = "python"
    } else {
        throw "Python not found. Please install Python to build bpm.exe"
    }

    Write-Host "  Using Python: $pythonCmd"

    # Check if PyInstaller is installed, install if not
    Write-Host "  Checking for PyInstaller..."
    $ErrorActionPreference = "SilentlyContinue"
    $pipShowOutput = & $pythonCmd -m pip show pyinstaller 2>$null
    $pyInstallerInstalled = ($LASTEXITCODE -eq 0) -and ($pipShowOutput -ne $null)
    $ErrorActionPreference = "Stop"

    if (-not $pyInstallerInstalled) {
        Write-Host "  Installing PyInstaller..." -ForegroundColor Yellow
        & $pythonCmd -m pip install pyinstaller --quiet
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to install PyInstaller"
        }
        Write-Host "  PyInstaller installed successfully." -ForegroundColor Green
    } else {
        Write-Host "  PyInstaller is already installed." -ForegroundColor Green
    }

    # Create a temporary directory for PyInstaller output
    $TempBuildDir = Join-Path $ProjectRoot "bpm_build_temp"
    if (Test-Path $TempBuildDir) {
        Remove-Item $TempBuildDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $TempBuildDir | Out-Null

    try {
        # Build standalone executable with PyInstaller
        Write-Host "  Building standalone executable..."
        $distDir = Join-Path $TempBuildDir "dist"
        $buildDir = Join-Path $TempBuildDir "build"
        $specFile = Join-Path $TempBuildDir "bpm.spec"
        
        # Build PyInstaller command arguments
        $pyInstallerArgs = @(
            "--name", "bpm",
            "--onefile",
            "--console",
            "--distpath", $distDir,
            "--workpath", $buildDir,
            "--specpath", $TempBuildDir,
            "--clean"
        )
        
        # Add exclude-module arguments for each module to exclude
        foreach ($module in $excludeModules) {
            $pyInstallerArgs += "--exclude-module"
            $pyInstallerArgs += $module
        }
        
        # Add the script file
        $pyInstallerArgs += $BpmScript
        
        # Run PyInstaller to create one-file executable with minimal dependencies
        & $pythonCmd -m PyInstaller $pyInstallerArgs
        
        if ($LASTEXITCODE -ne 0) {
            throw "PyInstaller failed with exit code $LASTEXITCODE"
        }
        
        $BpmExe = Join-Path $distDir "bpm.exe"
        if (-not (Test-Path $BpmExe)) {
            throw "bpm.exe was not created by PyInstaller"
        }
        
        Write-Host "  bpm.exe built successfully." -ForegroundColor Green
    }
    finally {
        # Clean up temporary build files (keep dist for copying)
        if (Test-Path $buildDir) {
            Remove-Item $buildDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        if (Test-Path $specFile) {
            Remove-Item $specFile -Force -ErrorAction SilentlyContinue
        }
    }
}

# Step 3: Build C# project
Write-Host "`n[3/4] Building OsuMappingHelper (C#)..." -ForegroundColor Yellow
dotnet build $CSharpProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "C# build failed with exit code $LASTEXITCODE"
}
Write-Host "C# build completed successfully." -ForegroundColor Green

# Step 4: Copy tools to output directory
Write-Host "`n[4/4] Copying tools to output directory..." -ForegroundColor Yellow

# Determine output directory
$OutputDir = Join-Path $ProjectRoot "OsuMappingHelper\bin\$Configuration\net8.0-windows"

# Create tools subdirectory
$ToolsDir = Join-Path $OutputDir "tools"
if (-not (Test-Path $ToolsDir)) {
    New-Item -ItemType Directory -Path $ToolsDir | Out-Null
}

# Copy bpm.exe
Write-Host "  Copying bpm.exe..."
$BpmExeSource = Join-Path $TempBuildDir "dist\bpm.exe"
if (Test-Path $BpmExeSource) {
    Copy-Item $BpmExeSource -Destination $ToolsDir -Force
    Write-Host "  bpm.exe copied successfully." -ForegroundColor Green
} else {
    throw "bpm.exe not found at $BpmExeSource"
}

# Copy dans.json (to output directory, next to exe)
Write-Host "  Copying dans.json..."
Copy-Item $DansConfig -Destination $OutputDir -Force

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

# Clean up temporary build directory
if (Test-Path $TempBuildDir) {
    Write-Host "  Cleaning up temporary build files..."
    Remove-Item $TempBuildDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`n=== Build Complete ===" -ForegroundColor Green
Write-Host "Output directory: $OutputDir"
Write-Host "Tools directory: $ToolsDir"

# List copied files
Write-Host "`nCopied files:"
Write-Host "  - dans.json (config)"
Get-ChildItem $ToolsDir | ForEach-Object { Write-Host "  - tools/$($_.Name)" }
