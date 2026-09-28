# Release Candidate Notes

## Release model

AeroHub currently targets a local dev-first deployment model:

- backend: ASP.NET Core API in `src/backend/AeroHub.Api`
- frontend: Vite React app in `src/frontend/AeroHub.Web`
- runtime contract: API + SignalR hub on localhost, frontend served separately in development, packaged build served by backend in a future packaged deployment

This keeps the project easy to validate while still exposing a production-style runtime contract for release candidate testing.

## Packaging flow

Use the following PowerShell flow from the repository root:

```powershell
Set-Location d:\source\repos\AeroHub

# 1. Build backend
 dotnet build .\src\backend\AeroHub.slnx --configuration Release --nologo

# 2. Build frontend
 Set-Location .\src\frontend\AeroHub.Web
 npm install
 npm run build

# 3. Copy the build output into a release folder
 Set-Location d:\source\repos\AeroHub
 New-Item -ItemType Directory -Force -Path .\release\aerohub-rc | Out-Null
 Copy-Item -Recurse -Force .\src\backend\AeroHub.Api\bin\Release\net8.0\* .\release\aerohub-rc\backend\
 Copy-Item -Recurse -Force .\src\frontend\AeroHub.Web\dist\* .\release\aerohub-rc\frontend\
 Copy-Item -Recurse -Force .\docs .\release\aerohub-rc\docs\
 Copy-Item -Recurse -Force .\fixtures .\release\aerohub-rc\fixtures\
 Copy-Item -Force .\README.md .\release\aerohub-rc\README.md
```

## Release-candidate checks

The release candidate should include:

- backend build succeeded in Release mode
- frontend asset build succeeded
- docs and fixtures shipped with the bundle
- API health and operator diagnostics endpoints respond on the deployed build
- SignalR remains connected after a browser reload
- storage snapshot export and retention remain operational

## Operator diagnostics

The backend exposes `/api/diagnostics/operator` and reports:

- backend service name + version + environment
- active decoders and active imports
- stream queue depth
- warning count and recent warning codes
- storage usage in bytes
- overall system health summary

This is intended for an operator dashboard and for release-candidate smoke checks.
