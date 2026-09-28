param(
    [string]$Configuration = 'Release',
    [string]$OutputRoot = 'release/aerohub-rc'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$backendProject = Join-Path $repoRoot 'src/backend/AeroHub.Api/AeroHub.Api.csproj'
$frontendRoot = Join-Path $repoRoot 'src/frontend/AeroHub.Web'
$outputPath = Join-Path $repoRoot $OutputRoot

Write-Host "Building backend..."
dotnet build $backendProject --configuration $Configuration --nologo

Write-Host "Building frontend..."
Set-Location $frontendRoot
npm install
npm run build

Write-Host "Packaging release bundle..."
Set-Location $repoRoot
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $outputPath 'backend') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $outputPath 'frontend') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $outputPath 'docs') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $outputPath 'fixtures') | Out-Null
Copy-Item -Recurse -Force (Join-Path $repoRoot 'src/backend/AeroHub.Api/bin/Release/net8.0/*') (Join-Path $outputPath 'backend')
Copy-Item -Recurse -Force (Join-Path $frontendRoot 'dist/*') (Join-Path $outputPath 'frontend')
Copy-Item -Recurse -Force (Join-Path $repoRoot 'docs') (Join-Path $outputPath 'docs')
Copy-Item -Recurse -Force (Join-Path $repoRoot 'fixtures') (Join-Path $outputPath 'fixtures')
Copy-Item -Force (Join-Path $repoRoot 'README.md') (Join-Path $outputPath 'README.md')

Write-Host "Release bundle created at: $outputPath"
