# Build Script for OsuMappingHelper
# This script builds both the C# project and the Rust msd-calculator,
# then copies the required tools to the output directory and creates
# a Squirrel.Windows installer package.
#
# For bpm.exe: PyInstaller automatically detects dependencies from bpm.py imports.
# Only librosa, numpy, and scipy are needed (see requirements-bpm.txt).
# The script excludes common unnecessary modules to keep the executable size small.

param(
    [string]$Configuration = "Release",
    [switch]$SkipRust = $false,
    [switch]$SkipBpm = $false,
    [switch]$SkipFfmpeg = $false,
    [switch]$SkipSquirrel = $false
)

$ErrorActionPreference = "Stop"

$ProjectRoot = $PSScriptRoot
$CSharpProject = Join-Path $ProjectRoot "OsuMappingHelper\OsuMappingHelper.csproj"
$RustProject515 = Join-Path $ProjectRoot "msd-calculator"
$RustProject505 = Join-Path $ProjectRoot "msd-calculator-505"
$BpmScript = Join-Path $ProjectRoot "bpm.py"
$DansConfig = Join-Path $ProjectRoot "dans.json"

Write-Host "=== OsuMappingHelper Build Script ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "Project Root: $ProjectRoot"

# Step 1: Build Rust msd-calculators (if not skipped)
if (-not $SkipRust) {
    Write-Host "`n[1/6] Building msd-calculators (Rust)..." -ForegroundColor Yellow
    
    # Build msd-calculator-515 (new MinaCalc 5.15)
    Write-Host "  Building msd-calculator-515 (MinaCalc 5.15)..." -ForegroundColor Cyan
    Push-Location $RustProject515
    try {
        if ($Configuration -eq "Release") {
            cargo build --release
        } else {
            cargo build
        }
        if ($LASTEXITCODE -ne 0) {
            throw "Rust build (515) failed with exit code $LASTEXITCODE"
        }
        Write-Host "  msd-calculator-515 build completed." -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
    
    # Build msd-calculator-505 (legacy MinaCalc 5.05)
    Write-Host "  Building msd-calculator-505 (MinaCalc 5.05)..." -ForegroundColor Cyan
    Push-Location $RustProject505
    try {
        if ($Configuration -eq "Release") {
            cargo build --release
        } else {
            cargo build
        }
        if ($LASTEXITCODE -ne 0) {
            throw "Rust build (505) failed with exit code $LASTEXITCODE"
        }
        Write-Host "  msd-calculator-505 build completed." -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
    
    Write-Host "All Rust builds completed successfully." -ForegroundColor Green
} else {
    Write-Host "`n[1/6] Skipping Rust build (--SkipRust specified)" -ForegroundColor Yellow
}

if (-not $SkipBpm) {
    # Step 2: Build bpm.exe from bpm.py using PyInstaller in isolated venv
    Write-Host "`n[2/6] Building bpm.exe from bpm.py..." -ForegroundColor Yellow

    # Check if Python is available
    $pythonCmd = $null
    if (Get-Command py -ErrorAction SilentlyContinue) {
        $pythonCmd = "py"
    } elseif (Get-Command python -ErrorAction SilentlyContinue) {
        $pythonCmd = "python"
    } else {
        throw "Python not found. Please install Python to build bpm.exe"
    }

    Write-Host "  Using Python: $pythonCmd"

    # Create a temporary directory for build
    $TempBuildDir = Join-Path $ProjectRoot "bpm_build_temp"
    if (Test-Path $TempBuildDir) {
        Remove-Item $TempBuildDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $TempBuildDir | Out-Null

    # Create virtual environment for clean build
    $VenvDir = Join-Path $TempBuildDir "venv"
    Write-Host "  Creating isolated virtual environment..."
    & $pythonCmd -m venv $VenvDir
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create virtual environment"
    }

    # Get paths for venv Python and pip
    $VenvPython = Join-Path $VenvDir "Scripts\python.exe"
    $VenvPip = Join-Path $VenvDir "Scripts\pip.exe"

    # Install only required dependencies from requirements-bpm.txt
    $RequirementsFile = Join-Path $ProjectRoot "requirements-bpm.txt"
    Write-Host "  Installing dependencies from requirements-bpm.txt..."
    & $VenvPip install -r $RequirementsFile --quiet
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install requirements"
    }

    # Install PyInstaller in the venv
    Write-Host "  Installing PyInstaller in venv..."
    & $VenvPip install pyinstaller --quiet
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install PyInstaller"
    }

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
        
        # Add the script file
        $pyInstallerArgs += $BpmScript
        
        # Run PyInstaller from the venv to create one-file executable
        & $VenvPython -m PyInstaller $pyInstallerArgs
        
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

# Step 3: Download ffmpeg binaries (if not skipped)
$FfmpegDir = Join-Path $ProjectRoot "ffmpeg_temp"
if (-not $SkipFfmpeg) {
    Write-Host "`n[3/6] Downloading ffmpeg binaries..." -ForegroundColor Yellow
    
    # Create temp directory for ffmpeg
    if (Test-Path $FfmpegDir) {
        Remove-Item $FfmpegDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $FfmpegDir | Out-Null
    
    # Download ffmpeg essentials from gyan.dev (stable, well-maintained builds)
    $FfmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
    $FfmpegZip = Join-Path $FfmpegDir "ffmpeg.zip"
    
    Write-Host "  Downloading from $FfmpegUrl..."
    try {
        # Use TLS 1.2 for HTTPS
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $FfmpegUrl -OutFile $FfmpegZip -UseBasicParsing
    }
    catch {
        throw "Failed to download ffmpeg: $_"
    }
    
    Write-Host "  Extracting ffmpeg..."
    Expand-Archive -Path $FfmpegZip -DestinationPath $FfmpegDir -Force
    
    # Find the bin folder (it's inside a versioned subfolder)
    $FfmpegBinDir = Get-ChildItem -Path $FfmpegDir -Directory | 
        Where-Object { $_.Name -like "ffmpeg-*" } | 
        Select-Object -First 1 | 
        ForEach-Object { Join-Path $_.FullName "bin" }
    
    if (-not $FfmpegBinDir -or -not (Test-Path $FfmpegBinDir)) {
        throw "Could not find ffmpeg bin directory"
    }
    
    Write-Host "  ffmpeg downloaded successfully." -ForegroundColor Green
} else {
    Write-Host "`n[3/6] Skipping ffmpeg download (--SkipFfmpeg specified)" -ForegroundColor Yellow
}

# Step 4: Publish C# project (self-contained)
Write-Host "`n[4/6] Publishing OsuMappingHelper (C#)..." -ForegroundColor Yellow
dotnet publish $CSharpProject -c $Configuration -r win-x64 --self-contained true
if ($LASTEXITCODE -ne 0) {
    throw "C# publish failed with exit code $LASTEXITCODE"
}
Write-Host "C# publish completed successfully." -ForegroundColor Green

# Step 5: Copy tools to output directory
Write-Host "`n[5/6] Copying tools to output directory..." -ForegroundColor Yellow

# Determine output directory (publish outputs to win-x64/publish subfolder)
$OutputDir = Join-Path $ProjectRoot "OsuMappingHelper\bin\$Configuration\net8.0-windows\win-x64\publish"

# Create tools subdirectory
$ToolsDir = Join-Path $OutputDir "tools"
if (-not (Test-Path $ToolsDir)) {
    New-Item -ItemType Directory -Path $ToolsDir | Out-Null
}

# Copy bpm.exe (only if bpm build was run)
if (-not $SkipBpm) {
    Write-Host "  Copying bpm.exe..."
    $BpmExeSource = Join-Path $TempBuildDir "dist\bpm.exe"
    if (Test-Path $BpmExeSource) {
        Copy-Item $BpmExeSource -Destination $ToolsDir -Force
        Write-Host "  bpm.exe copied successfully." -ForegroundColor Green
    } else {
        throw "bpm.exe not found at $BpmExeSource"
    }
} else {
    Write-Host "  Skipping bpm.exe copy (--SkipBpm specified)" -ForegroundColor Yellow
}

# Copy dans.json (to output directory, next to exe)
Write-Host "  Copying dans.json..."
Copy-Item $DansConfig -Destination $OutputDir -Force

# Copy msd-calculator-515.exe (new MinaCalc 5.15)
$MsdCalc515Exe = if ($Configuration -eq "Release") {
    Join-Path $RustProject515 "target\release\msd-calculator-515.exe"
} else {
    Join-Path $RustProject515 "target\debug\msd-calculator-515.exe"
}

if (Test-Path $MsdCalc515Exe) {
    Write-Host "  Copying msd-calculator-515.exe..."
    Copy-Item $MsdCalc515Exe -Destination $ToolsDir -Force
} else {
    Write-Host "  WARNING: msd-calculator-515.exe not found at $MsdCalc515Exe" -ForegroundColor Red
    Write-Host "  Run build without -SkipRust to build it first." -ForegroundColor Red
}

# Copy msd-calculator-505.exe (legacy MinaCalc 5.05)
$MsdCalc505Exe = if ($Configuration -eq "Release") {
    Join-Path $RustProject505 "target\release\msd-calculator-505.exe"
} else {
    Join-Path $RustProject505 "target\debug\msd-calculator-505.exe"
}

if (Test-Path $MsdCalc505Exe) {
    Write-Host "  Copying msd-calculator-505.exe..."
    Copy-Item $MsdCalc505Exe -Destination $ToolsDir -Force
} else {
    Write-Host "  WARNING: msd-calculator-505.exe not found at $MsdCalc505Exe" -ForegroundColor Red
    Write-Host "  Run build without -SkipRust to build it first." -ForegroundColor Red
}

# Copy ffmpeg binaries (to output directory root, next to exe)
if (-not $SkipFfmpeg) {
    Write-Host "  Copying ffmpeg binaries..."
    if (Test-Path $FfmpegBinDir) {
        Copy-Item (Join-Path $FfmpegBinDir "ffmpeg.exe") -Destination $OutputDir -Force
        Copy-Item (Join-Path $FfmpegBinDir "ffprobe.exe") -Destination $OutputDir -Force
        Write-Host "  ffmpeg binaries copied successfully." -ForegroundColor Green
    } else {
        Write-Host "  WARNING: ffmpeg binaries not found" -ForegroundColor Red
    }
} else {
    Write-Host "  Skipping ffmpeg copy (--SkipFfmpeg specified)" -ForegroundColor Yellow
}

# Clean up temporary build directory (only if bpm build was run)
if (-not $SkipBpm -and (Test-Path $TempBuildDir)) {
    Write-Host "  Cleaning up bpm build files..."
    Remove-Item $TempBuildDir -Recurse -Force -ErrorAction SilentlyContinue
}

# Clean up ffmpeg temp directory
if (-not $SkipFfmpeg -and (Test-Path $FfmpegDir)) {
    Write-Host "  Cleaning up ffmpeg download files..."
    Remove-Item $FfmpegDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`n[5/6] Build artifacts ready." -ForegroundColor Green
Write-Host "Output directory: $OutputDir"
Write-Host "Tools directory: $ToolsDir"

# List copied files
Write-Host "`nCopied files:"
Write-Host "  - dans.json (config)"
if (-not $SkipFfmpeg) {
    Write-Host "  - ffmpeg.exe"
    Write-Host "  - ffprobe.exe"
}
Get-ChildItem $ToolsDir | ForEach-Object { Write-Host "  - tools/$($_.Name)" }

# Step 6: Create Squirrel.Windows package (Release builds only)
if (-not $SkipSquirrel -and $Configuration -eq "Release") {
    Write-Host "`n[6/6] Creating Squirrel.Windows installer package..." -ForegroundColor Yellow
    
    # Read version from version.txt
    $VersionFile = Join-Path $ProjectRoot "OsuMappingHelper\version.txt"
    $VersionString = (Get-Content $VersionFile -Raw).Trim()
    # Remove 'v' prefix if present and convert to semantic version (e.g., v5.54 -> 5.54.0)
    $Version = $VersionString -replace '^v', ''
    if ($Version -notmatch '^\d+\.\d+\.\d+') {
        # Add .0 patch version if not present
        if ($Version -match '^\d+\.\d+$') {
            $Version = "$Version.0"
        } elseif ($Version -match '^\d+$') {
            $Version = "$Version.0.0"
        }
    }
    
    Write-Host "  Version: $Version"
    
    # Create Releases directory (preserve existing files for delta generation)
    $ReleasesDir = Join-Path $ProjectRoot "Releases"
    if (-not (Test-Path $ReleasesDir)) {
        New-Item -ItemType Directory -Path $ReleasesDir | Out-Null
    } else {
        # List existing files that will be used for delta generation
        $existingFiles = Get-ChildItem $ReleasesDir -ErrorAction SilentlyContinue
        if ($existingFiles) {
            Write-Host "  Found existing release files (for delta generation):"
            $existingFiles | ForEach-Object { Write-Host "    - $($_.Name)" }
        }
    }
    
    # Find Squirrel tools (installed via NuGet)
    $SquirrelExe = $null
    $NuGetPackages = Join-Path $env:USERPROFILE ".nuget\packages"
    $SquirrelPath = Join-Path $NuGetPackages "clowd.squirrel"
    
    if (Test-Path $SquirrelPath) {
        # Find latest version
        $LatestVersion = Get-ChildItem $SquirrelPath | Sort-Object Name -Descending | Select-Object -First 1
        if ($LatestVersion) {
            $SquirrelExe = Join-Path $LatestVersion.FullName "tools\Squirrel.exe"
        }
    }
    
    if (-not $SquirrelExe -or -not (Test-Path $SquirrelExe)) {
        Write-Host "  WARNING: Squirrel.exe not found. Run 'dotnet restore' first." -ForegroundColor Red
        Write-Host "  Skipping Squirrel package creation." -ForegroundColor Red
    } else {
        Write-Host "  Using Squirrel: $SquirrelExe"
        
        # Create NuSpec file for Squirrel
        $NuSpecPath = Join-Path $OutputDir "Companella.nuspec"
        $NuSpecContent = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
  <metadata>
    <id>Companella</id>
    <version>$Version</version>
    <title>Companella!</title>
    <authors>Leyna</authors>
    <description>osu!mania mapping helper and training companion</description>
    <copyright>Copyright 2026 Leyna</copyright>
  </metadata>
  <files>
    <file src="**\*.*" target="lib\net8.0-windows\" />
  </files>
</package>
"@
        Set-Content -Path $NuSpecPath -Value $NuSpecContent
        
        Write-Host "  Creating NuGet package..."
        
        # Use nuget.exe to create the package (or dotnet pack alternative)
        Push-Location $OutputDir
        try {
            # Pack using Squirrel's pack command (explicitly specify main exe due to ! in name)
            & $SquirrelExe pack --packId "Companella" --packVersion $Version --packDir "." --releaseDir $ReleasesDir --mainExe "Companella!.exe"
            
            if ($LASTEXITCODE -ne 0) {
                Write-Host "  WARNING: Squirrel pack failed with exit code $LASTEXITCODE" -ForegroundColor Red
            } else {
                Write-Host "  Squirrel package created successfully." -ForegroundColor Green
                
                # List created files
                Write-Host "`n  Release files:"
                Get-ChildItem $ReleasesDir | ForEach-Object { 
                    Write-Host "    - $($_.Name) ($([math]::Round($_.Length / 1MB, 2)) MB)"
                }
            }
        }
        finally {
            Pop-Location
            # Clean up nuspec
            if (Test-Path $NuSpecPath) {
                Remove-Item $NuSpecPath -Force -ErrorAction SilentlyContinue
            }
        }
    }
} elseif ($SkipSquirrel) {
    Write-Host "`n[6/6] Skipping Squirrel package (--SkipSquirrel specified)" -ForegroundColor Yellow
} else {
    Write-Host "`n[6/6] Skipping Squirrel package (Debug build)" -ForegroundColor Yellow
}

Write-Host "`n=== Build Complete ===" -ForegroundColor Green
