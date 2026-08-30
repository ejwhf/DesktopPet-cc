[CmdletBinding()]
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.3.0',

    [string]$PayloadZip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'src\DesktopPet.Setup'))
$projectPath = Join-Path $projectRoot 'DesktopPet.Setup.csproj'
$distRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'dist'))

if ([string]::IsNullOrWhiteSpace($PayloadZip)) {
    $PayloadZip = Join-Path $distRoot "DesktopPet-$Version-win-x64.zip"
}
$PayloadZip = [System.IO.Path]::GetFullPath($PayloadZip)

$distPrefix = $distRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $PayloadZip.StartsWith($distPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Payload ZIP must be inside the repository dist directory: $PayloadZip"
}
if (-not [System.IO.File]::Exists($PayloadZip)) {
    throw "Payload ZIP was not found: $PayloadZip"
}
if ([System.IO.Path]::GetExtension($PayloadZip) -ne '.zip') {
    throw "Payload must be a ZIP file: $PayloadZip"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 8 SDK is not installed or dotnet is not on PATH.'
}

$cliHome = Join-Path $repositoryRoot '.dotnet-cli'
[System.IO.Directory]::CreateDirectory($cliHome) | Out-Null
$env:DOTNET_CLI_HOME = $cliHome
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$sdkVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sdkVersion)) {
    throw 'Unable to read the .NET SDK version.'
}
$sdkMajor = 0
if (-not [int]::TryParse(($sdkVersion -split '\.')[0], [ref]$sdkMajor) -or $sdkMajor -lt 8) {
    throw "The build requires .NET 8 SDK or newer; found $sdkVersion."
}

$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'bin\installer-publish'))
$projectPrefix = $projectRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $publishDirectory.StartsWith($projectPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to publish outside the installer project: $publishDirectory"
}

if ([System.IO.Directory]::Exists($publishDirectory)) {
    [System.IO.Directory]::Delete($publishDirectory, $true)
}
[System.IO.Directory]::CreateDirectory($publishDirectory) | Out-Null
[System.IO.Directory]::CreateDirectory($distRoot) | Out-Null

$outputPath = Join-Path $distRoot "DesktopPet-Setup-$Version-win-x64.exe"

Write-Host "Building DesktopPet installer $Version"
Write-Host "  Payload: $PayloadZip"
Write-Host "  Output : $outputPath"

& dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $publishDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    "-p:Version=$Version" `
    "-p:PayloadZip=$PayloadZip"
if ($LASTEXITCODE -ne 0) {
    throw "Installer publish failed with exit code $LASTEXITCODE."
}

$publishedExecutable = Join-Path $publishDirectory 'DesktopPet-Setup.exe'
if (-not [System.IO.File]::Exists($publishedExecutable)) {
    throw "Published installer executable is missing: $publishedExecutable"
}

[System.IO.File]::Copy($publishedExecutable, $outputPath, $true)
$hash = Get-FileHash -LiteralPath $outputPath -Algorithm SHA256
$sizeMiB = [Math]::Round((Get-Item -LiteralPath $outputPath).Length / 1MB, 2)

Write-Host "`nInstaller complete: $outputPath ($sizeMiB MiB)" -ForegroundColor Green
Write-Host "SHA256: $($hash.Hash)"
Write-Host 'This framework-dependent installer requires .NET 8 Desktop Runtime (x64).'
