# AeroHub

AeroHub is an aviation SDR and aircraft-message analysis application. Its operator console brings signal-source status, decoded aviation messages, aircraft and radiosonde tracks, import health, and storage operations together in one local web interface.

The .NET backend owns ingestion, decoding and import adapters, normalization, storage, and real-time updates. The React + TypeScript frontend presents that data and provides operator controls. Native signal processing and already-decoded data imports are designed to feed the same normalized message and track workflows while retaining source provenance and diagnostics.

## Operator Console

- **Live RF:** View spectrum and waterfall displays, stream metrics, source health, and adapter diagnostics. Synthetic replay controls exercise the visualization at 1x, 5x, and 20x speeds.
- **Messages:** Review recent normalized aviation messages, filter by confidence or warnings, inspect parsed ACARS and datalink fields, and copy the original payload. Message details retain transport, frequency, source, and parser warnings when available.
- **Aircraft map and tracks:** Browse aircraft and radiosonde positions alongside nearby radio navigation aids. Filter aircraft by class, altitude, position availability, and update age; select an aircraft to inspect it, follow its map position, and view its recent track. Export filtered aircraft data as CSV or GeoJSON.
- **Imports:** Run the included ACARS, ADS-B/readsb, SATCOM, radiosonde, dumphfdl, and dumpvdl2 sample imports. Accepted and quarantined record counts, source provenance, and import diagnostics are surfaced in the console. A dump1090/readsb network connection can also be configured and monitored.
- **Decoder and source operations:** Inspect decoder/import state, configure available source and frequency profiles, and view hardware-source and external-decoder health. Adapter diagnostics and simulated failure/reconnect scenarios help exercise operational status handling.
- **WEFAX:** Inspect image lines and sync status, adjust polarity and slant, and replay complete or partial sample transmissions.
- **Settings and storage:** Persist decoder, frequency, import, and feeder settings; inspect storage counts and health; export a data snapshot; and apply configured retention operations.
- **Live status:** Backend health, SignalR connectivity, warnings, queue metrics, import activity, and recent operator actions are visible alongside the feature panels.

## Current Scope

The console and backend workflows are implemented, but not every control represents a completed live RF decoder. Spectrum/waterfall and WEFAX replay are synthetic. The current hardware-source manager and external-decoder lifecycle controls include simulated behavior; connecting a source in the UI should not be taken as proof of live demodulation. Several import buttons load repository sample fixtures. Check the source state, diagnostics, and provenance shown in AeroHub when evaluating a particular data path.

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