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

Confirm-InstallLock (Join-Path $UsbRoot "Templates\install.lock")

$serverIp = Read-Host "Adresse IP du serveur CPCREDO (ex. 192.168.1.10)"
$serverIp = $serverIp.Trim()
if ($serverIp -match "^https?://") { $serverIp = $serverIp -replace "^https?://", "" }
$serverIp = $serverIp.TrimEnd("/")
if ($serverIp -match ":5080$") { $serverIp = $serverIp -replace ":5080$", "" }
if ([string]::IsNullOrWhiteSpace($serverIp)) {
    Show-ErrorDialog "Adresse IP obligatoire." "Relancez INSTALLER-CLIENT.bat et saisissez l'IP du serveur (sans http, sans port)."
    exit 1
}
$url = "http://${serverIp}:5080"

function New-UrlShortcut {
    param([string]$Path, [string]$TargetUrl)
    Set-Content -LiteralPath $Path -Value "[InternetShortcut]`r`nURL=$TargetUrl`r`n" -Encoding ASCII
}

$desktop = [Environment]::GetFolderPath("Desktop")
$shortcutPath = Join-Path $desktop "CPCREDO.url"
New-UrlShortcut $shortcutPath $url
Write-Host "Raccourci Bureau : $shortcutPath -> $url"

$startup = Read-Host "Creer aussi un raccourci au demarrage de Windows ? (o/N)"
if ($startup -match "^[oOyY]") {
    $startupDir = [Environment]::GetFolderPath("Startup")
    New-UrlShortcut (Join-Path $startupDir "CPCREDO.url") $url
    Write-Host "Raccourci Demarrage cree."
}

Add-Type -AssemblyName System.Windows.Forms | Out-Null
[void][System.Windows.Forms.MessageBox]::Show(
    "Poste client pret.`r`n`r`nRaccourci Bureau : CPCREDO`r`nAdresse : $url`r`n`r`nAucune copie de l'application, pas de PostgreSQL.",
    "CPCREDO",
    [System.Windows.Forms.MessageBoxButtons]::OK,
    [System.Windows.Forms.MessageBoxIcon]::Information)
