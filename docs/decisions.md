# AeroHub Decisions

This file records editable project decisions made before implementation.

## Sprint 0 Decisions

| Decision | Current Choice | Status |
|---|---|---|
| Application name | AeroHub | Accepted |
| Architecture | .NET backend + React/TypeScript frontend | Accepted |
| Backend target | .NET 8 or later | Provisional |
| Frontend scaffold | Vite + React + TypeScript | Provisional |
| Test framework | xUnit for backend | Provisional |
| Development mode | Separate backend and frontend processes | Provisional |
| Packaged mode | Backend serves React build | Provisional |
| Primary real-time channel | SignalR for command/status/messages/tracks | Provisional |
| High-rate visual channel | Start with SignalR/JSON; use binary WebSocket only after profiling | Provisional |
| First vertical slice | Fixture replay -> ACARS parser -> message UI | Provisional |
| First import adapter | File-based newline-delimited JSON | Provisional |
| First aircraft-track slice | ADS-B/Mode S via dump1090/readsb-style import | Provisional |
| First image-mode slice | WEFAX synthetic scan-line replay | Provisional |
| Initial local ACARS frequencies | 136.800 MHz, 136.975 MHz, 136.650 MHz | User-provided |
| Initial storage technology | JSONL files with in-memory indexes | Accepted for starter implementation |
| First hardware adapter strategy | Simulator-first file/audio and rtl_tcp source manager | Accepted for starter implementation |

## Open Questions

1. Should the backend target exactly .NET 8 LTS, or the newest installed SDK with .NET 8 compatibility?
2. Should frontend packages use npm, pnpm, or yarn?
3. Should storage start with SQLite, embedded files, or an event-log plus indexed read model?
4. Which external decoded-data formats should be prioritized after NDJSON?
5. Which hardware source should be first: file/audio, rtl_tcp, or a Flex-style stream?

## Sprint 1 Notes

1. Backend solution created at `src/backend/AeroHub.slnx`.
2. Backend API health endpoint is `GET /api/health` on `http://localhost:5157` in local development.
3. Frontend Vite app created at `src/frontend/AeroHub.Web`.
4. Frontend dev server runs at `http://127.0.0.1:5173/` and proxies `/api` to the backend.
5. Initial health contract lives in `AeroHub.Contracts.HealthStatus`.

## Sprint 2 Notes

1. SignalR hub path is `/hubs/aerohub`.
2. Vite proxies both `/api` and `/hubs` to `http://localhost:5157`.
3. Fixture replay endpoint is `POST /api/fixtures/replay/acars`.
4. Recent messages endpoint is `GET /api/messages/recent?limit=50`.
5. JSON enum serialization uses string names for REST and SignalR payloads.
6. The React UI has a REST recent-message refresh after fixture replay so the demo remains useful if a live SignalR connection is temporarily disconnected.

## Sprint 3 Notes

1. ACARS parsing starts with conservative label/sublabel/preamble extraction, aircraft identifier extraction, confidence, raw preservation, and warnings.
2. Unknown labels are preserved with `ACARS_UNKNOWN_LABEL` rather than dropped.
3. Fixture replay now publishes three ACARS examples: `2L/POS`, `H1/#DFB`, and unknown label `ZZ/TEST`.
4. Local ACARS source profiles expose 136.800 MHz, 136.975 MHz, and 136.650 MHz.
5. React now has ACARS message filtering, selection, parsed detail, raw payload, warnings, and provenance display.

## Sprint 4 Notes

1. First decoded-data import source is `local-acars-ndjson`.
2. First import adapter is `FileNdjsonImportManager` for ACARS NDJSON fixture input.
3. Import source state is exposed through `GET /api/imports`.
4. Import diagnostics are exposed through `GET /api/imports/diagnostics` and SignalR `import.diagnostic` events.
5. Import start endpoint is `POST /api/imports/{importId}/start`.
6. Accepted imported ACARS records are normalized through the same ACARS parser and message bus as fixture replay.
7. Starter quarantine behavior covers schema mismatch, invalid JSON, oversized record, unsupported kind, duplicate record, and out-of-order timestamp cases.

## Sprint 5 Notes

1. Synthetic RF source ID is `synthetic-rf-replay`.
2. Spectrum and waterfall are reduced visualization products over SignalR/JSON for now.
3. Replay endpoint is `POST /api/streams/synthetic/replay?speedMultiplier=1`.
4. Metrics endpoint is `GET /api/streams/synthetic/metrics`.
5. React keeps only the latest 80 waterfall rows to bound visualization memory.
6. Binary WebSocket is still deferred until profiling proves SignalR/JSON is too expensive.

## Sprint 6 Notes

1. First aircraft-track source is `local-adsb-readsb-json`.
2. ADS-B import records update `InMemoryAircraftTrackStore` rather than the ACARS message parser.
3. Track API is `GET /api/aircraft`.
4. SignalR events are `aircraft.updated` and `aircraft.snapshot`.
5. Track correlation group IDs use the starter format `adsb-{ICAO}`.
6. Current tracks expire after 15 minutes without a fresher update.
7. React uses a table plus map placeholder until a real map library decision is made.

## Sprint 7 Notes

1. HFDL external-decoder import source is `sample-dumphfdl-json`.
2. VDL2 external-decoder import source is `sample-dumpvdl2-json`.
3. ACARS payloads from external decoder fixtures route through the ACARS parser.
4. ADS-C and CPDLC records are preserved as placeholder normalized messages with `EXTERNAL_PAYLOAD_PLACEHOLDER` warnings.
5. External process supervision is simulated for now; no real `dumphfdl` or `dumpvdl2` process is launched yet.
6. Supervision scenarios cover start, crash, timeout, malformed output, and restart limit.

## Sprint 8 Notes

1. CPDLC/ADS-C/SATCOM support is still a structured placeholder, not a full standards-complete parser.
2. `DatalinkMessageDetails` preserves direction, references, acknowledgement state, category, optional aircraft/position fields, and raw text.
3. `SatcomTransportMetadata` keeps satellite/channel/bearer/reassembly metadata separate from application details.
4. First SATCOM import source is `sample-satcom-json`.
5. ADS-C positions can update aircraft tracks with source type `ADS-C over SATCOM` or `ADS-C over HFDL`, distinct from ADS-B.

## Sprint 9 Notes

1. WEFAX starts as a synthetic image-mode replay service, not a full RF demodulator.
2. Initial WEFAX profile uses IOC 576 and 120 rpm.
3. WEFAX line/state events use SignalR/JSON for now.
4. React keeps a bounded WEFAX line buffer and renders to canvas.
5. Full FM subcarrier demodulation and captured WAV/IQ replay are deferred until contracts and UI behavior are stable.

## Sprint 10 Notes

1. Starter persistence uses `JsonlRecordStore`, not SQLite yet.
2. The store persists normalized messages, aircraft track snapshots, and import diagnostics.
3. Message persistence deduplicates by message ID.
4. Track persistence keeps the latest track per aircraft identifier.
5. Retention is day-based and rewrites JSONL files after cleanup.
6. Export snapshots are replayable JSONL folders.
7. React now displays storage counts/bytes and exposes refresh, export, and retention actions.

## Sprint 11 Notes

1. Hardware work starts with simulated adapters to protect decoder contracts and tests.
2. `file-audio-iq` models recorded or virtual audio/IQ input with acknowledged control.
3. `rtl-tcp-local` models raw U8 interleaved IQ and fire-and-forget control.
4. Sources require decoder ownership before start.
5. Exclusive ownership blocks a second active source owner.
6. Source health includes drops, reconnects, clipping, level, SNR, queue depth, sample rate, and bandwidth.
7. Real rtl_tcp work should validate the `RTL0` handshake and report host driver/setup failures as diagnostics.