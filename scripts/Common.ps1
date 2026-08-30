Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RepositoryRoot {
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
}

function Initialize-BuildEnvironment {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw '.NET 8 SDK is not installed or dotnet is not on PATH.'
    }
    if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
        throw 'Python 3.11+ is not installed or python is not on PATH.'
    }

    $cliHome = Join-Path $RepositoryRoot '.dotnet-cli'
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

    $pythonVersionText = (& python --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $pythonVersionText -notmatch 'Python\s+(\d+)\.(\d+)') {
        throw 'Unable to read the Python version.'
    }
    $pythonMajor = [int]$Matches[1]
    $pythonMinor = [int]$Matches[2]
    if ($pythonMajor -lt 3 -or ($pythonMajor -eq 3 -and $pythonMinor -lt 11)) {
        throw "The build requires Python 3.11 or newer; found $pythonVersionText."
    }

    Write-Host "Environment: .NET SDK $sdkVersion / $pythonVersionText"
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [Parameter(Mandatory = $true)][string]$Description
    )

    Write-Host "`n==> $Description" -ForegroundColor Cyan
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Candidate
    )

    $directorySeparators = [char[]]@(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $resolvedParent = [System.IO.Path]::GetFullPath($Parent).TrimEnd($directorySeparators)
    $resolvedCandidate = [System.IO.Path]::GetFullPath($Candidate)
    $prefix = $resolvedParent + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedCandidate.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside the repository target directory: $resolvedCandidate"
    }
    return $resolvedCandidate
}
