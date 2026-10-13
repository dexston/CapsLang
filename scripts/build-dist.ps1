<#
.SYNOPSIS
    Builds and packages CapsLang distributions (Portable ZIP and Installer Setup).

.DESCRIPTION
    Automates the compilation, signing, packaging, and checksum generation for
    CapsLang releases according to the project specifications.
    Follows the strict Fail-Fast principle: terminates immediately with an error code
    if any required tool or compilation step fails.

.PARAMETER Target
    Target distribution to build: 'Portable', 'Installer', or 'All'. Defaults to 'All'.

.PARAMETER Version
    Version string for the build (e.g. '1.0.0'). If omitted, read from CapsLang.csproj.

.PARAMETER CertPfxPath
    Path to the PKCS#12 (.pfx) certificate container. Defaults to resources\cert\CapsLang.pfx or $env:CERT_PFX_PATH.

.PARAMETER CertPassword
    Password for the certificate container. Defaults to $env:CERTIFICATE_PASSWORD or $env:CERT_PASSWORD.

.PARAMETER SkipSigning
    Skips code signing for local test builds without a certificate or SignTool.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet("Portable", "Installer", "All")]
    [string]$Target = "All",

    [Parameter(Mandatory = $false)]
    [string]$Version = "",

    [Parameter(Mandatory = $false)]
    [string]$CertPfxPath = "",

    [Parameter(Mandatory = $false)]
    [string]$CertPassword = "",

    [Parameter(Mandatory = $false)]
    [switch]$SkipSigning
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$csprojPath = Join-Path $projectRoot "CapsLang.csproj"
$distDir = Join-Path $projectRoot "build\dist"

# -------------------------------------------------------------
# 1. Resolve Version
# -------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($Version)) {
    if (Test-Path $csprojPath) {
        $projContent = [xml](Get-Content $csprojPath)
        $versionNode = $projContent.SelectSingleNode("//Version")
        if ($null -ne $versionNode -and -not [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
            $Version = $versionNode.InnerText.Trim()
        }
    }
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = "1.0.0"
    }
}
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    Write-Error "FAIL-FAST: Invalid version format '$Version'! Expected SemVer format without 'v' prefix (e.g. '1.0.0')."
    exit 1
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  CapsLang Distribution Builder" -ForegroundColor Cyan
Write-Host "  Target:       $Target" -ForegroundColor Cyan
Write-Host "  Version:      $Version" -ForegroundColor Cyan
Write-Host "  Project Root: $projectRoot" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
}

# -------------------------------------------------------------
# 2. Tool Discovery Helpers
# -------------------------------------------------------------
function Find-Iscc {
    $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\iscc.exe",
        "${env:ProgramFiles}\Inno Setup 6\iscc.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 7\iscc.exe",
        "${env:ProgramFiles}\Inno Setup 7\iscc.exe",
        "${env:LocalAppData}\Programs\Inno Setup 6\iscc.exe",
        "${env:LocalAppData}\Programs\Inno Setup 7\iscc.exe"
    )
    foreach ($path in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path $path)) {
            return $path
        }
    }
    return $null
}

function Find-Signtool {
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $roots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "${env:ProgramFiles}\Windows Kits\10\bin",
        "${env:ProgramFiles(x86)}\Microsoft SDKs\Windows"
    )
    foreach ($root in $roots) {
        if (-not [string]::IsNullOrWhiteSpace($root) -and (Test-Path $root)) {
            $found = Get-ChildItem -Path $root -Filter "signtool.exe" -Recurse -File -ErrorAction SilentlyContinue |
                Sort-Object { $_.FullName -like "*x64*" } -Descending |
                Select-Object -First 1
            if ($found) { return $found.FullName }
        }
    }
    return $null
}

function Sign-BinaryWithRetry {
    param(
        [string]$FilePath,
        [string]$SigntoolPath,
        [string]$PfxPath,
        [string]$Password
    )

    $timestampUrl = "http://timestamp.digicert.com"

    if (-not [string]::IsNullOrWhiteSpace($SigntoolPath)) {
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            Write-Host "Signing $FilePath with SignTool (attempt $attempt/3)..." -ForegroundColor Cyan
            $proc = Start-Process -FilePath $SigntoolPath -ArgumentList @(
                "sign", "/f", $PfxPath, "/p", $Password, "/fd", "sha256",
                "/d", "CapsLang",
                "/tr", $timestampUrl, "/td", "sha256", $FilePath
            ) -NoNewWindow -PassThru -Wait

            if ($proc.ExitCode -eq 0) {
                Write-Host "Successfully signed: $FilePath" -ForegroundColor Green
                return
            }
            Write-Warning "SignTool attempt $attempt failed with code $($proc.ExitCode). Retrying in 3 seconds..."
            Start-Sleep -Seconds 3
        }
    } else {
        $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
            $PfxPath,
            $Password,
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable
        )
        try {
            for ($attempt = 1; $attempt -le 3; $attempt++) {
                Write-Host "Signing $FilePath with PowerShell Authenticode (attempt $attempt/3)..." -ForegroundColor Cyan
                $null = Set-AuthenticodeSignature -FilePath $FilePath -Certificate $cert -TimestampServer $timestampUrl -HashAlgorithm SHA256 -ErrorAction SilentlyContinue

                $sig = Get-AuthenticodeSignature -FilePath $FilePath
                if ($null -ne $sig -and $null -ne $sig.SignerCertificate -and $sig.SignerCertificate.Thumbprint -eq $cert.Thumbprint) {
                    Write-Host "Successfully signed: $FilePath (PowerShell Authenticode)" -ForegroundColor Green
                    return
                }
                Write-Warning "PowerShell Authenticode attempt $attempt failed. Retrying in 3 seconds..."
                Start-Sleep -Seconds 3
            }
        } finally {
            $cert.Dispose()
        }
    }

    Write-Error "FAIL-FAST: Failed to sign $FilePath after 3 attempts!"
    exit 1
}

# -------------------------------------------------------------
# 3. Fail-Fast Validation for Installer Requirements
# -------------------------------------------------------------
$needsInstaller = ($Target -in @("Installer", "All"))
$isccPath = $null
$signtoolPath = $null
$resolvedPfxPath = $null

if ($needsInstaller) {
    $isccPath = Find-Iscc
    if (-not $isccPath) {
        Write-Error "FAIL-FAST: Inno Setup compiler (iscc.exe) was not found in PATH or standard Program Files locations! Cannot build Installer."
        exit 1
    }
    Write-Host "Found Inno Setup compiler: $isccPath" -ForegroundColor Green

    if (-not $SkipSigning) {
        $signtoolPath = Find-Signtool
        if ($signtoolPath) {
            Write-Host "Found SignTool: $signtoolPath" -ForegroundColor Green
        } else {
            Write-Host "SignTool (signtool.exe) not found; will use built-in PowerShell Authenticode engine." -ForegroundColor Yellow
        }

        # Resolve PFX path
        if ([string]::IsNullOrWhiteSpace($CertPfxPath)) {
            if (-not [string]::IsNullOrWhiteSpace($env:CERT_PFX_PATH)) {
                $CertPfxPath = $env:CERT_PFX_PATH
            } else {
                $CertPfxPath = Join-Path $projectRoot "resources\cert\CapsLang.pfx"
            }
        }
        if (-not (Test-Path $CertPfxPath)) {
            Write-Error "FAIL-FAST: Certificate PFX container not found at '$CertPfxPath'! To build an unsigned installer, pass -SkipSigning."
            exit 1
        }
        $resolvedPfxPath = (Resolve-Path $CertPfxPath).Path
        Write-Host "Found Certificate: $resolvedPfxPath" -ForegroundColor Green

        # Resolve Password
        if ([string]::IsNullOrWhiteSpace($CertPassword)) {
            if (-not [string]::IsNullOrWhiteSpace($env:CERTIFICATE_PASSWORD)) {
                $CertPassword = $env:CERTIFICATE_PASSWORD
            } elseif (-not [string]::IsNullOrWhiteSpace($env:CERT_PASSWORD)) {
                $CertPassword = $env:CERT_PASSWORD
            } elseif ($env:CI -eq "true") {
                Write-Error "FAIL-FAST: Certificate password was not provided in non-interactive CI environment!"
                exit 1
            } else {
                $sec = Read-Host -Prompt "Enter Certificate Password" -AsSecureString
                $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
                try {
                    $CertPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
                } finally {
                    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
                }
            }
        }
        if ([string]::IsNullOrWhiteSpace($CertPassword)) {
            Write-Error "FAIL-FAST: Certificate password cannot be empty!"
            exit 1
        }
    }
}

$artifactsCreated = @()

# -------------------------------------------------------------
# 4. Build Portable Distribution
# -------------------------------------------------------------
if ($Target -in @("Portable", "All")) {
    Write-Host ""
    Write-Host "----------------------------------------------------------" -ForegroundColor Yellow
    Write-Host "Building Portable Distribution (uiAccess=false)..." -ForegroundColor Yellow
    Write-Host "----------------------------------------------------------" -ForegroundColor Yellow

    $portableBinDir = Join-Path $projectRoot "build\bin\Release\Portable"
    $portableExe = Join-Path $portableBinDir "CapsLang.exe"

    dotnet build -c Release -p:IsPortable=true -p:Version=$Version $csprojPath
    if ($LASTEXITCODE -ne 0) {
        Write-Error "FAIL-FAST: dotnet build for Portable failed with code $LASTEXITCODE!"
        exit $LASTEXITCODE
    }

    if (-not (Test-Path $portableExe)) {
        Write-Error "FAIL-FAST: Expected executable was not produced at $portableExe!"
        exit 1
    }

    $zipName = "CapsLang-$Version-Portable.zip"
    $zipPath = Join-Path $distDir $zipName

    if (Test-Path $zipPath) {
        Remove-Item -Path $zipPath -Force
    }

    $stageDir = Join-Path $projectRoot "build\stage_portable"
    if (Test-Path $stageDir) {
        Remove-Item -Path $stageDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

    Copy-Item -Path $portableExe -Destination (Join-Path $stageDir "CapsLang.exe")
    Copy-Item -Path (Join-Path $projectRoot "README.md") -Destination (Join-Path $stageDir "README.md")
    Copy-Item -Path (Join-Path $projectRoot "LICENSE") -Destination (Join-Path $stageDir "LICENSE")

    Compress-Archive -Path "$stageDir\*" -DestinationPath $zipPath -Force
    Remove-Item -Path $stageDir -Recurse -Force

    $zipHash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$zipHash  $zipName" | Out-File -FilePath "$zipPath.sha256" -Encoding ascii

    Write-Host "Portable package created: $zipPath" -ForegroundColor Green
    Write-Host "SHA256: $zipHash" -ForegroundColor DarkGray
    $artifactsCreated += [PSCustomObject]@{
        Name   = $zipName
        Path   = $zipPath
        SHA256 = $zipHash
    }
}

# -------------------------------------------------------------
# 5. Build Installer Distribution
# -------------------------------------------------------------
if ($Target -in @("Installer", "All")) {
    Write-Host ""
    Write-Host "----------------------------------------------------------" -ForegroundColor Yellow
    Write-Host "Building Installer Distribution (uiAccess=true)..." -ForegroundColor Yellow
    Write-Host "----------------------------------------------------------" -ForegroundColor Yellow

    $installerBinDir = Join-Path $projectRoot "build\bin\Release\Installer"
    $installerExe = Join-Path $installerBinDir "CapsLang.exe"

    dotnet build -c Release -p:IsPortable=false -p:Version=$Version $csprojPath
    if ($LASTEXITCODE -ne 0) {
        Write-Error "FAIL-FAST: dotnet build for Installer payload failed with code $LASTEXITCODE!"
        exit $LASTEXITCODE
    }

    if (-not (Test-Path $installerExe)) {
        Write-Error "FAIL-FAST: Expected executable was not produced at $installerExe!"
        exit 1
    }

    # Sign binary
    if (-not $SkipSigning) {
        Sign-BinaryWithRetry -FilePath $installerExe -SigntoolPath $signtoolPath -PfxPath $resolvedPfxPath -Password $CertPassword
    } else {
        Write-Warning "Skipping binary signature (-SkipSigning specified)."
    }

    # Compile Inno Setup Installer
    Write-Host "Compiling Inno Setup installer..." -ForegroundColor Cyan
    $issPath = Join-Path $projectRoot "installer\CapsLang.iss"
    
    $isccArgs = @(
        "/DAppVersion=$Version",
        $issPath
    )

    & $isccPath $isccArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "FAIL-FAST: Inno Setup compilation failed with code $LASTEXITCODE!"
        exit $LASTEXITCODE
    }

    $setupName = "CapsLang-$Version-Setup.exe"
    $setupPath = Join-Path $distDir $setupName

    if (-not (Test-Path $setupPath)) {
        Write-Error "FAIL-FAST: Expected installer was not produced at $setupPath!"
        exit 1
    }

    # Sign installer
    if (-not $SkipSigning) {
        Sign-BinaryWithRetry -FilePath $setupPath -SigntoolPath $signtoolPath -PfxPath $resolvedPfxPath -Password $CertPassword
    } else {
        Write-Warning "Skipping installer signature (-SkipSigning specified)."
    }

    $setupHash = (Get-FileHash -Path $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$setupHash  $setupName" | Out-File -FilePath "$setupPath.sha256" -Encoding ascii

    Write-Host "Installer created: $setupPath" -ForegroundColor Green
    Write-Host "SHA256: $setupHash" -ForegroundColor DarkGray
    $artifactsCreated += [PSCustomObject]@{
        Name   = $setupName
        Path   = $setupPath
        SHA256 = $setupHash
    }
}

# -------------------------------------------------------------
# 6. Summary Report
# -------------------------------------------------------------
Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "  Build Completed Successfully!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
foreach ($art in $artifactsCreated) {
    Write-Host "  Artifact: $($art.Name)" -ForegroundColor White
    Write-Host "  SHA256:   $($art.SHA256)" -ForegroundColor DarkGray
}
Write-Host ""
