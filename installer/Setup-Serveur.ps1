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

function Open-CpcredoUrl {
    param([string]$Url)
    try {
        $cmd = Join-Path $env:SystemRoot "System32\cmd.exe"
        Start-Process -FilePath $cmd -ArgumentList "/c start `"`" `"$Url`"" -WindowStyle Hidden | Out-Null
        return
    }
    catch { }
    try { Start-Process $Url | Out-Null } catch { }
}

function New-DesktopUrlShortcut {
    param([string]$TargetUrl)
    $desktop = [Environment]::GetFolderPath("Desktop")
    $urlFile = Join-Path $desktop "CPCREDO.url"
    Set-Content -LiteralPath $urlFile -Value ("[InternetShortcut]`r`nURL=$TargetUrl`r`n") -Encoding ASCII
    $lnk = Join-Path $desktop "CPCREDO.lnk"
    if (Test-Path $lnk) {
        try { Remove-Item -LiteralPath $lnk -Force } catch { }
    }
}

function Start-CpcredoHidden {
    param([string]$ExePath, [string]$WorkDir)
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $ExePath
        $psi.WorkingDirectory = $WorkDir
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
        try { $psi.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Production" } catch { }
        $p = New-Object System.Diagnostics.Process
        $p.StartInfo = $psi
        if ($p.Start()) { return $p.Id }
    }
    catch { }
    $p2 = Start-Process -FilePath $ExePath -WorkingDirectory $WorkDir -WindowStyle Hidden -PassThru
    if ($null -eq $p2) { throw "start failed" }
    return $p2.Id
}

function Get-JsonPath {
    param($Object, [string[]]$Names)
    $cur = $Object
    foreach ($name in $Names) {
        if ($null -eq $cur) { return $null }
        $prop = $cur.PSObject.Properties[$name]
        if ($null -eq $prop) { return $null }
        $cur = $prop.Value
    }
    return $cur
}

function Test-PfxPassword {
    param([string]$PfxPath, [string]$Password)
    if (-not (Test-Path $PfxPath)) { return $false }
    if ([string]::IsNullOrWhiteSpace($Password)) { return $false }
    try {
        $secure = ConvertTo-SecureString -String $Password -Force -AsPlainText
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($PfxPath, $secure)
        if ($cert) { $cert.Dispose() }
        return $true
    }
    catch { return $false }
}

function New-OfficeCertificate {
    param([string]$CertDir, [string]$PfxPassword, [string[]]$LanIps)
    New-Item -ItemType Directory -Force -Path $CertDir | Out-Null
    $readme = Join-Path $CertDir "README.txt"
    Set-Content -LiteralPath $readme -Value "La premiere visite du navigateur peut afficher un avertissement (certificat auto-signe du bureau). Choisissez Continuer vers le site. Aucun nom de domaine public n'est requis." -Encoding UTF8
    $pfx = Join-Path $CertDir "cpcredo.pfx"
    if ((Test-Path $pfx) -and (Test-PfxPassword -PfxPath $pfx -Password $PfxPassword)) {
        Write-InstallLog "CERT" "OK" "certificat existant reutilise"
        return
    }
    if (Test-Path $pfx) {
        Remove-Item -LiteralPath $pfx -Force -ErrorAction SilentlyContinue
        Write-InstallLog "CERT" "INFO" "ancien certificat recree (mot de passe ne correspondait plus)"
    }
    $sanParts = New-Object System.Collections.Generic.List[string]
    [void]$sanParts.Add("DNS=localhost")
    [void]$sanParts.Add("DNS=CPCREDO")
    [void]$sanParts.Add("IPAddress=127.0.0.1")
    foreach ($ip in $LanIps) {
        if (-not [string]::IsNullOrWhiteSpace($ip)) { [void]$sanParts.Add("IPAddress=$ip") }
    }
    $san = "2.5.29.17={text}" + [string]::Join("&", $sanParts)
    try {
        $cert = New-SelfSignedCertificate -Subject "CN=CPCREDO" -FriendlyName "CPCREDO" `
            -CertStoreLocation "Cert:\LocalMachine\My" `
            -KeyExportPolicy Exportable -KeySpec KeyExchange -HashAlgorithm SHA256 `
            -NotAfter (Get-Date).AddYears(10) `
            -TextExtension @($san)
        $secure = ConvertTo-SecureString -String $PfxPassword -Force -AsPlainText
        Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $secure | Out-Null
        Export-Certificate -Cert $cert -FilePath (Join-Path $CertDir "cpcredo.cer") | Out-Null
        Write-InstallLog "CERT" "OK" $pfx
    }
    catch {
        throw "Impossible de creer le certificat HTTPS : $($_.Exception.Message)"
    }
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
$lan = Get-LanIPv4
$certsDir = Join-Path $dest "certs"
$pfxPass = New-OneTimePassword
$existingSettings = Join-Path $dest "appsettings.Production.json"
if (Test-Path $existingSettings) {
    try {
        $old = Get-Content -LiteralPath $existingSettings -Raw -Encoding UTF8 | ConvertFrom-Json
        $existingPass = Get-JsonPath $old @("Kestrel", "Endpoints", "HttpsLan", "Certificate", "Password")
        if ([string]::IsNullOrWhiteSpace($existingPass)) {
            $existingPass = Get-JsonPath $old @("Kestrel", "Certificates", "Default", "Password")
        }
        if (-not [string]::IsNullOrWhiteSpace($existingPass)) { $pfxPass = [string]$existingPass }
    } catch { }
}
try {
    New-OfficeCertificate -CertDir $certsDir -PfxPassword $pfxPass -LanIps $lan
}
catch {
    Fail-Step "CERT" $_.Exception.Message "Verifiez que Windows peut creer un certificat auto-signe (module PKI), puis relancez INSTALLER-SERVEUR.bat."
}
$pfxRel = "certs/cpcredo.pfx"

$settings = @"
{
  "ConnectionStrings": {
    "Default": "$(Escape-JsonString $conn)"
  },
  "Kestrel": {
    "Endpoints": {
      "HttpLocal": { "Url": "http://127.0.0.1:5080" },
      "HttpsLan": {
        "Url": "https://0.0.0.0:5443",
        "Certificate": {
          "Path": "$(Escape-JsonString $pfxRel)",
          "Password": "$(Escape-JsonString $pfxPass)"
        }
      }
    }
  },
  "Jwt": {
    "Issuer": "CPCREDO",
    "Audience": "CPCREDO.Staff",
    "Secret": "$(Escape-JsonString $jwtSecret)",
    "ExpiryMinutes": 480,
    "IdleMinutes": 12
  },
  "Cors": { "Origins": [] },
  "Seed": { "Enabled": false },
  "Backup": { "Folder": "$(Escape-JsonString (Join-Path $dest "backups"))", "PgDumpPath": "", "AutoBackupEnabled": true, "RetentionDays": 14, "KeepFiles": 7 },
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
    Write-InstallLog "SETTINGS" "OK" "Seed:Enabled false ; https://0.0.0.0:5443 + http://127.0.0.1:5080"
    $pfxPass = $null
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
start "CPCREDO" /D "$dest" "$exe"
"@
Set-Content -LiteralPath (Join-Path $dest "Start-CPCREDO.cmd") -Value $starter -Encoding ASCII

$adminOnce = New-AdminOneTimePassword

$tr = "$env:ComSpec /c `"$(Join-Path $dest "Start-CPCREDO.cmd")`""
$prev = $ErrorActionPreference
$ErrorActionPreference = "Continue"
schtasks /Create /TN "CPCREDO" /SC ONSTART /RL HIGHEST /RU SYSTEM /F /TR $tr | Out-Null
$taskCode = $LASTEXITCODE
$ErrorActionPreference = $prev
if ($taskCode -ne 0) {
    Fail-Step "TASK" "Impossible d'enregistrer la tache planifiee CPCREDO." "Relancez INSTALLER-SERVEUR.bat en tant qu'administrateur."
}
Write-InstallLog "TASK" "OK" "tache CPCREDO au demarrage"

$backupSrc = Join-Path $UsbRoot "backup.ps1"
if (-not (Test-Path $backupSrc)) {
    $backupSrc = Join-Path $UsbRoot "deploy\windows\backup.ps1"
}
if (-not (Test-Path $backupSrc)) {
    Fail-Step "BACKUP_SCRIPT" "backup.ps1 introuvable sur la cle USB." "Recreez la cle avec publish.ps1."
}
Copy-Item -LiteralPath $backupSrc -Destination (Join-Path $dest "backup.ps1") -Force
Write-InstallLog "BACKUP_SCRIPT" "OK" (Join-Path $dest "backup.ps1")

$backupTr = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $dest "backup.ps1")`""
$autoBackup = $true
$bakSettingsPath = Join-Path $dest "data\backup-settings.json"
if (Test-Path $bakSettingsPath) {
    try {
        $bakSaved = Get-Content -LiteralPath $bakSettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $autoProp = Get-JsonPath $bakSaved @("autoBackupEnabled")
        if ($null -eq $autoProp) { $autoProp = Get-JsonPath $bakSaved @("AutoBackupEnabled") }
        if ($null -ne $autoProp) { $autoBackup = [bool]$autoProp }
    } catch { }
}
$prev = $ErrorActionPreference
$ErrorActionPreference = "Continue"
schtasks /Query /TN "CPCREDO-Backup" | Out-Null
$backupExists = $LASTEXITCODE -eq 0
if ($backupExists) {
    schtasks /Change /TN "CPCREDO-Backup" /TR $backupTr | Out-Null
    $backupTaskCode = $LASTEXITCODE
    Write-InstallLog "BACKUP_TASK" "OK" "tache CPCREDO-Backup : chemin mis a jour"
} else {
    schtasks /Create /TN "CPCREDO-Backup" /SC DAILY /ST 18:30 /RL HIGHEST /RU SYSTEM /F /TR $backupTr | Out-Null
    $backupTaskCode = $LASTEXITCODE
    Write-InstallLog "BACKUP_TASK" "OK" "tache CPCREDO-Backup quotidienne 18:30"
}
if ($backupTaskCode -eq 0) {
    if ($autoBackup) {
        schtasks /Change /TN "CPCREDO-Backup" /ENABLE | Out-Null
        Write-InstallLog "BACKUP_TASK" "OK" "sauvegarde automatique Activée (18:30)"
    } else {
        schtasks /Change /TN "CPCREDO-Backup" /DISABLE | Out-Null
        Write-InstallLog "BACKUP_TASK" "OK" "sauvegarde automatique Désactivée"
    }
}
$ErrorActionPreference = $prev
if ($backupTaskCode -ne 0) {
    Fail-Step "BACKUP_TASK" "Impossible d'enregistrer la tache planifiee CPCREDO-Backup." "Relancez INSTALLER-SERVEUR.bat en tant qu'administrateur."
}

$prev = $ErrorActionPreference
$ErrorActionPreference = "Continue"
netsh advfirewall firewall delete rule name="CPCREDO 5080" | Out-Null
netsh advfirewall firewall delete rule name="CPCREDO 5443" | Out-Null
netsh advfirewall firewall add rule name="CPCREDO 5443" dir=in action=allow protocol=TCP localport=5443 profile=any | Out-Null
$fw = $LASTEXITCODE
$ErrorActionPreference = $prev
if ($fw -ne 0) {
    Fail-Step "FIREWALL" "Impossible d'ouvrir le port 5443." "Relancez INSTALLER-SERVEUR.bat en tant qu'administrateur."
}
Write-InstallLog "FIREWALL" "OK" "port 5443 (HTTPS, tous profils)"

$env:ASPNETCORE_ENVIRONMENT = "Production"
Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue
$env:Seed__BootstrapAdminPassword = $adminOnce
$appLog = Join-Path $dest "app.log"
try {
    $script:StartedPid = Start-CpcredoHidden -ExePath $exe -WorkDir $dest
    Start-Sleep -Seconds 2
    $running = @(Get-Process -Name "CPCREDO.WebApi" -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) { $script:StartedPid = $running[0].Id }
    Write-InstallLog "START" "OK" ("pid " + $script:StartedPid + " (sans fenetre)")
}
catch {
    $env:Seed__BootstrapAdminPassword = $null
    Fail-Step "START" "Impossible de demarrer CPCREDO.WebApi.exe." "Installez le module ASP.NET Core 8 Hosting Bundle, puis relancez INSTALLER-SERVEUR.bat."
}
$env:Seed__BootstrapAdminPassword = $null

function Test-HealthUrl([string]$Uri, [bool]$Https) {
    try {
        if ($Https) {
            [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
        }
        $r = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 3
        return ($r.StatusCode -eq 200)
    }
    catch { return $false }
}

$up = $false
$healthUrl = "http://127.0.0.1:5080/health"
for ($n = 1; $n -le 30; $n++) {
    Start-Sleep -Seconds 2
    if ($script:StartedPid -and (Get-Process -Id $script:StartedPid -ErrorAction SilentlyContinue) -eq $null) {
        $tail = ""
        if (Test-Path $appLog) { $tail = (Get-Content $appLog -Tail 8 | Out-String) }
        Fail-Step "HEALTH" "CPCREDO.WebApi.exe s'est arrete au demarrage." ("Journal : $appLog" + [Environment]::NewLine + $tail + "Si le certificat HTTPS pose probleme, relancez INSTALLER-SERVEUR.bat.")
    }
    if (Test-HealthUrl "http://127.0.0.1:5080/health" $false) { $up = $true; $healthUrl = "http://127.0.0.1:5080/health"; break }
    if (Test-HealthUrl "https://127.0.0.1:5443/health" $true) { $up = $true; $healthUrl = "https://127.0.0.1:5443/health"; break }
}
if (-not $up) {
    Fail-Step "HEALTH" "L'application n'a pas repondu sur http://127.0.0.1:5080/health ni https://127.0.0.1:5443/health." "Ouvrez C:\CPCREDO\app.log. Installez le module ASP.NET Core 8 Hosting Bundle si besoin, verifiez PostgreSQL, puis relancez INSTALLER-SERVEUR.bat."
}
Write-InstallLog "HEALTH" "OK" $healthUrl

$url = "https://127.0.0.1:5443"
if ($lan.Count -gt 0) { $url = "https://$($lan[0]):5443" }

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
foreach ($ip in $lan) { $lines.Add("https://${ip}:5443") }
$lines.Add("Admin local : http://127.0.0.1:5080")
$lines.Add("")
$lines.Add("Le serveur tourne sans fenetre (CPCREDO.WebApi).")
$lines.Add("Le navigateur s'ouvre sur l'adresse HTTPS du reseau.")
$lines.Add("La premiere visite HTTPS peut afficher un avertissement (certificat auto-signe).")
$lines.Add("Changez le mot de passe a la premiere connexion.")
$lines.Add("")
$lines.Add("Si l'ecran n'a pas change : Ctrl+F5 (ou fermez l'onglet).")
$final = [string]::Join([Environment]::NewLine, $lines)
try { New-DesktopUrlShortcut $url } catch { }
try { Open-CpcredoUrl $url } catch { }
[void][System.Windows.Forms.MessageBox]::Show($final, "CPCREDO", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information)
Write-InstallLog "DONE" "OK" $url
Write-Host $final
