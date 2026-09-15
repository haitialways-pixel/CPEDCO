#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"

$Root = $PSScriptRoot
$LogPath = Join-Path $Root "backup.log"
$LastPath = Join-Path $Root "data\backup-last.json"

function Write-BackupLog {
    param([string]$Status, [string]$Detail)
    $line = "{0:yyyy-MM-dd HH:mm:ss}  {1}  {2}" -f (Get-Date), $Status, $Detail
    try { Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8 } catch { }
    Write-Host $line
}

function Write-LastResult {
    param([bool]$Ok, [string]$DumpFileName, [string]$ErrorText)
    $dir = Split-Path $LastPath -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $obj = @{
        ok = $Ok
        dumpFileName = $DumpFileName
        error = $ErrorText
        atUtc = [DateTime]::UtcNow.ToString("o")
    }
    try {
        $json = ($obj | ConvertTo-Json -Compress)
        Set-Content -LiteralPath $LastPath -Value $json -Encoding UTF8
    } catch { }
}

function Fail-Backup {
    param([string]$Message)
    Write-BackupLog "FAIL" $Message
    Write-LastResult $false "" $Message
    exit 1
}

function Get-ConnPart {
    param([string]$Conn, [string]$Key)
    foreach ($p in $Conn.Split(";")) {
        $kv = $p.Split("=", 2)
        if ($kv.Count -eq 2 -and $kv[0].Trim() -eq $Key) { return $kv[1].Trim() }
    }
    return ""
}

function Find-PgDump {
    param([string]$Configured)
    if (-not [string]::IsNullOrWhiteSpace($Configured)) {
        if (Test-Path $Configured -PathType Container) {
            $nested = Join-Path $Configured "pg_dump.exe"
            if (Test-Path $nested) { return $nested }
        }
        if (Test-Path $Configured -PathType Leaf) { return $Configured }
    }
    $cmd = Get-Command pg_dump.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($ver in @("16", "17", "15")) {
        $c = Join-Path $env:ProgramFiles "PostgreSQL\$ver\bin\pg_dump.exe"
        if (Test-Path $c) { return $c }
    }
    return $null
}

try {
    $prod = Join-Path $Root "appsettings.Production.json"
    $app = Join-Path $Root "appsettings.json"
    $settingsFile = $prod
    if (-not (Test-Path $settingsFile)) { $settingsFile = $app }
    if (-not (Test-Path $settingsFile)) { Fail-Backup "appsettings.Production.json introuvable dans $Root." }

    $cfg = Get-Content -LiteralPath $settingsFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $conn = [string]$cfg.ConnectionStrings.Default
    if ([string]::IsNullOrWhiteSpace($conn)) { Fail-Backup "Chaine de connexion absente." }

    $folder = "C:\CPCREDO\backups"
    $pgDumpPath = ""
    $lockSettings = Join-Path $Root "data\backup-settings.json"
    if (Test-Path $lockSettings) {
        $saved = Get-Content -LiteralPath $lockSettings -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($saved.folder) { $folder = [string]$saved.folder }
        if ($saved.pgDumpPath) { $pgDumpPath = [string]$saved.pgDumpPath }
    }
    if ($cfg.Backup -and $cfg.Backup.Folder -and [string]::IsNullOrWhiteSpace($pgDumpPath) -eq $false) { }
    if ($cfg.Backup -and $cfg.Backup.Folder -and -not (Test-Path $lockSettings)) {
        if ($cfg.Backup.Folder) { $folder = [string]$cfg.Backup.Folder }
        if ($cfg.Backup.PgDumpPath) { $pgDumpPath = [string]$cfg.Backup.PgDumpPath }
    }

    if (-not (Test-Path $folder)) { New-Item -ItemType Directory -Force -Path $folder | Out-Null }

    $pgDump = Find-PgDump $pgDumpPath
    if (-not $pgDump) {
        Fail-Backup "pg_dump introuvable. Indiquez le dossier bin de PostgreSQL (ex. C:\Program Files\PostgreSQL\16\bin) dans Administration > Sauvegarde."
    }

    $hostName = Get-ConnPart $conn "Host"
    $port = Get-ConnPart $conn "Port"
    $database = Get-ConnPart $conn "Database"
    $user = Get-ConnPart $conn "Username"
    $pass = Get-ConnPart $conn "Password"
    if ([string]::IsNullOrWhiteSpace($hostName)) { $hostName = "localhost" }
    if ([string]::IsNullOrWhiteSpace($port)) { $port = "5432" }
    if ([string]::IsNullOrWhiteSpace($database)) { $database = "cpcredo" }

    $stamp = Get-Date -Format "yyyyMMdd-HHmm"
    $dumpName = "cpcredo-$stamp.dump"
    $dumpPath = Join-Path $folder $dumpName
    $env:PGPASSWORD = $pass
    $dumpArgs = @("-h", $hostName, "-p", $port, "-U", $user, "-d", $database, "-Fc", "-f", $dumpPath)
    & $pgDump @dumpArgs
    $code = $LASTEXITCODE
    $env:PGPASSWORD = $null
    if ($code -ne 0 -or -not (Test-Path $dumpPath)) {
        Fail-Backup "pg_dump a echoue (code $code)."
    }

    $kycRoot = Join-Path $Root "data\kyc"
    if (Test-Path $kycRoot) {
        $kycItems = @(Get-ChildItem -LiteralPath $kycRoot -Force -ErrorAction SilentlyContinue)
        if ($kycItems.Count -gt 0) {
            $zipName = "cpcredo-kyc-$stamp.zip"
            $zipPath = Join-Path $folder $zipName
            if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
            Compress-Archive -Path (Join-Path $kycRoot "*") -DestinationPath $zipPath -Force -ErrorAction SilentlyContinue
        }
    }

    $dumps = @(Get-ChildItem -LiteralPath $folder -Filter "cpcredo-*.dump" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime)
    $cutoff = (Get-Date).AddDays(-14)
    while ($dumps.Count -gt 7) {
        $oldest = $dumps[0]
        if ($oldest.LastWriteTime -gt $cutoff) { break }
        $kycOld = Join-Path $folder ("cpcredo-kyc-" + $oldest.Name.Substring("cpcredo-".Length))
        $kycOld = [IO.Path]::ChangeExtension($kycOld, ".zip")
        Remove-Item -LiteralPath $oldest.FullName -Force -ErrorAction SilentlyContinue
        if (Test-Path $kycOld) { Remove-Item -LiteralPath $kycOld -Force -ErrorAction SilentlyContinue }
        if ($dumps.Count -le 1) { break }
        $dumps = @($dumps | Select-Object -Skip 1)
    }

    Write-BackupLog "OK" $dumpName
    Write-LastResult $true $dumpName $null
    exit 0
}
catch {
    Fail-Backup $_.Exception.Message
}
