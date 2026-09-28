# AeroHub

AeroHub is planned as an aviation SDR and aircraft-message analysis application. The app will support two equal ingestion paths:

1. Native decoding from signal sources handled by the .NET backend.
2. Import of already-decoded data from other applications, normalized through the same backend pipeline.

The first implementation target is a backend/frontend architecture:

- .NET backend for hardware access, DSP, decoder modules, import adapters, normalization, storage, and real-time APIs.
- React + TypeScript frontend for operator workflow, visualization, message inspection, aircraft tracking, imports, and settings.

## Current Plans

- [AeroHubPlan.md](AeroHubPlan.md) - architecture and feature plan.
- [AeroHubSprintPlan.md](AeroHubSprintPlan.md) - sprint-based delivery plan.
- [docs/architecture.md](docs/architecture.md) - Sprint 0 architecture decisions and boundaries.
- [docs/api-contract.md](docs/api-contract.md) - starter API and live-stream contract notes.
- [docs/performance-budgets.md](docs/performance-budgets.md) - initial optimization and reliability budgets.
- [docs/decisions.md](docs/decisions.md) - editable project decision log.
- [docs/local-frequencies.md](docs/local-frequencies.md) - local monitoring frequencies and source-profile seeds.
- [docs/acars-parser.md](docs/acars-parser.md) - Sprint 3 ACARS parser behavior and fixture notes.
- [docs/decoded-data-imports.md](docs/decoded-data-imports.md) - Sprint 4 decoded-data import behavior and quarantine notes.
- [docs/synthetic-streams.md](docs/synthetic-streams.md) - Sprint 5 synthetic RF stream contracts and replay budgets.
- [docs/adsb-tracks.md](docs/adsb-tracks.md) - Sprint 6 ADS-B/readsb import and aircraft-track behavior.
- [docs/external-decoder-adapters.md](docs/external-decoder-adapters.md) - Sprint 7 dumphfdl/dumpvdl2 adapter and process supervision behavior.
- [docs/datalink-satcom-models.md](docs/datalink-satcom-models.md) - Sprint 8 CPDLC/ADS-C/SATCOM detail models.
- [docs/wefax-image-decoder.md](docs/wefax-image-decoder.md) - Sprint 9 WEFAX synthetic image decoder skeleton.
- [docs/storage-retention.md](docs/storage-retention.md) - Sprint 10 JSONL storage, retention, export, and replay behavior.
- [docs/hardware-source-adapters.md](docs/hardware-source-adapters.md) - Sprint 11 simulator-first file/audio and rtl_tcp source adapters.
- [docs/release-candidate.md](docs/release-candidate.md) - Sprint 12 release-candidate packaging and operator diagnostics notes.

## Planned Repository Layout

```text
src/
  backend/
    AeroHub.Api/
    AeroHub.Contracts/
    AeroHub.Core/
    AeroHub.Decoders/
    AeroHub.Infrastructure/
  frontend/
    AeroHub.Web/
tests/
fixtures/
docs/
```

## Provisional Tooling

- Backend: .NET 8 or later, ASP.NET Core, xUnit.
- Frontend: React, TypeScript, Vite.
- Real-time transport: SignalR first for control/events; binary WebSocket only if spectrum/waterfall profiling requires it.
- Development mode: separate backend and frontend processes.
- Packaged mode: backend serves the React build unless a later desktop shell requirement appears.

## Build Commands

```powershell
dotnet restore .\src\backend\AeroHub.slnx
dotnet build .\src\backend\AeroHub.slnx
dotnet test .\src\backend\AeroHub.slnx
Set-Location .\src\frontend\AeroHub.Web
npm install
npm run typecheck
npm run lint
npm run build
```

## Windows Installer

Install [Inno Setup](https://jrsoftware.org/isinfo.php), then run this from the repository root:

```powershell
.\scripts\package-installer.ps1
```

The script publishes a self-contained `win-x64` build, embeds the React build in the API host, and creates `release/installer/setup/AeroHub-Setup.exe`. The installer is per-user and creates Start Menu and optional desktop shortcuts. AeroHub listens on `http://localhost:5157` after launch.

## Local Development

Run the backend from the repository root:

```powershell
dotnet run --project .\src\backend\AeroHub.Api\AeroHub.Api.csproj --urls http://localhost:5157
```

Run the frontend from `src/frontend/AeroHub.Web`:

```powershell
npm run dev -- --host 127.0.0.1
```

The frontend dev server proxies `/api` to `http://localhost:5157`.
It also proxies `/hubs` to the backend for SignalR development traffic.