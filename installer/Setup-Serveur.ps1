#Requires -Version 5.1
param(
    [string]$UsbRoot = ""
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($UsbRoot)) { $UsbRoot = $PSScriptRoot }

$script:LogPath = $null
$script:StartedPid = $null

function Write-InstallLog {
    param([string]$Step, [string]$Status, [string]$Detail)
    $line = "{0:yyyy-MM-dd HH:mm:ss}  {1}  {2}  {3}" -f (Get-Date), $Step, $Status, $Detail
    if ($script:LogPath) {
        Add-Content -LiteralPath $script:LogPath -Value $line -Encoding UTF8
    }
    Write-Host $line
}

function Initialize-Log {
    param([string]$Path)
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    if (-not (Test-Path $Path)) {
        Set-Content -LiteralPath $Path -Value "CPCREDO install.log" -Encoding UTF8
    }
    $script:LogPath = $Path
}

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

function New-SecretBytes {
    param([int]$Count)
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $bytes = New-Object byte[] $Count
        $rng.GetBytes($bytes)
        Write-Output -NoEnumerate $bytes
    }
    finally { $rng.Dispose() }
}

function New-OneTimePassword {
    $alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@$%#"
    $bytes = New-SecretBytes 16
    $chars = New-Object char[] 16
    for ($i = 0; $i -lt 16; $i++) {
        $chars[$i] = $alphabet[$bytes[$i] % $alphabet.Length]
    }
    return (-join $chars)
}

function New-AdminOneTimePassword {
    $alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"
    $bytes = New-SecretBytes 8
    $chars = New-Object char[] 8
    for ($i = 0; $i -lt 8; $i++) {
        $chars[$i] = $alphabet[$bytes[$i] % $alphabet.Length]
    }
    return (-join $chars)
}

function Format-AdminPasswordDisplay {
    param([string]$Raw)
    if ($Raw.Length -eq 8) {
        return $Raw.Substring(0, 4) + "-" + $Raw.Substring(4, 4)
    }
    return $Raw
}

function Escape-JsonString {
    param([string]$Value)
    if ($null -eq $Value) { return "" }
    $s = $Value.Replace("\", "\\").Replace('"', '\"').Replace("`r", "\r").Replace("`n", "\n")
    return $s
}

function Show-ErrorDialog {
    param([string]$Message, [string]$WhatToDo)
    $logHint = "Journal : "
    if ($script:LogPath) { $logHint = $logHint + $script:LogPath }
    else { $logHint = $logHint + "(pas encore cree)" }
    $text = $Message + [Environment]::NewLine + [Environment]::NewLine + $WhatToDo + [Environment]::NewLine + [Environment]::NewLine + $logHint
    try {
        [void][System.Windows.Forms.MessageBox]::Show($text, "CPCREDO - echec", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
    }
    catch {
        Write-Host $text -ForegroundColor Red
    }
}

function Fail-Step {
    param([string]$Step, [string]$Message, [string]$WhatToDo)
    Write-InstallLog $Step "FAIL" $Message
    if ($script:StartedPid) {
        try { Stop-Process -Id $script:StartedPid -Force -ErrorAction SilentlyContinue } catch { }
    }
    Show-ErrorDialog $Message $WhatToDo
    exit 1
}

function Confirm-InstallLock {
    param([string]$LockPath)
    if (-not (Test-Path $LockPath)) {
        Fail-Step "PASSWORD" "Fichier d'installation manquant : Templates\install.lock" "Recreez la cle USB avec publish.ps1 sur le PC developpeur."
    }
    $expected = (Get-Content -LiteralPath $LockPath -Raw).Trim().ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($expected) -or $expected.Length -lt 64) {
        Fail-Step "PASSWORD" "install.lock invalide." "Recreez la cle USB avec publish.ps1."
    }
    for ($i = 1; $i -le 5; $i++) {
        $secure = Read-Host "Mot de passe d'installation" -AsSecureString
        $plain = ConvertTo-Plain $secure
        $actual = Get-InstallPasswordHash $plain
        $plain = $null
        if ($actual -eq $expected) {
            Write-InstallLog "PASSWORD" "OK" "mot de passe accepte"
            return
        }
        Write-Host "Mot de passe incorrect ($i/5)." -ForegroundColor Yellow
        Write-InstallLog "PASSWORD" "FAIL" ("essai " + $i + "/5")
    }
    Fail-Step "PASSWORD" "Trop d'essais. Installation annulee." "Verifiez le mot de passe d'installation fourni avec la cle USB, puis relancez INSTALLER-SERVEUR.bat."
}

function Get-PsqlPath {
    $candidates = @(
        (Join-Path $env:ProgramFiles "PostgreSQL\16\bin\psql.exe"),
        (Join-Path $env:ProgramFiles "PostgreSQL\17\bin\psql.exe"),
        (Join-Path $env:ProgramFiles "PostgreSQL\15\bin\psql.exe")
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { return $c }
    }
    return $null
}

function Get-PostgresServices {
    return @(Get-Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "postgresql*" })
}

function Test-PostgresRunning {
    $svcs = Get-PostgresServices
    foreach ($s in $svcs) {
        if ($s.Status -eq "Running") { return $true }
    }
    return $false
}

function Install-WingetPackage {
    param([string]$Id)
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) { return $false }
    Write-Host "Installation (si besoin) : $Id"
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & winget install --id $Id -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
    $code = $LASTEXITCODE
    $ErrorActionPreference = $prev
    if ($code -eq 0 -or $code -eq -1978335189) { return $true }
    return $false
}

function Invoke-Psql {
    param([string]$Psql, [string]$Database, [string]$Sql)
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $output = & $Psql -h localhost -U postgres -d $Database -v ON_ERROR_STOP=1 -tAc $Sql 2>&1
    $code = $LASTEXITCODE
    $ErrorActionPreference = $prev
    if ($code -ne 0) {
        throw ("psql exit " + $code + " : " + ($output | Out-String))
    }
    return ($output | Out-String).Trim()
}

function Get-LanIPv4 {
    try {
        $addrs = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop |
            Where-Object { $_.IPAddress -notlike "127.*" -and $_.PrefixOrigin -ne "WellKnown" } |
            Select-Object -ExpandProperty IPAddress)
        if ($addrs.Count -gt 0) { return $addrs }
    }
    catch { }
    return @()
}

function Show-LargePasswordBox {
    param([string]$Password, [string]$Url)
    $display = Format-AdminPasswordDisplay $Password
    $form = New-Object System.Windows.Forms.Form
    $form.Text = "CPCREDO - mot de passe administrateur (affichage unique)"
    $form.Width = 760
    $form.Height = 440
    $form.StartPosition = "CenterScreen"
    $form.TopMost = $true
    $form.FormBorderStyle = "FixedDialog"
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $panel = New-Object System.Windows.Forms.Panel
    $panel.Dock = "Fill"
    $panel.Padding = New-Object System.Windows.Forms.Padding(28)

    $intro = New-Object System.Windows.Forms.Label
    $intro.AutoSize = $false
    $intro.Width = 680
    $intro.Height = 70
    $intro.Top = 24
    $intro.Left = 28
    $intro.Font = New-Object System.Drawing.Font("Segoe UI", 14, [System.Drawing.FontStyle]::Regular)
    $intro.Text = "Utilisateur : admin" + [Environment]::NewLine + "Mot de passe (une seule fois) :"

    $code = New-Object System.Windows.Forms.Label
    $code.AutoSize = $false
    $code.Width = 680
    $code.Height = 90
    $code.Top = 100
    $code.Left = 28
    $code.Font = New-Object System.Drawing.Font("Consolas", 36, [System.Drawing.FontStyle]::Bold)
    $code.Text = $display
    $code.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter

    $note = New-Object System.Windows.Forms.Label
    $note.AutoSize = $false
    $note.Width = 680
    $note.Height = 140
    $note.Top = 200
    $note.Left = 28
    $note.Font = New-Object System.Drawing.Font("Segoe UI", 14, [System.Drawing.FontStyle]::Regular)
    $note.Text = "Saisissez-le sans espace ni tiret." + [Environment]::NewLine +
        "Changez le mot de passe (10 caracteres min.) a la premiere connexion." + [Environment]::NewLine + [Environment]::NewLine +
        "Adresse : " + $Url

    $panel.Controls.Add($intro)
    $panel.Controls.Add($code)
    $panel.Controls.Add($note)
    $form.Controls.Add($panel)
    $form.Add_Shown({ $form.Activate() })
    [void]$form.ShowDialog()
}

Add-Type -AssemblyName System.Windows.Forms | Out-Null
Add-Type -AssemblyName System.Drawing | Out-Null

Initialize-Log (Join-Path $UsbRoot "install.log")

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-InstallLog "ADMIN" "FAIL" "pas administrateur"
    Show-ErrorDialog "Setup-Serveur doit etre lance en tant qu'administrateur." "Clic droit -> Executer en tant qu'administrateur. Relancez INSTALLER-SERVEUR.bat."
    exit 1
}
Write-InstallLog "ADMIN" "OK" "session administrateur"

$lockPath = Join-Path $UsbRoot "Templates\install.lock"
$appSource = Join-Path $UsbRoot "App"
if (-not (Test-Path $appSource)) {
    Fail-Step "APP" "Dossier App introuvable." "Recreez la cle USB avec publish.ps1."
}

Confirm-InstallLock $lockPath

if (-not (Test-Path "C:\CPCREDO")) {
    New-Item -ItemType Directory -Force -Path "C:\CPCREDO" | Out-Null
}
$dialog = New-Object System.Windows.Forms.FolderBrowserDialog
$dialog.Description = "Dossier d'installation CPCREDO"
$dialog.SelectedPath = "C:\CPCREDO"
$dialog.ShowNewFolderButton = $true
if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
    Fail-Step "FOLDER" "Installation annulee." "Relancez INSTALLER-SERVEUR.bat et choisissez un dossier (defaut : C:\CPCREDO)."
}
$dest = $dialog.SelectedPath
if ([string]::IsNullOrWhiteSpace($dest)) { $dest = "C:\CPCREDO" }
New-Item -ItemType Directory -Force -Path $dest | Out-Null

$destLog = Join-Path $dest "install.log"
if ($script:LogPath -ne $destLog -and (Test-Path $script:LogPath)) {
    Copy-Item -LiteralPath $script:LogPath -Destination $destLog -Force
}
Initialize-Log $destLog
Write-InstallLog "FOLDER" "OK" $dest

Write-Host "Copie des fichiers vers $dest ..."
try {
    $prevCopy = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    schtasks /End /TN "CPCREDO" | Out-Null
    Get-Process -Name "CPCREDO.WebApi" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    $ErrorActionPreference = $prevCopy

    $destWww = Join-Path $dest "wwwroot"
    if (Test-Path $destWww) {
        Remove-Item -LiteralPath $destWww -Recurse -Force
    }
    Copy-Item -Path (Join-Path $appSource "*") -Destination $dest -Recurse -Force
    $srcWww = Join-Path $appSource "wwwroot"
    if (Test-Path $srcWww) {
        if (Test-Path $destWww) { Remove-Item -LiteralPath $destWww -Recurse -Force }
        Copy-Item -Path $srcWww -Destination $dest -Recurse -Force
    }
    $devOnDisk = Join-Path $dest "appsettings.Development.json"
    if (Test-Path $devOnDisk) { Remove-Item -LiteralPath $devOnDisk -Force }
    Write-InstallLog "COPY" "OK" "wwwroot remplace (anciens fichiers JS/CSS supprimes)"
}
catch {
    Fail-Step "COPY" "Impossible de copier les fichiers vers $dest" "Fermez CPCREDO s'il est deja ouvert, puis relancez INSTALLER-SERVEUR.bat."
}

$aspnetDir = Join-Path $env:ProgramFiles "dotnet\shared\Microsoft.AspNetCore.App"
$hasAspNet8 = $false
if (Test-Path $aspnetDir) {
    $hasAspNet8 = @(Get-ChildItem $aspnetDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "8.*" }).Count -gt 0
}
if (-not $hasAspNet8) {
    Write-InstallLog "HOSTING" "INFO" "module ASP.NET 8 absent, tentative winget"
    [void](Install-WingetPackage "Microsoft.DotNet.HostingBundle.8")
}

if (Test-PostgresRunning) {
    Write-InstallLog "POSTGRES_SERVICE" "OK" "service deja en cours"
}
else {
    $existing = Get-PostgresServices
    if ($existing.Count -gt 0) {
        foreach ($s in $existing) {
            try { Start-Service -Name $s.Name -ErrorAction Stop } catch { }
        }
        Start-Sleep -Seconds 3
    }
    if (-not (Test-PostgresRunning)) {
        Write-InstallLog "POSTGRES_SERVICE" "INFO" "tentative winget PostgreSQL 16"
        $ok = Install-WingetPackage "PostgreSQL.PostgreSQL.16"
        $waited = $false
        if ($ok) {
            for ($n = 1; $n -le 30; $n++) {
                Start-Sleep -Seconds 2
                if (Test-PostgresRunning) { $waited = $true; break }
            }
        }
        if (-not (Test-PostgresRunning)) {
            Fail-Step "POSTGRES_SERVICE" "PostgreSQL n'est pas en cours d'execution." "Installez PostgreSQL 16 puis relancez INSTALLER-SERVEUR.bat."
        }
        if ($waited) { Write-InstallLog "POSTGRES_SERVICE" "OK" "installe et demarre" }
        else { Write-InstallLog "POSTGRES_SERVICE" "OK" "service demarre" }
    }
    else {
        Write-InstallLog "POSTGRES_SERVICE" "OK" "service demarre"
    }
}

$psql = Get-PsqlPath
if (-not $psql) {
    Fail-Step "POSTGRES_PSQL" "psql.exe introuvable." "Installez PostgreSQL 16 puis relancez INSTALLER-SERVEUR.bat."
}
Write-InstallLog "POSTGRES_PSQL" "OK" $psql

$pgPassSecure = Read-Host "Mot de passe du superutilisateur PostgreSQL (postgres)" -AsSecureString
$pgPass = ConvertTo-Plain $pgPassSecure
$env:PGPASSWORD = $pgPass

$appDbPass = New-OneTimePassword
$appDbPassSql = $appDbPass.Replace("'", "''")

try {
    $roleSql = @"
DO `$`$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'cpcredo') THEN
    CREATE ROLE cpcredo LOGIN NOSUPERUSER NOCREATEDB PASSWORD '$appDbPassSql';
  ELSE
    ALTER ROLE cpcredo LOGIN NOSUPERUSER NOCREATEDB PASSWORD '$appDbPassSql';
  END IF;
END
`$`$;
"@
    [void](Invoke-Psql $psql "postgres" $roleSql)
    Write-InstallLog "POSTGRES_ROLE" "OK" "role cpcredo (sans CREATEDB)"

    $exists = Invoke-Psql $psql "postgres" "SELECT 1 FROM pg_database WHERE datname='cpcredo'"
    if ($exists -eq "1") {
        Write-InstallLog "POSTGRES_DB" "OK" "cpcredo existe deja - creation ignoree"
    }
    else {
        [void](Invoke-Psql $psql "postgres" "CREATE DATABASE cpcredo")
        Write-InstallLog "POSTGRES_DB" "OK" "cpcredo cree"
    }
    [void](Invoke-Psql $psql "postgres" "GRANT ALL ON DATABASE cpcredo TO cpcredo")
    try { [void](Invoke-Psql $psql "postgres" "ALTER DATABASE cpcredo OWNER TO cpcredo") } catch { }
    try { [void](Invoke-Psql $psql "cpcredo" "GRANT ALL ON SCHEMA public TO cpcredo") } catch { }
    Write-InstallLog "POSTGRES_GRANT" "OK" "GRANT ALL ON DATABASE cpcredo TO cpcredo"
}
catch {
    $env:PGPASSWORD = $null
    Fail-Step "POSTGRES_DB" "Impossible de preparer la base cpcredo." "Verifiez le mot de passe postgres et que le service PostgreSQL est demarre, puis relancez INSTALLER-SERVEUR.bat."
}

$jwtBytes = New-SecretBytes 32
$jwtSecret = [Convert]::ToBase64String([byte[]]$jwtBytes)
$kycRoot = Join-Path $dest "data\kyc"
$conn = "Host=localhost;Port=5432;Database=cpcredo;Username=cpcredo;Password=$appDbPass"

$settings = @"
{
  "Urls": "http://0.0.0.0:5080",
  "ConnectionStrings": {
    "Default": "$(Escape-JsonString $conn)"
  },
  "Jwt": {
    "Issuer": "CPCREDO",
    "Audience": "CPCREDO.Staff",
    "Secret": "$(Escape-JsonString $jwtSecret)",
    "ExpiryMinutes": 480
  },
  "Cors": { "Origins": [] },
  "Seed": { "Enabled": false },
  "Backup": { "Folder": "$(Escape-JsonString (Join-Path $dest "backups"))", "PgDumpPath": "" },
  "KycStorage": { "RootPath": "$(Escape-JsonString $kycRoot)" },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
    }
  },
  "AllowedHosts": "*"
}
"@
try {
    Set-Content -LiteralPath (Join-Path $dest "appsettings.Production.json") -Value $settings -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $dest "appsettings.json") -Value $settings -Encoding UTF8
    Write-InstallLog "SETTINGS" "OK" "Seed:Enabled false ; http://0.0.0.0:5080"
}
catch {
    Fail-Step "SETTINGS" "Impossible d'ecrire appsettings.Production.json." "Verifiez les droits sur $dest puis relancez INSTALLER-SERVEUR.bat."
}
$appDbPass = $null

$exe = Join-Path $dest "CPCREDO.WebApi.exe"
if (-not (Test-Path $exe)) {
    $found = @(Get-ChildItem -Path $dest -Filter "CPCREDO*.exe" -ErrorAction SilentlyContinue)
    if ($found.Count -gt 0) { $exe = $found[0].FullName }
}
if (-not (Test-Path $exe)) {
    Fail-Step "EXE" "CPCREDO.WebApi.exe introuvable dans $dest" "Recreez la cle USB avec publish.ps1."
}

$starter = @"
@echo off
cd /d "$dest"
set ASPNETCORE_ENVIRONMENT=Production
set ASPNETCORE_URLS=http://0.0.0.0:5080
"$exe"
"@
Set-Content -LiteralPath (Join-Path $dest "Start-CPCREDO.cmd") -Value $starter -Encoding ASCII

$adminOnce = New-AdminOneTimePassword

$tr = "`"" + (Join-Path $dest "Start-CPCREDO.cmd") + "`""
$prev = $ErrorActionPreference
$ErrorActionPreference = "Continue"
schtasks /Create /TN "CPCREDO" /SC ONSTART /RL HIGHEST /RU SYSTEM /F /TR $tr | Out-Null
$taskCode = $LASTEXITCODE
$ErrorActionPreference = $prev
if ($taskCode -ne 0) {
    Fail-Step "TASK" "Impossible d'enregistrer la tache planifiee CPCREDO." "Relancez INSTALLER-SERVEUR.bat en tant qu'administrateur."
}
Write-InstallLog "TASK" "OK" "tache CPCREDO au demarrage"

$prev = $ErrorActionPreference
$ErrorActionPreference = "Continue"
netsh advfirewall firewall delete rule name="CPCREDO 5080" | Out-Null
netsh advfirewall firewall add rule name="CPCREDO 5080" dir=in action=allow protocol=TCP localport=5080 profile=private | Out-Null
$fw = $LASTEXITCODE
$ErrorActionPreference = $prev
if ($fw -ne 0) {
    Fail-Step "FIREWALL" "Impossible d'ouvrir le port 5080 (reseau prive)." "Relancez INSTALLER-SERVEUR.bat en tant qu'administrateur."
}
Write-InstallLog "FIREWALL" "OK" "port 5080 profil prive"

$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ASPNETCORE_URLS = "http://0.0.0.0:5080"
$env:Seed__BootstrapAdminPassword = $adminOnce
try {
    $proc = Start-Process -FilePath $exe -WorkingDirectory $dest -PassThru -WindowStyle Hidden
    if ($proc) { $script:StartedPid = $proc.Id }
    Write-InstallLog "START" "OK" ("pid " + $script:StartedPid)
}
catch {
    $env:Seed__BootstrapAdminPassword = $null
    Fail-Step "START" "Impossible de demarrer CPCREDO.WebApi.exe." "Installez le module ASP.NET Core 8 Hosting Bundle, puis relancez INSTALLER-SERVEUR.bat."
}
$env:Seed__BootstrapAdminPassword = $null

$up = $false
for ($n = 1; $n -le 30; $n++) {
    Start-Sleep -Seconds 2
    try {
        $r = Invoke-WebRequest -Uri "http://127.0.0.1:5080/health" -UseBasicParsing -TimeoutSec 3
        if ($r.StatusCode -eq 200) { $up = $true; break }
    }
    catch { }
}
if (-not $up) {
    Fail-Step "HEALTH" "L'application n'a pas repondu sur http://127.0.0.1:5080/health." "Ouvrez le journal. Installez le module ASP.NET Core 8 Hosting Bundle si besoin, verifiez PostgreSQL, puis relancez INSTALLER-SERVEUR.bat."
}
Write-InstallLog "HEALTH" "OK" "http://127.0.0.1:5080/health"

$lan = Get-LanIPv4
$url = "http://127.0.0.1:5080"
if ($lan.Count -gt 0) { $url = "http://$($lan[0]):5080" }

$onceFile = Join-Path $dest "data\admin-initial-password.txt"
$userCount = "0"
try {
    $userCount = Invoke-Psql $psql "cpcredo" "SELECT COUNT(*) FROM users"
}
catch {
    Fail-Step "ADMIN_USER" "Impossible de lire la table users." "Ouvrez le journal ($script:LogPath). Verifiez PostgreSQL, puis relancez INSTALLER-SERVEUR.bat."
}
if ($userCount -eq "0") {
    Fail-Step "ADMIN_USER" "Aucun utilisateur apres la migration." "Ouvrez le journal. Relancez INSTALLER-SERVEUR.bat en tant qu'administrateur."
}
if (Test-Path $onceFile) {
    Write-InstallLog "ADMIN_USER" "OK" "administrateur initial cree"
    Show-LargePasswordBox $adminOnce $url
    try { Remove-Item -LiteralPath $onceFile -Force } catch { }
    Write-InstallLog "ADMIN_USER" "OK" "mot de passe affiche une fois puis fichier supprime"
}
else {
    Write-InstallLog "ADMIN_USER" "OK" "utilisateurs deja presents - mot de passe initial non reaffiche"
}
$env:PGPASSWORD = $null
$pgPass = $null
$adminOnce = $null

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("CPCREDO est pret.")
$lines.Add("")
$lines.Add("Ouvrez :")
$lines.Add("http://127.0.0.1:5080")
foreach ($ip in $lan) { $lines.Add("http://${ip}:5080") }
$lines.Add("")
$lines.Add("Changez le mot de passe a la premiere connexion.")
$lines.Add("")
$lines.Add("Si l'ecran n'a pas change : Ctrl+F5 (ou fermez l'onglet).")
$final = [string]::Join([Environment]::NewLine, $lines)
[void][System.Windows.Forms.MessageBox]::Show($final, "CPCREDO", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information)
Write-InstallLog "DONE" "OK" $url
Write-Host $final
