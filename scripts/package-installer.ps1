param(
    [string]$Configuration = 'Release',
    [string]$OutputRoot = 'release/installer',
    [string]$Runtime = 'win-x64',
    [switch]$SkipInstallerCompiler
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$backendProject = Join-Path $repoRoot 'src/backend/AeroHub.Api/AeroHub.Api.csproj'
$frontendRoot = Join-Path $repoRoot 'src/frontend/AeroHub.Web'
$outputPath = Join-Path $repoRoot $OutputRoot
$publishPath = Join-Path $outputPath 'publish'

Set-Location $frontendRoot
npm install
npm run build

Set-Location $repoRoot
if (Test-Path $outputPath) {
    Remove-Item -Recurse -Force $outputPath
}
New-Item -ItemType Directory -Force -Path $publishPath | Out-Null

dotnet publish $backendProject `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $publishPath `
    --nologo

New-Item -ItemType Directory -Force -Path (Join-Path $publishPath 'wwwroot') | Out-Null
Copy-Item -Recurse -Force (Join-Path $frontendRoot 'dist/*') (Join-Path $publishPath 'wwwroot')

if (-not $SkipInstallerCompiler) {
    $iscc = Get-Command iscc -ErrorAction SilentlyContinue
    if (-not $iscc) {
        $innoCandidates = @(
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe')
        )
        $innoPath = $innoCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($innoPath) {
            $iscc = Get-Item $innoPath
        }
    }
    if (-not $iscc) {
        throw 'Inno Setup Compiler (iscc.exe) was not found. Install Inno Setup or rerun with -SkipInstallerCompiler.'
    }

    & $iscc.Source (Join-Path $repoRoot 'installer/AeroHub.iss')
}

Write-Host "Published application: $publishPath"
if (-not $SkipInstallerCompiler) {
    Write-Host "Installer created in: $(Join-Path $outputPath 'setup')"
}