#Requires -Version 5.1
param(
    [string]$OutDir = "",
    [Security.SecureString]$InstallPassword
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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

function ConvertTo-Plain {
    param([Security.SecureString]$Secure)
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

$installerDir = $PSScriptRoot
$repoRoot = Split-Path $installerDir -Parent
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $repoRoot "CPCREDO-USB"
}

if (-not $InstallPassword) {
    $p1 = Read-Host "Mot de passe d'installation (USB)" -AsSecureString
    $p2 = Read-Host "Confirmer le mot de passe d'installation" -AsSecureString
    $a = ConvertTo-Plain $p1
    $b = ConvertTo-Plain $p2
    if ($a -ne $b -or $a.Length -lt 8) {
        Write-Host "Les mots de passe ne correspondent pas, ou trop courts (min. 8)." -ForegroundColor Red
        exit 1
    }
    $plain = $a
}
else {
    $plain = ConvertTo-Plain $InstallPassword
}

$hash = Get-InstallPasswordHash $plain
$plain = $null

$dotnet = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

Write-Host "Compilation de l'interface..."
Push-Location (Join-Path $repoRoot "src\CPECDO.Web")
try {
    if (-not (Test-Path "node_modules")) { npm install }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build a echoue." }
}
finally { Pop-Location }

$publishDir = Join-Path $repoRoot "artifacts\usb-publish"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

Write-Host "Publication de l'API (win-x64, framework-dependent)..."
& $dotnet publish (Join-Path $repoRoot "src\CPCREDO.WebApi\CPCREDO.WebApi.csproj") `
    -c Release -r win-x64 --self-contained false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish a echoue." }

$wwwroot = Join-Path $publishDir "wwwroot"
New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
Copy-Item -Path (Join-Path $repoRoot "src\CPECDO.Web\dist\*") -Destination $wwwroot -Recurse -Force
$bakedHost = Get-ChildItem -Path $wwwroot -Recurse -Include *.js,*.html | Select-String -Pattern "localhost:5080|127\.0\.0\.1:5080|VITE_API_URL"
if ($bakedHost) {
    throw "wwwroot contains a baked API host. Production must use relative /api URLs only."
}

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
$appDest = Join-Path $OutDir "App"
$templates = Join-Path $OutDir "Templates"
New-Item -ItemType Directory -Force -Path $appDest | Out-Null
New-Item -ItemType Directory -Force -Path $templates | Out-Null

Copy-Item -Path (Join-Path $publishDir "*") -Destination $appDest -Recurse -Force
$devSettings = Join-Path $appDest "appsettings.Development.json"
if (Test-Path $devSettings) { Remove-Item -LiteralPath $devSettings -Force }
Copy-Item -Path (Join-Path $installerDir "Templates\appsettings.Production.json") -Destination (Join-Path $appDest "appsettings.json") -Force
Copy-Item -Path (Join-Path $installerDir "INSTALLER-SERVEUR.bat") -Destination $OutDir -Force
Copy-Item -Path (Join-Path $installerDir "INSTALLER-CLIENT.bat") -Destination $OutDir -Force
Copy-Item -Path (Join-Path $installerDir "Setup-Serveur.ps1") -Destination $OutDir -Force
Copy-Item -Path (Join-Path $installerDir "Setup-Client.ps1") -Destination $OutDir -Force
Copy-Item -Path (Join-Path $installerDir "README.txt") -Destination $OutDir -Force
Copy-Item -Path (Join-Path $installerDir "Templates\appsettings.Production.json") -Destination $templates -Force
Set-Content -LiteralPath (Join-Path $templates "install.lock") -Value $hash -Encoding ASCII -NoNewline

Write-Host ""
Write-Host "Cle USB assemblee : $OutDir"
Write-Host "Le serveur double-clique INSTALLER-SERVEUR.bat (administrateur)."
Write-Host "Le caissier double-clique INSTALLER-CLIENT.bat."
Write-Host "Le mot de passe d'installation n'est pas stocke ; seul le hash SHA-256 est dans Templates\install.lock."
