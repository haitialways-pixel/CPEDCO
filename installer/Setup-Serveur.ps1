#Requires -Version 5.1
param(
    [string]$UsbRoot = ""
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($UsbRoot)) { $UsbRoot = $PSScriptRoot }

$script:LogPath = $null
$script:StartedPid = $null
$script:Dest = "C:\CPCREDO"
$script:IsUpdate = $false
$script:PgPort = 5432
$script:AdminOnce = $null
$script:PublicUrl = "https://127.0.0.1:5443"
$script:DbCheckOk = $false
$script:LastTech = ""
$script:MissingList = @()

Add-Type -AssemblyName System.Windows.Forms | Out-Null
Add-Type -AssemblyName System.Drawing | Out-Null
Add-Type -AssemblyName System.Security | Out-Null

function Get-ItemCount {
    param($Value)
    return (As-Array $Value).Count
}

function As-Array {
    param($Value)
    if ($null -eq $Value) { return ,@() }
    if ($Value -is [System.Array]) { return ,$Value }
    return ,@($Value)
}

function Write-InstallLog {
    param([string]$Step, [string]$Status, [string]$Detail)
    $line = "{0:yyyy-MM-dd HH:mm:ss}  {1}  {2}  {3}" -f (Get-Date), $Step, $Status, $Detail
    if ($script:LogPath) {
        try { Add-Content -LiteralPath $script:LogPath -Value $line -Encoding UTF8 } catch { }
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
    $alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789"
    $bytes = New-SecretBytes 20
    $chars = New-Object char[] 20
    for ($i = 0; $i -lt 20; $i++) {
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
    return $Value.Replace("\", "\\").Replace('"', '\"').Replace("`r", "\r").Replace("`n", "\n")
}

function Protect-DpapiString {
    param([string]$Plain, [string]$Path)
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Plain)
    $entropy = [System.Text.Encoding]::UTF8.GetBytes("CPCREDO")
    $protected = [System.Security.Cryptography.ProtectedData]::Protect(
        $bytes, $entropy, [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
    Set-Content -LiteralPath $Path -Value ([Convert]::ToBase64String($protected)) -Encoding ASCII
    try {
        & icacls $Path /inheritance:r /grant:r "SYSTEM:(F)" "Administrators:(F)" | Out-Null
    } catch { }
}

function Unprotect-DpapiString {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return $null }
    $raw = (Get-Content -LiteralPath $Path -Raw -ErrorAction SilentlyContinue)
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    $entropy = [System.Text.Encoding]::UTF8.GetBytes("CPCREDO")
    $plain = [System.Security.Cryptography.ProtectedData]::Unprotect(
        [Convert]::FromBase64String($raw.Trim()),
        $entropy,
        [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
    return [System.Text.Encoding]::UTF8.GetString($plain)
}

function Copy-LogToClipboard {
    if ($script:LogPath -and (Test-Path $script:LogPath)) {
        $text = Get-Content -LiteralPath $script:LogPath -Raw -ErrorAction SilentlyContinue
        if ($text) { [System.Windows.Forms.Clipboard]::SetText($text) }
    }
}

function Show-ErrorDialog {
    param([string]$Message, [string]$WhatToDo, [string]$Tech = "")
    $script:LastTech = $Tech
    $logHint = "Journal : C:\CPCREDO\logs\install.log"
    if ($script:LogPath) { $logHint = "Journal : " + $script:LogPath }
    $human = $Message
    if ($WhatToDo) { $human = $human + [Environment]::NewLine + [Environment]::NewLine + $WhatToDo }
    $human = $human + [Environment]::NewLine + [Environment]::NewLine + $logHint
    $form = New-Object System.Windows.Forms.Form
    $form.Text = "CPCREDO"
    $form.Width = 640
    $form.Height = 420
    $form.StartPosition = "CenterScreen"
    $form.Font = New-Object System.Drawing.Font("Segoe UI", 12)
    $form.FormBorderStyle = "FixedDialog"
    $form.MaximizeBox = $false
    $lbl = New-Object System.Windows.Forms.Label
    $lbl.Left = 24; $lbl.Top = 20; $lbl.Width = 580; $lbl.Height = 180
    $lbl.Text = $human
    $details = New-Object System.Windows.Forms.TextBox
    $details.Left = 24; $details.Top = 210; $details.Width = 580; $details.Height = 80
    $details.Multiline = $true; $details.ReadOnly = $true; $details.ScrollBars = "Vertical"
    $details.Visible = $false
    $details.Text = $Tech
    $btnDetails = New-Object System.Windows.Forms.Button
    $btnDetails.Text = "Détails techniques"
    $btnDetails.Left = 24; $btnDetails.Top = 300; $btnDetails.Width = 200; $btnDetails.Height = 40
    $script:FailDetailsBox = $details
    $btnDetails.Add_Click({
        if ($script:FailDetailsBox) {
            $script:FailDetailsBox.Visible = -not $script:FailDetailsBox.Visible
        }
    })
    $btnCopy = New-Object System.Windows.Forms.Button
    $btnCopy.Text = "Copier le journal"
    $btnCopy.Left = 236; $btnCopy.Top = 300; $btnCopy.Width = 180; $btnCopy.Height = 40
    $btnCopy.Add_Click({ Copy-LogToClipboard })
    $btnRetry = New-Object System.Windows.Forms.Button
    $btnRetry.Text = "Réessayer"
    $btnRetry.Left = 24; $btnRetry.Top = 348; $btnRetry.Width = 180; $btnRetry.Height = 40
    $btnRetry.DialogResult = [System.Windows.Forms.DialogResult]::Retry
    $btnCancel = New-Object System.Windows.Forms.Button
    $btnCancel.Text = "Annuler"
    $btnCancel.Left = 216; $btnCancel.Top = 348; $btnCancel.Width = 160; $btnCancel.Height = 40
    $btnCancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $form.Controls.AddRange(@($lbl, $details, $btnDetails, $btnCopy, $btnRetry, $btnCancel))
    $form.AcceptButton = $btnRetry
    $form.CancelButton = $btnCancel
    return $form.ShowDialog()
}

function Fail-Step {
    param([string]$Step, [string]$Message, [string]$WhatToDo, [string]$Tech = "")
    $tech = $Tech
    if ([string]::IsNullOrWhiteSpace($tech)) { $tech = $Message }
    Write-InstallLog $Step "FAIL" $Message
    if ($script:StartedPid) {
        try { Stop-Process -Id $script:StartedPid -Force -ErrorAction SilentlyContinue } catch { }
    }
    $defaultWhat = "Installation interrompue. Un fichier manque sur la cle ou une etape interne a echoue.`r`nRouvrez l'installeur apres verification du dossier CPCREDO-USB.`r`nJournal : C:\CPCREDO\logs\install.log"
    if ([string]::IsNullOrWhiteSpace($WhatToDo)) { $WhatToDo = $defaultWhat }
    $result = Show-ErrorDialog $Message $WhatToDo $tech
    if ($result -eq [System.Windows.Forms.DialogResult]::Retry) {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = (Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe")
        $psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -STA -File `"$PSCommandPath`""
        $psi.WorkingDirectory = $UsbRoot
        $psi.UseShellExecute = $true
        try { [void][System.Diagnostics.Process]::Start($psi) } catch { }
    }
    exit 1
}

function Get-StatePath { return (Join-Path $script:Dest "logs\install-state.json") }

function Save-InstallState {
    param([hashtable]$State)
    $path = Get-StatePath
    $dir = Split-Path -Parent $path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    ($State | ConvertTo-Json) | Set-Content -LiteralPath $path -Encoding UTF8
}

function Read-InstallState {
    $path = Get-StatePath
    if (-not (Test-Path $path)) { return $null }
    try { return Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { return $null }
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
    $svcs = @(Get-PostgresServices)
    foreach ($s in $svcs) {
        if ($s.Status -eq "Running") { return $true }
    }
    return $false
}

function Test-PortInUse {
    param([int]$Port)
    try {
        $conns = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
        return ((As-Array $conns).Count -gt 0)
    }
    catch {
        try {
            $tcp = New-Object System.Net.Sockets.TcpClient
            $tcp.Connect("127.0.0.1", $Port)
            $tcp.Close()
            return $true
        }
        catch { return $false }
    }
}

function Invoke-Psql {
    param([string]$Psql, [string]$Database, [string]$Sql, [int]$Port = 5432, [string]$User = "postgres")
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $output = & $Psql -h 127.0.0.1 -p $Port -U $User -d $Database -v ON_ERROR_STOP=1 -tAc $Sql 2>&1
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
        return @(As-Array $addrs)
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
    try {
        $startDir = Join-Path ([Environment]::GetFolderPath("CommonStartMenu")) "Programs"
        if (-not (Test-Path $startDir)) { New-Item -ItemType Directory -Force -Path $startDir | Out-Null }
        Copy-Item -LiteralPath $urlFile -Destination (Join-Path $startDir "CPCREDO.url") -Force
    } catch { }
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
    foreach ($ip in @(As-Array $LanIps)) {
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

function Test-DotNet8 {
    $aspnetDir = Join-Path $env:ProgramFiles "dotnet\shared\Microsoft.AspNetCore.App"
    if (-not (Test-Path $aspnetDir)) { return $false }
    $dirs = @(Get-ChildItem $aspnetDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "8.*" })
    return ((As-Array $dirs).Count -gt 0)
}

function Test-DotNetDesktop8 {
    $dir = Join-Path $env:ProgramFiles "dotnet\shared\Microsoft.WindowsDesktop.App"
    if (-not (Test-Path $dir)) { return $false }
    $dirs = @(Get-ChildItem $dir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "8.*" })
    return ((As-Array $dirs).Count -gt 0)
}

function Test-VcRedist {
    $keys = @(
        "HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x64"
    )
    foreach ($k in $keys) {
        if (Test-Path $k) { return $true }
    }
    return $false
}

function Test-WindowsOk {
    if (-not [Environment]::Is64BitOperatingSystem) { return $false }
    try {
        $v = [System.Environment]::OSVersion.Version
        if ($v.Major -gt 10) { return $true }
        if ($v.Major -eq 10 -and $v.Build -ge 10240) { return $true }
    } catch { }
    return $false
}

function Get-FreeGb {
    try {
        $d = Get-PSDrive -Name C -ErrorAction Stop
        return [math]::Round(($d.Free / 1GB), 1)
    }
    catch { return 0 }
}

function Get-FileSha256 {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return $null }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-Manifest {
    $p = Join-Path $UsbRoot "OfflinePackages\offline-packages.json"
    if (-not (Test-Path $p)) { $p = Join-Path $UsbRoot "offline-packages.json" }
    if (-not (Test-Path $p)) { $p = Join-Path $PSScriptRoot "offline-packages.json" }
    if (-not (Test-Path $p)) { return $null }
    try { return Get-Content -LiteralPath $p -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { return $null }
}

function Get-ShaMap {
    $map = @{}
    $p = Join-Path $UsbRoot "OfflinePackages\SHA256SUMS.txt"
    if (-not (Test-Path $p)) { return $map }
    $lines = @(Get-Content -LiteralPath $p -ErrorAction SilentlyContinue)
    foreach ($line in $lines) {
        if ($line -match "^\s*([A-Fa-f0-9]{64})\s+(.+)$") {
            $map[$Matches[2].Trim()] = $Matches[1].ToLowerInvariant()
        }
    }
    return $map
}

function Find-OfflineFile {
    param([string]$FileName)
    $p = Join-Path $UsbRoot "OfflinePackages\$FileName"
    if (Test-Path $p) { return $p }
    return $null
}

function Assert-PackageFile {
    param([string]$Path, [int64]$MinBytes, [string]$ExpectedSha)
    if (-not (Test-Path $Path)) { return $false }
    $len = (Get-Item -LiteralPath $Path).Length
    if ($len -lt $MinBytes) { return $false }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSha)) {
        $actual = Get-FileSha256 $Path
        if ($actual -ne $ExpectedSha.ToLowerInvariant()) { return $false }
    }
    return $true
}

function Get-OfficialPackage {
    param($Entry, [string]$ExpectedSha)
    $local = Find-OfflineFile $Entry.file
    if ($local -and (Assert-PackageFile $local ([int64]$Entry.minBytes) $ExpectedSha)) {
        Write-InstallLog "PACKAGE" "OK" ("USB " + $Entry.file)
        return $local
    }
    if ($local -and -not (Assert-PackageFile $local ([int64]$Entry.minBytes) $ExpectedSha)) {
        Write-InstallLog "PACKAGE" "INFO" ("fichier USB ignore (taille ou signature) " + $Entry.file)
    }
    $destDir = Join-Path $script:Dest "logs\downloads"
    New-Item -ItemType Directory -Force -Path $destDir | Out-Null
    $destFile = Join-Path $destDir $Entry.file
    $url = [string]$Entry.url
    Write-InstallLog "PACKAGE" "INFO" ("telechargement officiel " + $url)
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $client = New-Object System.Net.WebClient
        $client.DownloadFile($url, $destFile)
        $client.Dispose()
    }
    catch {
        throw "Pas de connexion stable. Rebranchez la cle USB complete (dossier OfflinePackages) ou reessayez quand internet sera disponible."
    }
    if (-not (Assert-PackageFile $destFile ([int64]$Entry.minBytes) $ExpectedSha)) {
        throw "Pas de connexion stable. Rebranchez la cle USB complete (dossier OfflinePackages) ou reessayez quand internet sera disponible."
    }
    return $destFile
}

function Wait-Ui {
    try { [System.Windows.Forms.Application]::DoEvents() } catch { }
}

function Invoke-SilentSetup {
    param([string]$FilePath, [string]$Arguments)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = $Arguments
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    while (-not $p.HasExited) {
        Wait-Ui
        Start-Sleep -Milliseconds 400
    }
    return $p.ExitCode
}

function Register-ResumeAfterReboot {
    $bat = Join-Path $UsbRoot "INSTALLER-SERVEUR.bat"
    $exe = Join-Path $UsbRoot "Installer-CPCREDO.exe"
    $cmd = $bat
    if (Test-Path $exe) { $cmd = $exe }
    try {
        New-Item -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\RunOnce" -Force | Out-Null
        Set-ItemProperty -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\RunOnce" -Name "CPCREDO-Install" -Value "`"$cmd`""
    } catch {
        Write-InstallLog "RESUME" "INFO" "impossible d'enregistrer la reprise automatique"
    }
}

# --- wizard ---
$script:Form = $null
$script:Title = $null
$script:Body = $null
$script:CheckList = $null
$script:Progress = $null
$script:ProgressLabel = $null
$script:BtnNext = $null
$script:BtnBack = $null
$script:PasswordBox = $null
$script:RadioFr = $null
$script:RadioHt = $null
$script:AdminLabel = $null
$script:DbLabel = $null
$script:Page = 1

function New-UiFont([int]$Size, [bool]$Bold = $false) {
    $style = [System.Drawing.FontStyle]::Regular
    if ($Bold) { $style = [System.Drawing.FontStyle]::Bold }
    return New-Object System.Drawing.Font("Segoe UI", $Size, $style)
}

function Show-Page {
    param([int]$Number)
    $script:Page = $Number
    $script:PasswordBox.Visible = $false
    $script:CheckList.Visible = $false
    $script:Progress.Visible = $false
    $script:ProgressLabel.Visible = $false
    $script:RadioFr.Visible = $false
    $script:RadioHt.Visible = $false
    $script:AdminLabel.Visible = $false
    $script:DbLabel.Visible = $false
    $script:BtnBack.Visible = $false
    $script:BtnNext.Enabled = $true
    $script:BtnNext.Text = "Suivant"
    if ($Number -eq 1) {
        $script:Title.Text = "Bienvenue"
        $updateNote = ""
        if ($script:IsUpdate) { $updateNote = [Environment]::NewLine + [Environment]::NewLine + "CPCREDO est déjà installé. Cliquez sur Suivant pour mettre à jour." }
        $script:Body.Text = "Installation de CPCREDO. Nous allons préparer cet ordinateur. Cliquez sur Suivant." + $updateNote + [Environment]::NewLine + [Environment]::NewLine + "Mot de passe d'installation :"
        $script:PasswordBox.Visible = $true
        $script:PasswordBox.Focus()
    }
    elseif ($Number -eq 2) {
        $script:Title.Text = "Vérification"
        $script:Body.Text = "Contrôle de cet ordinateur :"
        $script:CheckList.Visible = $true
        $script:BtnBack.Visible = $true
    }
    elseif ($Number -eq 3) {
        $script:Title.Text = "Préparation des composants manquants"
        $script:Body.Text = "Il manque un outil nécessaire. CPCREDO va l'installer pour vous. Ne fermez pas cette fenêtre."
        $script:Progress.Visible = $true
        $script:ProgressLabel.Visible = $true
        $script:BtnNext.Enabled = $false
    }
    elseif ($Number -eq 4) {
        $script:Title.Text = "Installation CPCREDO"
        $script:Body.Text = "Copie du logiciel et préparation de la caisse. Ne fermez pas cette fenêtre."
        $script:Progress.Visible = $true
        $script:ProgressLabel.Visible = $true
        $script:BtnNext.Enabled = $false
    }
    elseif ($Number -eq 5) {
        $script:Title.Text = "Terminé"
        $script:Body.Text = "CPCREDO est prêt. Cliquez sur Démarrer."
        $script:RadioFr.Visible = $true
        $script:RadioHt.Visible = $true
        $script:AdminLabel.Visible = $true
        $script:DbLabel.Visible = $true
        $script:BtnNext.Text = "Démarrer"
        $script:BtnNext.Enabled = $true
    }
    Wait-Ui
}

function Confirm-InstallLockUi {
    $lockPath = Join-Path $UsbRoot "Templates\install.lock"
    if (-not (Test-Path $lockPath)) {
        Fail-Step "PASSWORD" "Fichier d'installation manquant : Templates\install.lock" "Recreez la cle USB avec publish.ps1 sur le PC developpeur."
    }
    $expected = (Get-Content -LiteralPath $lockPath -Raw).Trim().ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($expected) -or $expected.Length -lt 64) {
        Fail-Step "PASSWORD" "install.lock invalide." "Recreez la cle USB avec publish.ps1."
    }
    $state = Read-InstallState
    if ($state -and $state.resume -eq $true) {
        Write-InstallLog "PASSWORD" "OK" "reprise apres redemarrage"
        return $true
    }
    $plain = $script:PasswordBox.Text
    if ([string]::IsNullOrWhiteSpace($plain)) {
        [void][System.Windows.Forms.MessageBox]::Show("Entrez le mot de passe d'installation.", "CPCREDO")
        return $false
    }
    $actual = Get-InstallPasswordHash $plain
    if ($actual -ne $expected) {
        [void][System.Windows.Forms.MessageBox]::Show("Mot de passe incorrect.", "CPCREDO")
        Write-InstallLog "PASSWORD" "FAIL" "essai"
        return $false
    }
    Write-InstallLog "PASSWORD" "OK" "mot de passe accepte"
    return $true
}

function Update-CheckList {
    $lines = New-Object System.Collections.Generic.List[string]
    $ok = $true
    if (Test-WindowsOk) { [void]$lines.Add("[OK] Windows : cet ordinateur convient.") }
    else { [void]$lines.Add("[X] Windows : trop ancien ou 32 bits. Il faut Windows 10 ou 11 64 bits."); $ok = $false }

    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        [void]$lines.Add("[OK] Droits administrateur : oui.")
    }
    else {
        [void]$lines.Add("[X] Droits administrateur : non.")
        $ok = $false
    }

    $free = Get-FreeGb
    if ($free -ge 2) { [void]$lines.Add("[OK] Espace disque : $free Go libres.") }
    else { [void]$lines.Add("[X] Espace disque : pas assez de place (il faut 2 Go)."); $ok = $false }

    $script:MissingList = @()
    if (Test-DotNet8) { [void]$lines.Add("[OK] .NET : trouve.") }
    else { [void]$lines.Add("[...] .NET : manquant. CPCREDO l'installera."); $script:MissingList += "dotnet" }

    if (Test-VcRedist) { [void]$lines.Add("[OK] Composant Windows : trouve.") }
    else { [void]$lines.Add("[...] Composant Windows : manquant. CPCREDO l'installera."); $script:MissingList += "vcredist" }

    $pg = Test-PostgresRunning
    if (-not $pg) {
        $svcs = @(Get-PostgresServices)
        if ((As-Array $svcs).Count -gt 0) {
            foreach ($s in $svcs) { try { Start-Service -Name $s.Name -ErrorAction SilentlyContinue } catch { } }
            Start-Sleep -Seconds 2
            $pg = Test-PostgresRunning
        }
    }
    if ($pg -or (Get-PsqlPath)) { [void]$lines.Add("[OK] Base de données : trouvée.") }
    else { [void]$lines.Add("[...] Base de données : manquante. CPCREDO l'installera."); $script:MissingList += "postgresql" }

    $script:CheckList.Text = [string]::Join([Environment]::NewLine + [Environment]::NewLine, $lines)
    return $ok
}

function Set-Progress {
    param([int]$Value, [string]$Text)
    $script:Progress.Value = [Math]::Min(100, [Math]::Max(0, $Value))
    $script:ProgressLabel.Text = $Text
    Wait-Ui
}

function Install-MissingComponents {
    $manifest = Get-Manifest
    if ($null -eq $manifest) {
        throw "Fichier introuvable sur la cle : OfflinePackages\offline-packages.json"
    }
    $shaMap = Get-ShaMap
    $missing = @(As-Array $script:MissingList)
    $total = (As-Array $missing).Count
    if ($total -eq 0) { return }
    $i = 0
    if ($missing -contains "vcredist") {
        $i++
        Set-Progress 10 ("Étape $i sur $total : installation d'un composant Windows")
        $entry = $manifest.vcredist
        $sha = $shaMap[$entry.file]
        $file = Get-OfficialPackage $entry $sha
        $code = Invoke-SilentSetup $file $entry.args
        Write-InstallLog "VCREDIST" "OK" ("exit " + $code)
        if ($code -eq 3010 -or $code -eq 1641) {
            Save-InstallState @{ resume = $true; step = "after-vcredist"; needReboot = $true }
            Register-ResumeAfterReboot
            [void][System.Windows.Forms.MessageBox]::Show("L'ordinateur doit redémarrer. Après le redémarrage, rouvrez Installer CPCREDO. Il reprendra tout seul.", "CPCREDO")
            Restart-Computer -Force
            exit 0
        }
    }
    if ($missing -contains "dotnet") {
        $i++
        Set-Progress 40 ("Étape $i sur $total : installation de .NET")
        $entry = $manifest.dotnetHosting
        $sha = $shaMap[$entry.file]
        $file = Get-OfficialPackage $entry $sha
        $code = Invoke-SilentSetup $file $entry.args
        Write-InstallLog "DOTNET" "OK" ("hosting exit " + $code)
        if ($code -eq 3010 -or $code -eq 1641) {
            Save-InstallState @{ resume = $true; step = "after-dotnet"; needReboot = $true }
            Register-ResumeAfterReboot
            [void][System.Windows.Forms.MessageBox]::Show("L'ordinateur doit redémarrer. Après le redémarrage, rouvrez Installer CPCREDO. Il reprendra tout seul.", "CPCREDO")
            Restart-Computer -Force
            exit 0
        }
        if (-not (Test-DotNetDesktop8) -and $manifest.dotnetDesktop) {
            try {
                $dsha = $shaMap[$manifest.dotnetDesktop.file]
                $dfile = Get-OfficialPackage $manifest.dotnetDesktop $dsha
                [void](Invoke-SilentSetup $dfile $manifest.dotnetDesktop.args)
            } catch { Write-InstallLog "DOTNET" "INFO" "runtime desktop facultatif ignore" }
        }
        if (-not (Test-DotNet8)) {
            throw "L'installation de .NET n'a pas abouti. Reessayez."
        }
    }
    if ($missing -contains "postgresql") {
        $i++
        Set-Progress 75 ("Étape $i sur $total : installation de la base de données")
        Install-PostgreSQLOffline $manifest $shaMap
    }
    Set-Progress 100 "Composants prets."
}

function Install-PostgreSQLOffline {
    param($Manifest, $ShaMap)
    if (Test-PostgresRunning -or (Get-PsqlPath)) {
        Write-InstallLog "POSTGRES" "OK" "deja present, installation ignoree"
        return
    }
    $port = 5432
    if (Test-PortInUse 5432) {
        $port = 5433
        Write-InstallLog "POSTGRES" "INFO" "port 5432 occupe, essai 5433"
    }
    $script:PgPort = $port
    $super = New-OneTimePassword
    $entry = $Manifest.postgresql
    $sha = $ShaMap[$entry.file]
    $file = Get-OfficialPackage $entry $sha
    $prefix = Join-Path $env:ProgramFiles "PostgreSQL\16"
    $dataDir = Join-Path $prefix "data"
    $args = "--mode unattended --unattendedmodeui none --superpassword `"$super`" --servicename postgresql-x64-16 --serverport $port --prefix `"$prefix`" --datadir `"$dataDir`" --disable-components stackbuilder --create_shortcuts 0 --install_runtimes 1"
    $code = Invoke-SilentSetup $file $args
    Write-InstallLog "POSTGRES" "INFO" ("installeur exit " + $code)
    Protect-DpapiString $super (Join-Path $script:Dest "logs\pg-setup.dpapi")
    for ($n = 1; $n -le 40; $n++) {
        Wait-Ui
        Start-Sleep -Seconds 2
        if (Test-PostgresRunning) { break }
        $svcs = @(Get-PostgresServices)
        foreach ($s in $svcs) { try { Start-Service -Name $s.Name -ErrorAction SilentlyContinue } catch { } }
    }
    if (-not (Get-PsqlPath)) {
        throw "La base de donnees n'a pas pu etre installee. Rebranchez la cle USB complete (dossier OfflinePackages) puis reessayez."
    }
    $env:PGPASSWORD = $super
}

function Copy-AppFromUsb {
    $appSource = Join-Path $UsbRoot "App"
    if (-not (Test-Path $appSource)) {
        throw "Fichier introuvable sur la cle : $appSource"
    }
    $required = @("CPCREDO.WebApi.exe", "CPCREDO.WebApi.dll")
    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($name in $required) {
        $p = Join-Path $appSource $name
        if (-not (Test-Path $p)) { [void]$missing.Add($p) }
    }
    if ((As-Array $missing).Count -gt 0) {
        throw ("Fichier introuvable sur la cle : " + [string]::Join(", ", $missing))
    }

    foreach ($folder in @($script:Dest, (Join-Path $script:Dest "logs"), (Join-Path $script:Dest "data"), (Join-Path $script:Dest "backups"), (Join-Path $script:Dest "certs"))) {
        if (-not (Test-Path $folder)) { New-Item -ItemType Directory -Force -Path $folder | Out-Null }
    }

    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    schtasks /End /TN "CPCREDO" | Out-Null
    $procs = @(Get-Process -Name "CPCREDO.WebApi" -ErrorAction SilentlyContinue)
    foreach ($pr in $procs) { try { Stop-Process -Id $pr.Id -Force -ErrorAction SilentlyContinue } catch { } }
    Start-Sleep -Seconds 1
    $ErrorActionPreference = $prev

    $skip = @("data", "logs", "backups", "certs", "appsettings.Production.json", "appsettings.json", "appsettings.Development.json")
    $items = @(Get-ChildItem -LiteralPath $appSource -Force -ErrorAction SilentlyContinue)
    foreach ($item in $items) {
        if ($skip -contains $item.Name) { continue }
        $target = Join-Path $script:Dest $item.Name
        try {
            if ($item.PSIsContainer) {
                if (-not (Test-Path $target)) { New-Item -ItemType Directory -Force -Path $target | Out-Null }
                $children = @(Get-ChildItem -LiteralPath $item.FullName -Force -ErrorAction SilentlyContinue)
                if ((As-Array $children).Count -gt 0) {
                    Copy-Item -Path (Join-Path $item.FullName "*") -Destination $target -Recurse -Force -ErrorAction Stop
                }
            }
            else {
                Copy-Item -LiteralPath $item.FullName -Destination $target -Force -ErrorAction Stop
            }
        }
        catch {
            throw "Fichier introuvable sur la cle : $($item.FullName)"
        }
    }

    $srcWww = Join-Path $appSource "wwwroot"
    $destWww = Join-Path $script:Dest "wwwroot"
    if (Test-Path $srcWww) {
        if (-not (Test-Path $destWww)) { New-Item -ItemType Directory -Force -Path $destWww | Out-Null }
        $wwwItems = @(Get-ChildItem -LiteralPath $srcWww -Force -ErrorAction SilentlyContinue)
        if ((As-Array $wwwItems).Count -gt 0) {
            Copy-Item -Path (Join-Path $srcWww "*") -Destination $destWww -Recurse -Force
        }
        $assets = Join-Path $destWww "assets"
        $stale = @(Get-ChildItem -LiteralPath $assets -ErrorAction SilentlyContinue)
        if ((As-Array $stale).Count -eq 0) {
            Write-InstallLog "COPY" "INFO" "wwwroot/assets vide ou absent (ok)"
        }
    }
    else {
        Write-InstallLog "COPY" "INFO" "wwwroot absent sur la cle, copie ignoree"
    }

    $devOnDisk = Join-Path $script:Dest "appsettings.Development.json"
    if (Test-Path $devOnDisk) { Remove-Item -LiteralPath $devOnDisk -Force -ErrorAction SilentlyContinue }
    Write-InstallLog "COPY" "OK" $script:Dest
}

function Ensure-PostgresDatabase {
    $svcs = @(Get-PostgresServices)
    if ((As-Array $svcs).Count -gt 0 -and -not (Test-PostgresRunning)) {
        foreach ($s in $svcs) { try { Start-Service -Name $s.Name -ErrorAction Stop } catch { } }
        Start-Sleep -Seconds 3
    }
    if (-not (Test-PostgresRunning)) {
        throw "La base de donnees n'est pas demarree."
    }
    $psql = Get-PsqlPath
    if (-not $psql) { throw "Outil de base introuvable (psql)." }

    if (-not $env:PGPASSWORD) {
        $saved = Unprotect-DpapiString (Join-Path $script:Dest "logs\pg-setup.dpapi")
        if ($saved) { $env:PGPASSWORD = $saved }
    }
    if (-not $env:PGPASSWORD) {
        $form = New-Object System.Windows.Forms.Form
        $form.Text = "CPCREDO"
        $form.Width = 520; $form.Height = 260
        $form.StartPosition = "CenterScreen"
        $form.Font = New-UiFont 12
        $lbl = New-Object System.Windows.Forms.Label
        $lbl.Left = 20; $lbl.Top = 20; $lbl.Width = 460; $lbl.Height = 70
        $lbl.Text = "Une base existe deja. Entrez le mot de passe du compte postgres (un seul champ)."
        $tb = New-Object System.Windows.Forms.TextBox
        $tb.Left = 20; $tb.Top = 100; $tb.Width = 360; $tb.UseSystemPasswordChar = $true
        $suggested = New-OneTimePassword
        $btnCopy = New-Object System.Windows.Forms.Button
        $btnCopy.Text = "Copier"
        $btnCopy.Left = 390; $btnCopy.Top = 96; $btnCopy.Width = 90; $btnCopy.Height = 32
        $btnCopy.Add_Click({ [System.Windows.Forms.Clipboard]::SetText($tb.Text) })
        $ok = New-Object System.Windows.Forms.Button
        $ok.Text = "OK"; $ok.Left = 20; $ok.Top = 160; $ok.Width = 140; $ok.Height = 40
        $ok.DialogResult = [System.Windows.Forms.DialogResult]::OK
        $form.Controls.AddRange(@($lbl, $tb, $btnCopy, $ok))
        $form.AcceptButton = $ok
        $tb.Text = $suggested
        if ($form.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK -or [string]::IsNullOrWhiteSpace($tb.Text)) {
            throw "Mot de passe de la base manquant."
        }
        $env:PGPASSWORD = $tb.Text
    }

    $appDbPass = New-OneTimePassword
    $appDbPassSql = $appDbPass.Replace("'", "''")
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
    [void](Invoke-Psql $psql "postgres" $roleSql $script:PgPort)
    Write-InstallLog "POSTGRES_ROLE" "OK" "role cpcredo"
    $exists = Invoke-Psql $psql "postgres" "SELECT 1 FROM pg_database WHERE datname='cpcredo'" $script:PgPort
    if ($exists -eq "1") {
        Write-InstallLog "POSTGRES_DB" "OK" "cpcredo existe deja - creation ignoree"
    }
    else {
        [void](Invoke-Psql $psql "postgres" "CREATE DATABASE cpcredo" $script:PgPort)
        Write-InstallLog "POSTGRES_DB" "OK" "cpcredo cree"
    }
    try { [void](Invoke-Psql $psql "postgres" "GRANT ALL ON DATABASE cpcredo TO cpcredo" $script:PgPort) } catch { }
    try { [void](Invoke-Psql $psql "postgres" "ALTER DATABASE cpcredo OWNER TO cpcredo" $script:PgPort) } catch { }
    try { [void](Invoke-Psql $psql "cpcredo" "GRANT ALL ON SCHEMA public TO cpcredo" $script:PgPort) } catch { }
    $conn = "Host=127.0.0.1;Port=$($script:PgPort);Database=cpcredo;Username=cpcredo;Password=$appDbPass"
    Protect-DpapiString $conn (Join-Path $script:Dest "data\connection.dpapi")
    $script:AppConn = $conn
    $appDbPass = $null
}

function Write-AppSettings {
    $jwtBytes = New-SecretBytes 32
    $jwtSecret = [Convert]::ToBase64String([byte[]]$jwtBytes)
    $kycRoot = Join-Path $script:Dest "data\kyc"
    $lan = @(Get-LanIPv4)
    $certsDir = Join-Path $script:Dest "certs"
    $pfxPass = New-OneTimePassword
    $existingSettings = Join-Path $script:Dest "appsettings.Production.json"
    if (Test-Path $existingSettings) {
        try {
            $old = Get-Content -LiteralPath $existingSettings -Raw -Encoding UTF8 | ConvertFrom-Json
            $existingPass = Get-JsonPath $old @("Kestrel", "Endpoints", "HttpsLan", "Certificate", "Password")
            if (-not [string]::IsNullOrWhiteSpace($existingPass)) { $pfxPass = [string]$existingPass }
            $existingJwt = Get-JsonPath $old @("Jwt", "Secret")
            if (-not [string]::IsNullOrWhiteSpace($existingJwt)) { $jwtSecret = [string]$existingJwt }
        } catch { }
    }
    New-OfficeCertificate -CertDir $certsDir -PfxPassword $pfxPass -LanIps $lan
    $settings = @"
{
  "ConnectionStrings": {
    "Default": ""
  },
  "Kestrel": {
    "Endpoints": {
      "HttpLocal": { "Url": "http://127.0.0.1:5080" },
      "HttpsLan": {
        "Url": "https://0.0.0.0:5443",
        "Certificate": {
          "Path": "certs/cpcredo.pfx",
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
  "Backup": { "Folder": "$(Escape-JsonString (Join-Path $script:Dest "backups"))", "PgDumpPath": "", "AutoBackupEnabled": true, "RetentionDays": 14, "KeepFiles": 7 },
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
    Set-Content -LiteralPath (Join-Path $script:Dest "appsettings.Production.json") -Value $settings -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $script:Dest "appsettings.json") -Value $settings -Encoding UTF8
    Write-InstallLog "SETTINGS" "OK" "connexion protegee DPAPI ; Seed:Enabled false"
}

function Finish-WindowsIntegration {
    $exe = Join-Path $script:Dest "CPCREDO.WebApi.exe"
    if (-not (Test-Path $exe)) {
        $found = @(Get-ChildItem -Path $script:Dest -Filter "CPCREDO*.exe" -ErrorAction SilentlyContinue)
        if ((As-Array $found).Count -gt 0) { $exe = $found[0].FullName }
    }
    if (-not (Test-Path $exe)) {
        throw "CPCREDO.WebApi.exe introuvable dans $($script:Dest)"
    }
    $starter = @"
@echo off
cd /d "$($script:Dest)"
set ASPNETCORE_ENVIRONMENT=Production
start "CPCREDO" /D "$($script:Dest)" "$exe"
"@
    Set-Content -LiteralPath (Join-Path $script:Dest "Start-CPCREDO.cmd") -Value $starter -Encoding ASCII

    $tr = "$env:ComSpec /c `"$(Join-Path $script:Dest "Start-CPCREDO.cmd")`""
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    schtasks /Create /TN "CPCREDO" /SC ONSTART /RL HIGHEST /RU SYSTEM /F /TR $tr | Out-Null
    $taskCode = $LASTEXITCODE
    $ErrorActionPreference = $prev
    if ($taskCode -ne 0) { throw "Impossible d'enregistrer le demarrage automatique." }
    Write-InstallLog "TASK" "OK" "tache CPCREDO au demarrage"

    $backupSrc = Join-Path $UsbRoot "backup.ps1"
    if (-not (Test-Path $backupSrc)) { $backupSrc = Join-Path $UsbRoot "deploy\windows\backup.ps1" }
    if (Test-Path $backupSrc) {
        Copy-Item -LiteralPath $backupSrc -Destination (Join-Path $script:Dest "backup.ps1") -Force
        $backupTr = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $script:Dest "backup.ps1")`""
        $autoBackup = $true
        $bakSettingsPath = Join-Path $script:Dest "data\backup-settings.json"
        if (Test-Path $bakSettingsPath) {
            try {
                $bakSaved = Get-Content -LiteralPath $bakSettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
                $autoProp = Get-JsonPath $bakSaved @("autoBackupEnabled")
                if ($null -ne $autoProp) { $autoBackup = [bool]$autoProp }
            } catch { }
        }
        $ErrorActionPreference = "Continue"
        schtasks /Query /TN "CPCREDO-Backup" | Out-Null
        $backupExists = $LASTEXITCODE -eq 0
        if ($backupExists) {
            schtasks /Change /TN "CPCREDO-Backup" /TR $backupTr | Out-Null
        } else {
            schtasks /Create /TN "CPCREDO-Backup" /SC DAILY /ST 18:30 /RL HIGHEST /RU SYSTEM /F /TR $backupTr | Out-Null
        }
        if ($autoBackup) { schtasks /Change /TN "CPCREDO-Backup" /ENABLE | Out-Null }
        else { schtasks /Change /TN "CPCREDO-Backup" /DISABLE | Out-Null }
        $ErrorActionPreference = "Stop"
        Write-InstallLog "BACKUP_TASK" "OK" "18:30"
    }
    else {
        Write-InstallLog "BACKUP_SCRIPT" "INFO" "backup.ps1 absent sur la cle, tache ignoree"
    }

    $ErrorActionPreference = "Continue"
    netsh advfirewall firewall delete rule name="CPCREDO 5080" | Out-Null
    netsh advfirewall firewall delete rule name="CPCREDO 5443" | Out-Null
    netsh advfirewall firewall add rule name="CPCREDO 5443" dir=in action=allow protocol=TCP localport=5443 profile=any | Out-Null
    $fw = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    if ($fw -ne 0) { throw "Impossible d'ouvrir le port reseau 5443." }
    Write-InstallLog "FIREWALL" "OK" "5443"

    $script:AdminOnce = New-AdminOneTimePassword
    $env:ASPNETCORE_ENVIRONMENT = "Production"
    Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue
    $env:Seed__BootstrapAdminPassword = $script:AdminOnce
    try {
        $script:StartedPid = Start-CpcredoHidden -ExePath $exe -WorkDir $script:Dest
        Start-Sleep -Seconds 2
        $running = @(Get-Process -Name "CPCREDO.WebApi" -ErrorAction SilentlyContinue)
        if ((As-Array $running).Count -gt 0) { $script:StartedPid = $running[0].Id }
        Write-InstallLog "START" "OK" ("pid " + $script:StartedPid)
    }
    catch {
        $env:Seed__BootstrapAdminPassword = $null
        throw "Impossible de demarrer CPCREDO."
    }
    $env:Seed__BootstrapAdminPassword = $null

    $up = $false
    for ($n = 1; $n -le 30; $n++) {
        Wait-Ui
        Start-Sleep -Seconds 2
        try {
            $r = Invoke-WebRequest -Uri "http://127.0.0.1:5080/health" -UseBasicParsing -TimeoutSec 3
            if ($r.StatusCode -eq 200) { $up = $true; break }
        } catch { }
    }
    if (-not $up) { throw "L'application n'a pas repondu. Ouvrez C:\CPCREDO\app.log puis reessayez." }
    Write-InstallLog "HEALTH" "OK" "http://127.0.0.1:5080/health"

    $lan = @(Get-LanIPv4)
    $script:PublicUrl = "https://127.0.0.1:5443"
    if ((As-Array $lan).Count -gt 0) { $script:PublicUrl = "https://$($lan[0]):5443" }

    $psql = Get-PsqlPath
    try {
        $userCount = Invoke-Psql $psql "cpcredo" "SELECT COUNT(*) FROM users" $script:PgPort
        if ($userCount -eq "0") { throw "Aucun utilisateur." }
        $script:DbCheckOk = $true
    }
    catch {
        $script:DbCheckOk = $false
        Write-InstallLog "ADMIN_USER" "FAIL" $_.Exception.Message
    }
    $onceFile = Join-Path $script:Dest "data\admin-initial-password.txt"
    if (Test-Path $onceFile) {
        try { Remove-Item -LiteralPath $onceFile -Force } catch { }
        Write-InstallLog "ADMIN_USER" "OK" "fichier mot de passe initial supprime"
    }
    $env:PGPASSWORD = $null
    try { New-DesktopUrlShortcut $script:PublicUrl } catch { }
}

function Complete-FirstRun {
    $lng = "fr"
    if ($script:RadioHt.Checked) { $lng = "ht" }
    $locale = Join-Path $script:Dest "wwwroot\ui-locale.json"
    try {
        $www = Split-Path -Parent $locale
        if (-not (Test-Path $www)) { New-Item -ItemType Directory -Force -Path $www | Out-Null }
        Set-Content -LiteralPath $locale -Value ("{`"lng`":`"$lng`"}") -Encoding ASCII
    } catch { }
    $onceFile = Join-Path $script:Dest "data\admin-initial-password.txt"
    if (Test-Path $onceFile) { try { Remove-Item -LiteralPath $onceFile -Force } catch { } }
    try { Open-CpcredoUrl $script:PublicUrl } catch { }
    Write-InstallLog "DONE" "OK" $script:PublicUrl
    Save-InstallState @{ resume = $false; step = "done"; needReboot = $false }
}

function Invoke-Page4 {
    Show-Page 4
    Set-Progress 10 "Preparation des dossiers..."
    Copy-AppFromUsb
    Set-Progress 40 "Preparation de la base..."
    Ensure-PostgresDatabase
    Set-Progress 60 "Configuration..."
    Write-AppSettings
    Set-Progress 80 "Demarrage..."
    Finish-WindowsIntegration
    Set-Progress 100 "Termine."
    if ($script:DbCheckOk) { $script:DbLabel.Text = "Connexion réussie"; $script:DbLabel.ForeColor = [System.Drawing.Color]::ForestGreen }
    else { $script:DbLabel.Text = "Connexion a verifier (voir le journal)"; $script:DbLabel.ForeColor = [System.Drawing.Color]::DarkOrange }
    if ($script:IsUpdate) {
        $script:AdminLabel.Text = "Les comptes existants sont conserves. Changez le mot de passe a la connexion si demande."
    }
    else {
        $disp = Format-AdminPasswordDisplay $script:AdminOnce
        $script:AdminLabel.Text = "Utilisateur : admin" + [Environment]::NewLine + "Mot de passe (une seule fois) : " + $disp + [Environment]::NewLine + "Saisissez-le sans espace ni tiret. 10 caracteres min. a la premiere connexion."
    }
    Show-Page 5
}

function On-Next {
    try {
        if ($script:Page -eq 1) {
            if (-not (Confirm-InstallLockUi)) { return }
            $ok = Update-CheckList
            Show-Page 2
            if (-not $ok) { $script:BtnNext.Enabled = $false }
        }
        elseif ($script:Page -eq 2) {
            $missing = @(As-Array $script:MissingList)
            if ((As-Array $missing).Count -gt 0) {
                Show-Page 3
                try {
                    Install-MissingComponents
                }
                catch {
                    Fail-Step "COMPONENTS" $_.Exception.Message "Pas de connexion stable. Rebranchez la cle USB complete (dossier OfflinePackages) ou reessayez quand internet sera disponible." $_.Exception.ToString()
                }
            }
            Invoke-Page4
        }
        elseif ($script:Page -eq 5) {
            Complete-FirstRun
            $script:Form.Close()
        }
    }
    catch {
        Fail-Step "INSTALL" $_.Exception.Message $null $_.Exception.ToString()
    }
}

# ---- main ----
$usbLog = Join-Path $UsbRoot "install.log"
Initialize-Log $usbLog

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-InstallLog "ADMIN" "FAIL" "pas administrateur"
    [void][System.Windows.Forms.MessageBox]::Show(
        "Demandez a quelqu'un qui gere cet ordinateur de faire un clic droit sur Installer CPCREDO et choisir Executer en tant qu'administrateur.",
        "CPCREDO")
    exit 1
}
Write-InstallLog "ADMIN" "OK" "session administrateur"

$appSource = Join-Path $UsbRoot "App"
if (-not (Test-Path $appSource)) {
    Fail-Step "APP" "Dossier App introuvable." "Recreez la cle USB avec publish.ps1."
}

New-Item -ItemType Directory -Force -Path $script:Dest | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $script:Dest "logs") | Out-Null
$destLog = Join-Path $script:Dest "logs\install.log"
if ($script:LogPath -ne $destLog -and (Test-Path $script:LogPath)) {
    try { Copy-Item -LiteralPath $script:LogPath -Destination $destLog -Force } catch { }
}
Initialize-Log $destLog
Write-InstallLog "FOLDER" "OK" $script:Dest

if (Test-Path (Join-Path $script:Dest "CPCREDO.WebApi.exe")) {
    $script:IsUpdate = $true
    Write-InstallLog "UPDATE" "INFO" "installation existante, mode mise a jour"
}

$script:Form = New-Object System.Windows.Forms.Form
$script:Form.Text = "Installation de CPCREDO"
$script:Form.Width = 760
$script:Form.Height = 560
$script:Form.StartPosition = "CenterScreen"
$script:Form.FormBorderStyle = "FixedDialog"
$script:Form.MaximizeBox = $false
$script:Form.MinimizeBox = $false
$script:Form.Font = New-UiFont 12

$script:Title = New-Object System.Windows.Forms.Label
$script:Title.Left = 32; $script:Title.Top = 24; $script:Title.Width = 680; $script:Title.Height = 40
$script:Title.Font = New-UiFont 20 $true

$script:Body = New-Object System.Windows.Forms.Label
$script:Body.Left = 32; $script:Body.Top = 80; $script:Body.Width = 680; $script:Body.Height = 90
$script:Body.Font = New-UiFont 14

$script:PasswordBox = New-Object System.Windows.Forms.TextBox
$script:PasswordBox.Left = 32; $script:PasswordBox.Top = 180; $script:PasswordBox.Width = 400; $script:PasswordBox.Height = 36
$script:PasswordBox.UseSystemPasswordChar = $true
$script:PasswordBox.Font = New-UiFont 14

$script:CheckList = New-Object System.Windows.Forms.Label
$script:CheckList.Left = 32; $script:CheckList.Top = 160; $script:CheckList.Width = 680; $script:CheckList.Height = 250
$script:CheckList.Font = New-UiFont 13

$script:Progress = New-Object System.Windows.Forms.ProgressBar
$script:Progress.Left = 32; $script:Progress.Top = 220; $script:Progress.Width = 680; $script:Progress.Height = 28
$script:Progress.Minimum = 0; $script:Progress.Maximum = 100

$script:ProgressLabel = New-Object System.Windows.Forms.Label
$script:ProgressLabel.Left = 32; $script:ProgressLabel.Top = 258; $script:ProgressLabel.Width = 680; $script:ProgressLabel.Height = 40
$script:ProgressLabel.Font = New-UiFont 13

$script:RadioFr = New-Object System.Windows.Forms.RadioButton
$script:RadioFr.Text = "Français"
$script:RadioFr.Left = 32; $script:RadioFr.Top = 170; $script:RadioFr.Width = 160; $script:RadioFr.Checked = $true
$script:RadioHt = New-Object System.Windows.Forms.RadioButton
$script:RadioHt.Text = "Kreyòl"
$script:RadioHt.Left = 200; $script:RadioHt.Top = 170; $script:RadioHt.Width = 160

$script:AdminLabel = New-Object System.Windows.Forms.Label
$script:AdminLabel.Left = 32; $script:AdminLabel.Top = 210; $script:AdminLabel.Width = 680; $script:AdminLabel.Height = 90
$script:AdminLabel.Font = New-UiFont 13

$script:DbLabel = New-Object System.Windows.Forms.Label
$script:DbLabel.Left = 32; $script:DbLabel.Top = 310; $script:DbLabel.Width = 680; $script:DbLabel.Height = 36
$script:DbLabel.Font = New-UiFont 14 $true

$script:BtnBack = New-Object System.Windows.Forms.Button
$script:BtnBack.Text = "Retour"
$script:BtnBack.Left = 32; $script:BtnBack.Top = 450; $script:BtnBack.Width = 160; $script:BtnBack.Height = 48
$script:BtnBack.Add_Click({ if ($script:Page -eq 2) { Show-Page 1 } })

$script:BtnNext = New-Object System.Windows.Forms.Button
$script:BtnNext.Text = "Suivant"
$script:BtnNext.Left = 540; $script:BtnNext.Top = 450; $script:BtnNext.Width = 170; $script:BtnNext.Height = 48
$script:BtnNext.Font = New-UiFont 14 $true
$script:BtnNext.Add_Click({ On-Next })

$script:Form.Controls.AddRange(@(
    $script:Title, $script:Body, $script:PasswordBox, $script:CheckList,
    $script:Progress, $script:ProgressLabel, $script:RadioFr, $script:RadioHt,
    $script:AdminLabel, $script:DbLabel, $script:BtnBack, $script:BtnNext
))
$script:Form.AcceptButton = $script:BtnNext

$state = Read-InstallState
if ($state -and $state.resume -eq $true) {
    Write-InstallLog "RESUME" "OK" ([string]$state.step)
    Show-Page 2
    [void](Update-CheckList)
    $script:Form.Add_Shown({
        try {
            $missing = @(As-Array $script:MissingList)
            if ((As-Array $missing).Count -gt 0) {
                Show-Page 3
                Install-MissingComponents
            }
            Invoke-Page4
        }
        catch { Fail-Step "RESUME" $_.Exception.Message $null $_.Exception.ToString() }
    })
}
else {
    Show-Page 1
}

[void]$script:Form.ShowDialog()
