#Requires -Version 5.1
param(
    [string]$UsbRoot = ""
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($UsbRoot)) { $UsbRoot = $PSScriptRoot }

function ConvertTo-Plain {
    param([Security.SecureString]$Secure)
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

function Get-InstallPasswordHash {
    param([string]$Plain)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Plain)
        $hash = $sha.ComputeHash($bytes)
        return ([BitConverter]::ToString($hash) -replace "-", "").ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Show-ErrorDialog {
    param([string]$Message, [string]$WhatToDo)
    $text = $Message + [Environment]::NewLine + [Environment]::NewLine + $WhatToDo
    try {
        Add-Type -AssemblyName System.Windows.Forms | Out-Null
        [void][System.Windows.Forms.MessageBox]::Show($text, "CPCREDO - echec", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
    }
    catch {
        Write-Host $text -ForegroundColor Red
    }
}

function Confirm-InstallLock {
    param([string]$LockPath)
    if (-not (Test-Path $LockPath)) {
        Show-ErrorDialog "Fichier d'installation manquant : Templates\install.lock" "Utilisez la meme cle USB que le serveur."
        exit 1
    }
    $expected = (Get-Content -LiteralPath $LockPath -Raw).Trim().ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($expected) -or $expected.Length -lt 64) {
        Show-ErrorDialog "install.lock invalide." "Recreez la cle USB avec publish.ps1."
        exit 1
    }
    for ($i = 1; $i -le 5; $i++) {
        $secure = Read-Host "Mot de passe d'installation" -AsSecureString
        $plain = ConvertTo-Plain $secure
        $actual = Get-InstallPasswordHash $plain
        $plain = $null
        if ($actual -eq $expected) { return }
        Write-Host "Mot de passe incorrect ($i/5)." -ForegroundColor Yellow
    }
    Show-ErrorDialog "Trop d'essais. Installation annulee." "Verifiez le mot de passe d'installation fourni avec la cle USB."
    exit 1
}

function Open-CpcredoUrl {
    param([string]$Url)
    if ([string]::IsNullOrWhiteSpace($Url)) { return }
    $target = $Url.Trim()
    if ($target -notmatch "^https://") {
        $target = "https://" + ($target -replace "^https?://", "")
    }
    Write-Host "Ouverture : $target"

    $rundll = Join-Path $env:SystemRoot "System32\rundll32.exe"
    try {
        Start-Process -FilePath $rundll -ArgumentList @("url.dll,FileProtocolHandler", $target) | Out-Null
        return
    }
    catch { }

    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $target
        $psi.UseShellExecute = $true
        [void][System.Diagnostics.Process]::Start($psi)
        return
    }
    catch { }

    try {
        $cmd = Join-Path $env:SystemRoot "System32\cmd.exe"
        Start-Process -FilePath $cmd -ArgumentList @("/c", "start", "", $target) | Out-Null
    }
    catch { }
}

function New-DesktopUrlShortcut {
    param([string]$TargetUrl)
    $desktops = @(
        [Environment]::GetFolderPath("Desktop")
        [Environment]::GetFolderPath("CommonDesktopDirectory")
    )
    foreach ($desktop in $desktops) {
        if ([string]::IsNullOrWhiteSpace($desktop) -or -not (Test-Path $desktop)) { continue }
        $urlFile = Join-Path $desktop "CPCREDO.url"
        try {
            Set-Content -LiteralPath $urlFile -Value ("[InternetShortcut]`r`nURL=$TargetUrl`r`n") -Encoding ASCII
            Write-Host "Raccourci : $urlFile -> $TargetUrl"
        }
        catch { }
        $lnk = Join-Path $desktop "CPCREDO.lnk"
        if (Test-Path $lnk) {
            try { Remove-Item -LiteralPath $lnk -Force } catch { }
        }
    }
}

function Test-ServerUrl {
    param([string]$Uri)
    try {
        [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
        $r = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 5
        return ($r.StatusCode -eq 200)
    }
    catch { return $false }
}

Confirm-InstallLock (Join-Path $UsbRoot "Templates\install.lock")

$serverIp = Read-Host "Adresse IP du serveur CPCREDO (ex. 192.168.1.10)"
$serverIp = $serverIp.Trim()
if ($serverIp -match "^https?://") { $serverIp = $serverIp -replace "^https?://", "" }
$serverIp = $serverIp.TrimEnd("/")
if ($serverIp -match ":\d+$") { $serverIp = $serverIp -replace ":\d+$", "" }
if ([string]::IsNullOrWhiteSpace($serverIp)) {
    Show-ErrorDialog "Adresse IP obligatoire." "Relancez INSTALLER-CLIENT.bat et saisissez l'IP du serveur (sans http, sans port)."
    exit 1
}
$url = "https://${serverIp}:5443"
$health = "https://${serverIp}:5443/health"

Add-Type -AssemblyName System.Windows.Forms | Out-Null

if (-not (Test-ServerUrl $health)) {
    $msg = "Le serveur n'a pas repondu a $health.`r`n`r`nVerifiez :`r`n- l'adresse IP`r`n- que INSTALLER-SERVEUR.bat a bien termine`r`n- le pare-feu du serveur (port 5443)`r`n`r`nLe raccourci Bureau sera tout de meme cree."
    [void][System.Windows.Forms.MessageBox]::Show(
        $msg,
        "CPCREDO - serveur injoignable",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Warning)
}

New-DesktopUrlShortcut $url

$startup = Read-Host "Creer aussi un raccourci au demarrage de Windows ? (o/N)"
if ($startup -match "^[oOyY]") {
    $startupDir = [Environment]::GetFolderPath("Startup")
    $startupUrl = Join-Path $startupDir "CPCREDO.url"
    Set-Content -LiteralPath $startupUrl -Value ("[InternetShortcut]`r`nURL=$url`r`n") -Encoding ASCII
    Write-Host "Raccourci Demarrage cree."
}

Open-CpcredoUrl $url

[void][System.Windows.Forms.MessageBox]::Show(
    "Poste client pret.`r`n`r`nRaccourci Bureau : CPCREDO`r`nAdresse : $url`r`n`r`nLe navigateur s'ouvre sur cette adresse.`r`nLa premiere visite peut afficher un avertissement de certificat : Continuer vers le site.`r`n`r`nAucune copie de l'application, pas de PostgreSQL.",
    "CPCREDO",
    [System.Windows.Forms.MessageBoxButtons]::OK,
    [System.Windows.Forms.MessageBoxIcon]::Information)
