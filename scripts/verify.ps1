[CmdletBinding()]
param(
    [switch]$SkipBuild
)

. (Join-Path $PSScriptRoot 'Common.ps1')

$repositoryRoot = Get-RepositoryRoot
Initialize-BuildEnvironment -RepositoryRoot $repositoryRoot
Push-Location $repositoryRoot
try {
    Invoke-NativeCommand -FilePath 'python' -Description 'Lint production asset manifest and 18 sprites' -ArgumentList @(
        'tools/asset_pipeline.py',
        'lint',
        '--asset-root',
        'assets'
    )

    Invoke-NativeCommand -FilePath 'python' -Description 'Lint 20 animation clips and 160 frames' -ArgumentList @(
        'tools/animation_pipeline.py',
        'lint',
        '--asset-root',
        'assets'
    )

    Invoke-NativeCommand -FilePath 'python' -Description 'Run Python asset-tool tests' -ArgumentList @(
        '-m',
        'unittest',
        'discover',
        '-s',
        'tests',
        '-v'
    )

    Invoke-NativeCommand -FilePath 'dotnet' -Description 'Run Core manifest and timeline smoke test' -ArgumentList @(
        'run',
        '--project',
        'src/DesktopPet.Core/Smoke/DesktopPet.Core.Smoke.csproj',
        '--configuration',
        'Release',
        '--',
        'assets/manifest/assets.json',
        'assets/manifest/animations.json'
    )

    if (-not $SkipBuild) {
        Invoke-NativeCommand -FilePath 'dotnet' -Description 'Build Release solution' -ArgumentList @(
            'build',
            'DesktopPet.sln',
            '--configuration',
            'Release'
        )
    }

    Write-Host "`nVerification complete: all checks passed." -ForegroundColor Green
}
finally {
    Pop-Location
}
