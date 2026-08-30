[CmdletBinding()]
param(
    [ValidatePattern('^[0-9A-Za-z][0-9A-Za-z._-]*$')]
    [string]$Version = '0.3.0',

    [switch]$SkipVerify,

    [switch]$DryRun
)

. (Join-Path $PSScriptRoot 'Common.ps1')

$repositoryRoot = Get-RepositoryRoot
Initialize-BuildEnvironment -RepositoryRoot $repositoryRoot
$distRoot = Assert-ChildPath -Parent $repositoryRoot -Candidate (Join-Path $repositoryRoot 'dist')
$packageName = "DesktopPet-$Version-win-x64"
$publishDirectory = Assert-ChildPath -Parent $distRoot -Candidate (Join-Path $distRoot $packageName)
$zipPath = Assert-ChildPath -Parent $distRoot -Candidate (Join-Path $distRoot "$packageName.zip")
$projectPath = Join-Path $repositoryRoot 'src/DesktopPet.App/DesktopPet.App.csproj'

Write-Host 'Publish configuration:'
Write-Host "  Project: $projectPath"
Write-Host '  Target: win-x64 / framework-dependent / Release'
Write-Host "  Directory: $publishDirectory"
Write-Host "  ZIP : $zipPath"

if ($DryRun) {
    Write-Host "`nDry-run complete: no files were removed, published, or archived." -ForegroundColor Green
    exit 0
}

Push-Location $repositoryRoot
try {
    if (-not $SkipVerify) {
        & (Join-Path $PSScriptRoot 'verify.ps1')
        if (-not $?) {
            throw 'Pre-publish verification failed.'
        }
    }

    [System.IO.Directory]::CreateDirectory($distRoot) | Out-Null
    if ([System.IO.Directory]::Exists($publishDirectory)) {
        [System.IO.Directory]::Delete($publishDirectory, $true)
    }
    if ([System.IO.File]::Exists($zipPath)) {
        [System.IO.File]::Delete($zipPath)
    }

    Invoke-NativeCommand -FilePath 'dotnet' -Description 'Publish Windows x64 portable directory' -ArgumentList @(
        'publish',
        'src/DesktopPet.App/DesktopPet.App.csproj',
        '--configuration',
        'Release',
        '--runtime',
        'win-x64',
        '--self-contained',
        'false',
        '--output',
        $publishDirectory,
        '-p:DebugType=None',
        '-p:DebugSymbols=false'
    )

    $requiredFiles = @(
        (Join-Path $publishDirectory 'DesktopPet.exe'),
        (Join-Path $publishDirectory 'DesktopPet.dll'),
        (Join-Path $publishDirectory 'DesktopPet.runtimeconfig.json'),
        (Join-Path $publishDirectory 'assets/manifest/assets.json'),
        (Join-Path $publishDirectory 'assets/manifest/animations.json')
    )
    foreach ($requiredFile in $requiredFiles) {
        if (-not [System.IO.File]::Exists($requiredFile)) {
            throw "Published output is missing a required file: $requiredFile"
        }
    }
    $publishedSprites = [System.IO.Directory]::GetFiles(
        (Join-Path $publishDirectory 'assets/sprites'), '*.png').Count
    if ($publishedSprites -ne 18) {
        throw "Published output must contain 18 pose sprites; found $publishedSprites."
    }
    $publishedAnimationFrames = [System.IO.Directory]::GetFiles(
        (Join-Path $publishDirectory 'assets/animations'),
        '*.png',
        [System.IO.SearchOption]::AllDirectories).Count
    if ($publishedAnimationFrames -ne 160) {
        throw "Published output must contain 160 animation frames; found $publishedAnimationFrames."
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::Open(
        $zipPath,
        [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $directorySeparators = [char[]]@(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar)
        $publishPrefix = $publishDirectory.TrimEnd($directorySeparators) +
            [System.IO.Path]::DirectorySeparatorChar
        $files = [System.IO.Directory]::GetFiles(
            $publishDirectory,
            '*',
            [System.IO.SearchOption]::AllDirectories) | Sort-Object
        foreach ($file in $files) {
            $relativePath = $file.Substring($publishPrefix.Length).Replace('\', '/')
            $entry = $archive.CreateEntry($relativePath, [System.IO.Compression.CompressionLevel]::Optimal)
            # Fixed timestamp avoids metadata-only ZIP changes between identical builds.
            $entry.LastWriteTime = [System.DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [System.TimeSpan]::Zero)
            $inputStream = [System.IO.File]::OpenRead($file)
            $outputStream = $entry.Open()
            try {
                $inputStream.CopyTo($outputStream)
            }
            finally {
                $outputStream.Dispose()
                $inputStream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    $zipSize = [Math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 2)
    Write-Host "`nPublish complete: $zipPath ($zipSize MiB)" -ForegroundColor Green
    Write-Host 'The target computer requires .NET 8 Desktop Runtime (x64).'
}
finally {
    Pop-Location
}
