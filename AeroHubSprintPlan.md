## AeroHub Sprint Plan

This sprint plan converts `AeroHubPlan.md` into an editable delivery sequence. It assumes two-week sprints, but each sprint can be split or merged depending on available time. The north star is a reliable aviation SDR platform with two equal ingestion paths: native decoding from signals and imported decoded data from other applications.

## Sprint 0: Project Decisions and Setup

**Status**: Complete - initial documentation and provisional decisions are in place.

**Goal**: Make the project buildable and remove early ambiguity.

**Scope**

1. Confirm target .NET version, Node version, package manager, test framework, and repository layout.
2. Decide local development model: separate backend/frontend processes.
3. Decide first packaged model: backend serves the React build unless a later desktop shell requirement appears.
4. Create initial `README.md`, `.gitignore`, `docs/architecture.md`, `docs/api-contract.md`, and `docs/performance-budgets.md`.
5. Record initial performance budgets for spectrum FPS, waterfall rows/sec, message events/sec, track updates/sec, WEFAX line rate, queue depth, and max payload size.

**Exit Criteria**

1. The repo has documented build/run commands.
2. Architecture and performance-budget docs exist.
3. Packaging decision is recorded as provisional, not left implicit.

**Completed Outputs**

1. `README.md`
2. `.gitignore`
3. `docs/architecture.md`
4. `docs/api-contract.md`
5. `docs/performance-budgets.md`
6. `docs/decisions.md`

## Sprint 1: Backend and Frontend Skeletons

**Status**: Complete - backend/frontend skeletons build and the frontend displays backend health.

**Goal**: Create a working full-stack shell with no real decoder logic yet.

**Scope**

1. Create backend projects: `AeroHub.Api`, `AeroHub.Contracts`, `AeroHub.Core`, `AeroHub.Decoders`, `AeroHub.Infrastructure`, and backend tests.
2. Create React + TypeScript frontend under `src/frontend/AeroHub.Web`.
3. Add health/status endpoint and frontend backend-connection indicator.
4. Add CI-ready commands for backend restore/build/test and frontend install/typecheck/build.
5. Add basic structured logging with correlation IDs.

**Exit Criteria**

1. `dotnet build` and backend tests pass.
2. Frontend typecheck/build passes.
3. Frontend can display backend health.
4. `AeroHub.Core` and `AeroHub.Contracts` have no UI or ASP.NET dependency.

**Completed Outputs**

1. `src/backend/AeroHub.slnx`
2. `src/backend/AeroHub.Api`
3. `src/backend/AeroHub.Contracts`
4. `src/backend/AeroHub.Core`
5. `src/backend/AeroHub.Decoders`
6. `src/backend/AeroHub.Infrastructure`
7. `tests/AeroHub.Core.Tests`
8. `tests/AeroHub.Api.Tests`
9. `src/frontend/AeroHub.Web`
10. `/api/health` endpoint and React backend status indicator

## Sprint 2: Contracts, Message Bus, and Fixture Replay

**Status**: Complete - canonical contracts, in-memory message bus, fixture replay, SignalR events, and React message display are in place.

**Goal**: Establish the canonical ingestion pipeline before feature work fans out.

**Scope**

1. Define the canonical pipeline: `SourceAdapter -> Decoder/ImportAdapter -> Validation -> Normalization -> Deduplication -> Storage -> Live Broadcast -> UI`.
2. Add contracts for signal sources, decoded-data sources, decoder state, import state, normalized messages, aircraft tracks, warnings, and provenance.
3. Implement in-memory message bus and fixture replay service.
4. Add SignalR hub for messages/status and REST endpoints for query/configuration.
5. Add frontend stores and views for message stream, source status, and decoder/import status.

**Exit Criteria**

1. A fixture can publish normalized messages through the backend and appear in React.
2. Message provenance is visible in API output and UI.
3. Backpressure and queue behavior are documented for the message stream.

**Completed Outputs**

1. Contract records for ingestion provenance, warnings, normalized aviation messages, runtime snapshots, and fixture replay results.
2. Core interfaces for clock, message bus, fixture replay, signal sources, decoded-data sources, import adapters, decoder modules, and track storage.
3. In-memory message bus with bounded recent-message storage.
4. ACARS fixture replay service seeded from local ACARS frequencies.
5. REST endpoints for recent messages, sources, imports, decoders, and ACARS fixture replay.
6. SignalR hub at `/hubs/aerohub` with `message.snapshot` and `message.received` events.
7. React message stream UI with backend health, SignalR status, fixture replay button, runtime state, and provenance display.

## Sprint 3: ACARS Vertical Slice

**Status**: Complete - conservative ACARS parser, fixtures, tests, local source-profile seeds, and React message review are in place.

**Goal**: Deliver the first useful aviation message workflow without live RF.

**Scope**

1. Implement ACARS normalized message contracts and a conservative text payload parser.
2. Preserve raw payload, label, optional sublabel, parser confidence, warnings, and transport metadata.
3. Add known-good, malformed, and unknown-label fixtures.
4. Add React message table, filters, and ACARS detail panel.
5. Add import parity test showing native fixture and imported decoded record normalize through the same message path.
6. Seed local ACARS source profiles from `docs/local-frequencies.md`: 136.800 MHz, 136.975 MHz, and 136.650 MHz.

**Exit Criteria**

1. ACARS fixtures pass backend tests.
2. Unknown ACARS messages are preserved, not dropped.
3. React can filter, select, and inspect ACARS messages.

**Completed Outputs**

1. `AeroHub.Decoders.Acars.AcarsMessageParser` with label, sublabel, preamble, aircraft identifier, confidence, raw preservation, and warnings.
2. `AcarsMessageDetails` attached to normalized aviation messages.
3. Fixture files under `fixtures/acars/local-vhf` for candidate OOOI, observed weather/operational, and unknown-label payloads.
4. `AeroHub.Decoders.Tests` parser coverage for known-good, malformed, unknown-label, and imported-data parity cases.
5. Fixture replay now publishes parsed ACARS messages through the shared message bus.
6. `/api/sources` now exposes the three local ACARS frequency profile seeds: 136.800 MHz, 136.975 MHz, and 136.650 MHz.
7. React message UI now supports ACARS filtering, selectable messages, raw payload inspection, parsed field detail, warnings, and provenance display.

## Sprint 4: Decoded-Data Import Framework

**Status**: Complete - NDJSON import source, import manager, diagnostics/quarantine behavior, API endpoints, SignalR events, tests, and React import UI are in place.

**Goal**: Make external decoded data a first-class ingestion path.

**Scope**

1. Implement `IDecodedDataSource`, `IImportAdapter`, and `ImportManager`.
2. Add file-based NDJSON import as the first adapter.
3. Add import diagnostics stream and quarantine behavior for malformed, oversized, schema-mismatched, duplicate, or out-of-order records.
4. Add source app, source format, format version, adapter version, import timestamp, original timestamp, confidence, and raw record reference to imported records.
5. Add React Imports page for source status, diagnostics, accepted count, rejected count, and last error.

**Exit Criteria**

1. NDJSON import can feed normalized messages without bypassing validation, deduplication, storage, or live broadcast.
2. Bad records are visible in import diagnostics and do not crash ingestion.
3. Native and imported messages are distinguishable in UI and storage.

**Completed Outputs**

1. Import contracts for decoded import records, import replay results, and import diagnostics.
2. `IImportManager` with import state, diagnostics, and start behavior.
3. `FileNdjsonImportManager` for the `local-acars-ndjson` source.
4. Import fixture at `fixtures/imports/acars/local-acars.ndjson` with accepted, schema-mismatched, duplicate, and out-of-order records.
5. Import quarantine diagnostics for malformed JSON, oversized records, schema mismatches, unsupported kinds, duplicate records, and out-of-order records.
6. REST endpoints for import sources, import diagnostics, and starting an import.
7. SignalR events for `import.updated`, `import.diagnostic`, and `import.diagnostic.snapshot`.
8. React import controls and diagnostics display with accepted/quarantined counts and provenance-visible imported messages.
9. `docs/decoded-data-imports.md`.

## Sprint 5: Synthetic Spectrum, Waterfall, and Stream Budgets

**Status**: Complete - synthetic spectrum/waterfall contracts, replay service, metrics, SignalR events, React canvas views, and 1x/5x/20x tests are in place.

**Goal**: Prove real-time visualization contracts before hardware arrives.

**Scope**

1. Add synthetic spectrum and waterfall frame producers in the backend.
2. Define frame contracts, timestamps, sequence numbers, drop/coalesce policy, and reconnect behavior.
3. Start with SignalR or JSON transport, then measure whether binary WebSocket is needed.
4. Build React canvas spectrum/waterfall views with frame-rate caps and bounded queues.
5. Add replay tests at 1x, 5x, and 20x for stream stability.

**Exit Criteria**

1. Frontend remains interactive during synthetic replay.
2. Slow frontend clients do not block backend producers.
3. Stream metrics expose frame rate, queue depth, dropped frames, and client lag.

**Completed Outputs**

1. Contracts for `SpectrumFrame`, `WaterfallRows`, `WaterfallRow`, `StreamMetricsSnapshot`, and `SyntheticStreamReplayResult`.
2. `ISyntheticRfStreamService` in Core.
3. `SyntheticRfStreamService` producing reduced spectrum frames and waterfall row batches at 1x, 5x, and 20x replay speeds.
4. REST endpoints for synthetic stream metrics and replay.
5. SignalR events for `spectrum.frame`, `waterfall.rows`, and `stream.metrics`.
6. React replay controls and canvas-backed spectrum/waterfall displays with bounded waterfall row buffering.
7. Backend tests covering 1x, 5x, and 20x replay behavior.
8. `docs/synthetic-streams.md`.

## Sprint 6: ADS-B/Mode S Track Slice

**Status**: Complete - ADS-B/readsb import fixture, aircraft-track contracts/store, API/SignalR track updates, React aircraft table/map placeholder, diagnostics, tests, and docs are in place.

**Goal**: Add the first aircraft-track producing feature.

**Scope**

1. Define ADS-B/Mode S message and aircraft-track contracts.
2. Add fixture/import support for dump1090/readsb-style aircraft JSON.
3. Implement track updates, source confidence, duplicate handling, and stale-track expiry.
4. Add React aircraft table and map placeholder.
5. Add storage indexes for ICAO address, callsign, source type, timestamps, and correlation group ID.

**Exit Criteria**

1. Imported ADS-B aircraft JSON creates aircraft tracks.
2. Track updates preserve provenance and stale-state behavior.
3. Frontend can inspect aircraft history without high-volume table slowdown.

**Completed Outputs**

1. `AdsbAircraftImportRecord` contract for readsb/dump1090-style decoded aircraft records.
2. Expanded `AircraftTrackSnapshot` with callsign, speed, track, source type, correlation group, stale state, and provenance.
3. `InMemoryAircraftTrackStore` with update events, duplicate/older update rejection, and 15-minute stale-track expiry.
4. ADS-B fixture at `fixtures/imports/adsb/local-readsb-aircraft.ndjson`.
5. `FileNdjsonImportManager` support for `local-adsb-readsb-json` with schema, duplicate, stale, and invalid-position quarantine diagnostics.
6. `GET /api/aircraft` and SignalR `aircraft.snapshot` / `aircraft.updated` support.
7. React ADS-B import action, aircraft count, aircraft table, provenance display, and map placeholder.
8. Tests for ADS-B import, quarantine diagnostics, track provenance, and stale-track expiry.
9. `docs/adsb-tracks.md`.

## Sprint 7: HFDL and VDL2 External Decoder Adapters

**Status**: Complete - dumphfdl/dumpvdl2 sample imports, external process supervision skeleton, diagnostics, API/SignalR events, React controls, tests, and docs are in place.

**Goal**: Integrate mature external decoder outputs before native RF demodulation.

**Scope**

1. Add dumphfdl JSON import/process adapter skeleton.
2. Add dumpvdl2 JSON import/process adapter skeleton.
3. Route ACARS payloads to the ACARS parser and CPDLC/ADS-C payloads to placeholder application models.
4. Add external process supervision: version detection, startup validation, stdout/stderr contracts, timeout, restart policy, crash-loop protection, and captured logs.
5. Add diagnostics UI for external decoder process state.

**Exit Criteria**

1. Sample dumphfdl and dumpvdl2 records normalize into messages with transport provenance.
2. External adapter failures are visible and recoverable.
3. Process supervision tests cover crash, timeout, malformed output, and restart limits.

**Completed Outputs**

1. `ExternalDecoderRecord`, `ExternalDecoderProcessSnapshot`, `ExternalDecoderDiagnostic`, and `ExternalDecoderSimulationResult` contracts.
2. `IExternalDecoderProcessManager` and `ExternalDecoderProcessManager` with simulated start/crash/timeout/malformed-output/restart-limit scenarios.
3. `sample-dumphfdl-json` and `sample-dumpvdl2-json` import sources.
4. HFDL and VDL2 fixture files under `fixtures/imports/hfdl` and `fixtures/imports/vdl2`.
5. ACARS payload routing through `AcarsMessageParser` and ADS-C/CPDLC placeholder preservation with warnings.
6. REST endpoints and SignalR events for external decoder process state and diagnostics.
7. React controls for dumphfdl/dumpvdl2 imports and external process scenario simulation.
8. Tests covering HFDL/VDL2 import routing and crash/timeout/malformed-output/restart-limit supervision.
9. `docs/external-decoder-adapters.md`.

## Sprint 8: CPDLC/ADS-C and SATCOM Application Models

**Status**: Complete - CPDLC/ADS-C detail records, SATCOM metadata, SATCOM import fixture, ADS-C track correlation, React detail display, tests, and docs are in place.

**Goal**: Expand beyond ACARS text into structured aviation datalink content.

**Scope**

1. Add CPDLC/ADS-C normalized contracts for direction, message references, acknowledgements, reports, position observations, and warnings.
2. Add SATCOM transport metadata contracts and import adapter skeletons.
3. Preserve unknown/proprietary payloads with parser confidence and raw references.
4. Add React detail views for CPDLC/ADS-C and SATCOM provenance.
5. Add correlation hooks so ADS-C positions can contribute aircraft observations without being treated like ADS-B.

**Exit Criteria**

1. Structured datalink fixtures produce normalized records.
2. Unknown elements are retained and visible.
3. ADS-C/SATCOM observations can be correlated with aircraft tracks while keeping source semantics.

**Completed Outputs**

1. `DatalinkMessageDetails` and `SatcomTransportMetadata` optional message details.
2. Extended external decoder records with message references, acknowledgement state, aircraft identifier, position, and SATCOM metadata.
3. `sample-satcom-json` import source and `fixtures/imports/satcom/sample-satcom.ndjson`.
4. CPDLC, ADS-C, and proprietary SATCOM placeholder messages with structured details and raw preservation.
5. ADS-C position correlation into aircraft tracks with source type distinct from ADS-B.
6. React SATCOM import action and CPDLC/ADS-C/SATCOM detail display.
7. Tests for SATCOM CPDLC/ADS-C/proprietary payload preservation and ADS-C track correlation.
8. `docs/datalink-satcom-models.md`.

## Sprint 9: WEFAX Image Decoder Skeleton

**Status**: Complete - WEFAX contracts, synthetic scan-line replay, state/line SignalR events, React image panel, controls, tests, and docs are in place.

**Goal**: Add the first image-mode decoder path.

**Scope**

1. Define WEFAX image, scan-line, decoder-state, tone, sync-confidence, IOC, line-rate, and slant metadata contracts.
2. Implement synthetic WEFAX scan-line generation and fixture replay.
3. Add frontend WEFAX image panel with line streaming, degraded state, save/export placeholder, polarity, contrast, slant, and resync controls.
4. Add tests for line-rate conversion, IOC width, polarity, slant metadata, partial capture preservation, and sync-state transitions.
5. Defer full FM subcarrier demodulation until the image contracts and UI are stable.

**Exit Criteria**

1. Synthetic WEFAX lines render in React without blocking message/track streams.
2. Partial and degraded image states are visible.
3. WEFAX tests pass from deterministic fixtures.

**Completed Outputs**

1. Contracts for `WefaxDecoderState`, `WefaxToneMetrics`, `WefaxImageLine`, `WefaxLineBatch`, and `WefaxReplayResult`.
2. `IWefaxReplayService` and `SyntheticWefaxReplayService`.
3. REST endpoints for WEFAX state, replay, and controls.
4. SignalR `wefax.lines` and `wefax.state` events plus hub snapshot support.
5. React WEFAX panel with replay, partial replay, inversion, slant, resync, image canvas, metrics, and warnings.
6. Backend tests for deterministic line replay, partial/lost sync preservation, and control updates.
7. `docs/wefax-image-decoder.md`.

## Sprint 10: Storage, Retention, and Query Reliability

**Status**: Complete - JSONL record store, retention, export, persisted message/track/diagnostic paths, tests, UI storage metrics, and docs are in place.

**Goal**: Replace purely in-memory operation with reliable local persistence.

**Scope**

1. Choose local storage technology and record the decision.
2. Implement retention policies for raw captures, imported raw records, normalized messages, aircraft tracks, WEFAX images, logs, replay fixtures, and diagnostics.
3. Add indexes for message and track query performance.
4. Add export/replay support for captures and imported records.
5. Add tests for retention cleanup, query filters, deduplication, and replay fidelity.

**Exit Criteria**

1. Long replay does not cause unbounded memory or disk growth.
2. Common queries stay within documented latency budgets.
3. Stored records preserve provenance and raw references.

**Completed Outputs**

1. Storage contracts for retention policy, storage snapshot, retention result, and export result.
2. `IRecordStore` and `JsonlRecordStore` with JSONL persistence and in-memory indexes.
3. Message bus persistence with message ID deduplication.
4. Aircraft track persistence with latest-track replacement.
5. Import diagnostic persistence.
6. Retention cleanup for messages, tracks, and diagnostics.
7. Export snapshots containing replayable JSONL files.
8. Storage API endpoints and React storage metrics/actions.
9. Tests for deduplication, reload, retention, export, and replay fidelity.
10. `docs/storage-retention.md`.

## Sprint 11: Hardware Source Adapters

**Status**: Complete - simulator-first file/audio and rtl_tcp source adapters, capabilities, ownership, metrics, diagnostics, API, UI, tests, and docs are in place.

**Goal**: Add live input sources without destabilizing decoder contracts.

**Scope**

1. Add file/audio input adapter first, then rtl_tcp or selected SDR hardware adapter.
2. Expose capabilities for sample rate, bandwidth, frequency range, gain controls, and exclusive/shared ownership.
3. Enforce source/decoder compatibility in `DecoderManager` and `ImportManager`.
4. Add health metrics for sample rate, dropped buffers, reconnects, clipping, SNR/level, and queue depth.
5. Add tests using simulated adapters before real devices.

**Exit Criteria**

1. Hardware/source failures degrade gracefully and surface actionable diagnostics.
2. Exclusive source ownership is enforced.
3. Fixture replay remains the regression oracle for decoder behavior.

**Completed Outputs**

1. Hardware source contracts for capabilities, metrics, diagnostics, start requests, snapshots, and simulation results.
2. `IHardwareSourceManager` and `SimulatedHardwareSourceManager`.
3. File/audio IQ source adapter seed with timestamped audio-buffer framing.
4. rtl_tcp source adapter seed with raw U8 interleaved-IQ framing and fire-and-forget control semantics.
5. Exclusive ownership enforcement requiring a decoder owner before source start.
6. Source health metrics for sample rate, bandwidth, drops, reconnects, level, SNR, clipping, and queue depth.
7. Simulated buffer-drop, reconnect, clipping, and failure diagnostics.
8. REST and SignalR endpoints for hardware source state and diagnostics.
9. React Live RF source-adapter panel with start/stop and failure simulation controls.
10. Tests covering capabilities, ownership, simulation diagnostics, and invalid tuning requests.
11. `docs/hardware-source-adapters.md`.

## Sprint 12: Reliability Hardening and Release Candidate

**Status**: In progress - operator diagnostics and release-candidate packaging are in place; final soak validation is the remaining hardening pass.

**Goal**: Make AeroHub dependable enough for long-running monitoring.

**Scope**

1. Run soak tests with fixture replay, decoded-data imports, synthetic spectrum/waterfall, and external adapter failures.
2. Test frontend disconnect/reconnect, backend restart mid-stream, queue saturation, corrupted imports, and killed external decoder processes.
3. Add release packaging for the chosen deployment model.
4. Add operator diagnostics dashboard for backend version, active decoders/imports, stream rates, queue depths, process state, storage usage, and recent warnings.
5. Review all feature acceptance gates and document remaining gaps.

**Exit Criteria**

1. Soak tests complete without unbounded memory, disk, or queue growth.
2. Restart/reconnect paths preserve visible state and recover automatically where possible.
3. Release candidate build includes backend, frontend, docs, and replay fixtures.

**Completed Outputs**

1. Operator diagnostics endpoint at `/api/diagnostics/operator` exposing system health, active imports, queue depth, storage usage, and recent warning codes.
2. React dashboard summary that surfaces backend version, environment, active warning state, and storage footprint.
3. Release-candidate packaging guide and a PowerShell packaging script for backend, frontend build output, docs, and replay fixtures.
4. Confirmation that runtime metrics and storage snapshots remain visible during reliability checks.
5. Bounded message-history regression coverage under sustained replay, plus isolated-output validation for the release test path.

## Sprint 13: Map Enhancements

**Status**: Complete - quick wins, filters, distance/bearing, view persistence, CSV & GeoJSON exports, scoped single-aircraft playback slider, low-zoom marker clustering, radio navaid overlays, and multi-source feed visual distinctions are all complete.

**Goal**: Extend the live aircraft map (Leaflet map, aircraft list, classification, flight-track trail, range rings, and last-hour filter) into a more complete situational-awareness view for operators.

**Scope**

1. Quick wins: last-seen/staleness display in marker popups, altitude-based marker size or color banding, copy-to-clipboard for hex/registration/callsign from the map popup, and a "follow selected aircraft" mode that keeps the map centered on the selection as it moves.
2. Historical playback: a scrubber/slider over the cached per-aircraft position history (already persisted in `aircraft_positions`) to replay the last hour of traffic instead of only live-plus-trail.
3. Additional map filters: aircraft class (Military/Government/Commercial/Civilian/Unknown), altitude range, and "position-only" toggle, layered on top of the existing last-hour filter.
4. Distance/bearing from center displayed per aircraft (in the list and popup), pairing with the existing range rings.
5. Marker clustering at low zoom levels so dense traffic near the range rings stays readable when zoomed out, with a click-to-zoom interaction.
6. Multi-source/multi-receiver support: distinguish aircraft feed provenance (dump1090, readsb, SATCOM, RadioSonde) with source badges across popups, detail cards, list views, and track tables.
7. Persist the user's map view (center/zoom) in `localStorage` so it survives page reloads instead of always resetting to the configured center.
8. Export the currently visible aircraft (as shown on the map, respecting active filters) as CSV or GeoJSON.

**Exit Criteria**

1. Each quick-win item works against the live dump1090 feed without regressing existing map/list/photo/classification behavior.
2. Historical playback and additional filters compose correctly with the existing last-hour filter, range rings, and flight-track trail.
3. Marker clustering does not hide the ability to select and inspect an individual aircraft or zoom in.
4. Map view persistence and export features have test coverage where backend logic is involved (e.g. filter composition, distance/bearing calculation, navaid service).

**Completed Outputs**

1. Marker popups show last-seen age (e.g. "2m ago"), altitude/speed/track, distance/bearing from the map center, source feed badge, plus a "Copy hex" button wired to the clipboard helper.
2. Marker icons carry a small altitude-band color indicator (ground/low/mid/high) alongside the existing type-based icon and category rotation.
3. "Follow selected aircraft" toggle keeps the map panned to the selection as new positions arrive, independent of the one-time pan-on-select behavior.
4. Aircraft-class filter (checkboxes with the same color coding as the list/badges), altitude min/max filter, and a "position only" toggle, all composed with the existing last-hour filter in a single `mapTracks` derivation feeding both the map and the aircraft list.
5. Map view (center/zoom) persists across reloads via `localStorage`.
6. "Export (CSV)" and "Export (GeoJSON)" buttons download the currently filtered aircraft set in standard tabular or spatial formats.
7. Scoped historical playback: a 0-60 minute slider (shown once the selected aircraft has 2+ cached history points) that places a distinct marker at the closest historical position for the chosen time offset, using the same timestamped position cache as the flight-track trail.
8. Low-zoom Marker Clustering: groups nearby aircraft markers into cluster badges when zoomed out (`zoom < 9`), calculating center points and zooming in on click.
9. Multi-Source Visual Distinction: feed provenance badges (`dump1090`, `readsb`, `radiosonde`, `satcom`) on map popups, detail cards, list items, and track table rows.
10. Radio Navigation Aids (Navaids): 33 ground-based VOR, VORTAC, NDB, ILS Localizer, and DME stations within 200 miles of EM13pc rendered with distinct symbol markers, callsign labels, and distance/bearing metadata.

**Deferred**

1. Full multi-aircraft simultaneous playback - the playback slider is intentionally scoped to the selected aircraft's own cached history rather than replaying every aircraft simultaneously, to avoid bulk-fetching history for aircraft that aren't selected.

## Cross-Sprint Rules

1. Every feature must pass the acceptance gates in `AeroHubPlan.md` before graduating from skeleton to active module.
2. Every decoder/import adapter must preserve provenance, raw record references, confidence, and warnings.
3. Native decoder output, external process output, and imported decoded data must use the same normalization, deduplication, storage, and broadcast path.
4. React must remain a presentation and command surface; DSP, protocol parsing, and import parsing stay in the .NET backend.
5. High-rate streams must have budgets, bounded queues, metrics, and drop/coalesce policies before live hardware work depends on them.
6. Add fixture replay before adding live hardware behavior for the same feature.