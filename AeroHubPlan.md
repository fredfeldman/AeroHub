## Plan: AeroHub App Starter

AeroHub will be a two-part aviation SDR application: a .NET backend owns hardware access, DSP, protocol decoding, decoded-data import, message normalization, persistence, and real-time APIs; a React frontend owns operator workflow, spectrum/waterfall visualization, aircraft/message views, and configuration. Start with a thin vertical slice that can run without real hardware, then add HFDL, ACARS, VDL2, ADS-B/Mode S, CPDLC/ADS-C, SATCOM, and WEFAX modules behind stable backend contracts.

**Steps**

### Phase 1: Repository and Tooling Foundation

1. Create a solution/repo layout with `src/backend`, `src/frontend`, `tests`, `docs`, and `fixtures`. Keep existing `.github/skills`, `.github/instructions`, and `.github/prompts` in place.
2. Initialize a .NET solution for the backend. Recommended projects: `AeroHub.Api`, `AeroHub.Core`, `AeroHub.Infrastructure`, `AeroHub.Decoders`, `AeroHub.Contracts`, and focused test projects.
3. Initialize a React + TypeScript frontend under `src/frontend/AeroHub.Web`. Prefer Vite unless there is a later reason to choose Next.js.
4. Add shared repo hygiene: `.gitignore`, `README.md`, `docs/architecture.md`, `docs/api-contract.md`, and optional `Directory.Build.props` / central package management for .NET.
5. Define initial build commands in the README for backend restore/build/test and frontend install/build/test.

### Phase 2: Backend Architecture

6. In `AeroHub.Contracts`, define transport-neutral records for signal sources, decoded-data sources, channels, decoder state, import state, normalized messages, aircraft tracks, spectrum frames, waterfall frames, and backend health.
7. In `AeroHub.Core`, define core interfaces: `ISignalSource`, `IDecodedDataSource`, `IImportAdapter`, `ISpectrumFrameProvider`, `IDecoderModule`, `IStreamingDecoder`, `IBatchDecoder`, `IDecoderManager`, `IMessageBus`, `IAircraftTrackStore`, and `IClock`.
8. In `AeroHub.Decoders`, create module folders for `Acars`, `Hfdl`, `Vdl2`, `Adsb`, `CpdlcAdsC`, `Satcom`, and `Wefax`. Start with interfaces, placeholder parsers, synthetic fixtures, and typed normalized outputs rather than full RF demodulators.
9. In `AeroHub.Infrastructure`, add runtime services for in-memory message bus, decoder registration, fixture replay, decoded-data import adapters, settings storage, and optional external decoder process hosting.
10. In `AeroHub.Api`, expose HTTP endpoints for status, channels, decoded-data sources, decoder catalog, decoder start/stop/configuration, import start/stop/configuration, message query, track query, and settings. Add SignalR hubs or WebSocket endpoints for live spectrum frames, decoder events, import events, messages, and aircraft tracks.
11. Keep DSP/protocol code out of the API layer. API controllers/hubs should adapt contracts and route commands only.

### Phase 3: Decoder Vertical Slice

12. Implement the first no-hardware vertical slice using fixture replay. Recommended initial slice: ACARS text payload parsing plus normalized message display, because it proves message contracts without needing RF demodulation.
13. Add a second real-time visual slice with synthetic spectrum/waterfall frames so the frontend can develop rendering and throttling independently of hardware.
14. Add WEFAX as the first image-mode decoder skeleton: synthetic line generation, decoder state, image buffer metadata, and frontend rendering target. Full FM subcarrier demodulation can follow after contracts are stable.
15. Add HFDL and VDL2 initially as transport adapters that can ingest normalized sample JSON, decoded-data imports, or external decoder output from tools such as dumphfdl/dumpvdl2, then hand validated payloads to ACARS or CPDLC/ADS-C parsers.
16. Add ADS-B/Mode S as a surveillance module with typed frame/message models and aircraft track updates. Full 1090 demodulation can be deferred behind the same module contract.
17. Seed ACARS source profiles from `docs/local-frequencies.md`, initially including 136.800 MHz, 136.975 MHz, and 136.650 MHz as editable local VHF ACARS entries.

### Phase 3A: Feature Expansion Roadmap

Feature expansion should proceed by reusable capability, not by adding isolated screens. Each new aircraft signal should prove one backend contract, one frontend view pattern, and one fixture-based validation path that later decoders can reuse.

18. **Message-core expansion**: implement ACARS first, then reuse the normalized message pipeline for HFDL ACARS, VDL2 ACARS, SATCOM ACARS, CPDLC, ADS-C, and unknown/proprietary payload preservation.
19. **Transport expansion**: add HFDL, VDL2, and SATCOM as transport adapters that attach source metadata, validation status, and raw payload references before handing application payloads to ACARS or CPDLC/ADS-C parsers.
20. **Surveillance expansion**: add ADS-B/Mode S as the first aircraft-track producer, then allow HFDL, ADS-C, SATCOM, and ACARS position messages to contribute track observations with source-specific confidence.
21. **Image-mode expansion**: add WEFAX as a separate image decoder path with scan-line streaming, image metadata, tone/sync confidence, and save/export support.
22. **Hardware expansion**: keep fixture replay as the contract oracle, then add file/audio input, rtl_tcp or SDR device input, and finally multi-source operation. Each hardware adapter must expose capabilities so the backend can prevent unsupported decoder/source combinations.
23. **Decoded-data import expansion**: add import adapters for files, TCP/UDP streams, WebSocket feeds, REST polling, ZeroMQ where available, and watched folders. Imports should accept already-decoded messages from other applications and normalize them through the same message/track pipeline used by native decoders.
24. **External-decoder expansion**: add process adapters for mature tools before writing full RF demodulators when that gives faster validation. Candidate adapters include dumphfdl for HFDL, dumpvdl2 for VDL2, and dump1090/readsb-style outputs for ADS-B. Treat supervised external processes and user-configured import feeds as separate source types even when they produce the same normalized message records.
25. **Correlation expansion**: after at least two message/track sources exist, add correlation services for duplicate messages, aircraft identity resolution, route/position timelines, and source reliability metrics.
26. **Operator workflow expansion**: grow the frontend from tables into task views: monitor live RF, triage messages, inspect aircraft history, compare decoder/import confidence, replay captures, and tune active sources.

### Phase 3B: Feature Acceptance Gates

A feature is ready to expand from skeleton to active module only when these checks pass:

27. The backend exposes a typed contract for the feature's input, output, state, errors, and source metadata.
28. The module can run from a fixture without live hardware or external network access.
29. The module preserves raw payload/frame references and parser warnings for unknown or partial data.
30. The frontend can show the feature's primary output and degraded/error state without protocol-specific hacks in shared components.
31. Tests cover at least one known-good fixture, one malformed fixture, and one unknown/unsupported payload case.
32. The feature declares whether it can run concurrently with other decoders/imports or requires exclusive source/hardware ownership.
33. Documentation records which skill, external reference, external app format, or decoder oracle was used for validation.
34. Imported decoded data must be tagged with provenance: source app, source format, import adapter, original timestamp, receive/import timestamp, confidence, and raw record reference.
35. Native decoder output and imported decoded output must be distinguishable in storage and UI while sharing the same normalized message and aircraft-track contracts.

### Phase 4: React Frontend

36. Build a dense operator shell with navigation for Live RF, Messages, Aircraft, Decoders, Imports, WEFAX, Sources, and Settings.
37. Add API client modules generated from or aligned with `docs/api-contract.md`: status, decoder catalog, decoder control, import source control, messages, tracks, settings, and live stream clients.
38. Add state stores for backend connection state, selected channel/source, active decoders, active imports, message filters, aircraft tracks, and user layout/settings.
39. Implement live views: spectrum/waterfall canvas, decoder/import status grid, aviation message table, aircraft track table/map placeholder, ACARS detail panel, import source panel, and WEFAX image panel.
40. Treat React as presentation and command surface only. Do not put DSP, protocol parsing, import parsing, or raw high-rate sample processing in the frontend.

### Phase 5: Hardware and External Decoder Integration

41. Add source adapters incrementally: fixture replay first, decoded-data file import second, then audio/WASAPI or file input, then SDR hardware adapters such as RTL-SDR/rtl_tcp or Flex-style streams.
42. Add decoded-data import adapters for common outputs: newline-delimited JSON, CSV, UDP JSON, TCP JSON, watched folder drops, Airframes-style API data, dump1090/readsb aircraft JSON, dumphfdl JSON, and dumpvdl2 JSON.
43. Add external decoder process adapters where practical: dumphfdl for HFDL comparison, dumpvdl2 for VDL2 comparison, and dump1090/readsb-style output for ADS-B comparison.
44. Make `DecoderManager` and an `ImportManager` responsible for resource arbitration. They should prevent conflicting decoder/source/import combinations when hardware, ports, files, or process ownership are exclusive.
45. Preserve source metadata in every message: transport, frequency, sample rate, SNR/level when available, validation state, parser/import confidence, source app, source format, and raw payload reference.

### Phase 6: Persistence and Operations

46. Add settings persistence for receiver/source profiles, import source profiles, decoder thresholds, frontend layout, message filters, and display preferences.
47. Add capture/fixture/import management: save raw or normalized replay inputs, imported decoded records, metadata, and expected output.
48. Add structured logging and diagnostics for decoder confidence, import confidence, dropped frames, malformed import records, external process failures, reconnects, and parser warnings.
49. Add a lightweight health dashboard in the frontend for backend version, connected sources, active decoders/imports, stream rates, and queue depths.

**Relevant Files**

- `.github/instructions/sdr-generated-app.instructions.md` - current layering and naming rules for generated C#/XAML app code; adapt its core principles to backend + React.
- `.github/prompts/new-sdr-analysis-app.prompt.md` - useful project-layer template, but update direction from WPF-only to .NET API + React.
- `.github/skills/smart-sdr-architecture/SKILL.md` - source for manager/state/display layering.
- `.github/skills/signal-decoding-analysis/SKILL.md` - decoder module shape and mode catalog.
- `.github/skills/acars-message-documentation/SKILL.md` - ACARS application parsing guidance.
- `.github/skills/hfdl-airframes/SKILL.md` - HFDL, Airframes, and dumphfdl interoperability guidance.
- `.github/skills/vdl-mode-2-datalink/SKILL.md` - VDL2/AVLC transport guidance.
- `.github/skills/adsb-mode-s-surveillance/SKILL.md` - ADS-B/Mode S surveillance and track guidance.
- `.github/skills/cpdlc-ads-c-datalink/SKILL.md` - CPDLC/ADS-C/FANS/ATN application guidance.
- `.github/skills/satcom-aero-datalink/SKILL.md` - SATCOM transport guidance.
- `.github/skills/wefax-weather-fax/SKILL.md` - WEFAX image decoder guidance.
- `.github/skills/dsp-fundamentals/SKILL.md` - DSP primitive guidance.
- `.github/skills/third-party-license-compliance/SKILL.md` - required before adapting external decoder implementation details.
- `docs/local-frequencies.md` - local ACARS frequency seeds for editable source profiles.

**Verification**

1. Backend foundation: run `dotnet restore`, `dotnet build`, and `dotnet test` from the solution root.
2. Frontend foundation: run package install, lint, typecheck, unit tests, and production build from `src/frontend/AeroHub.Web`.
3. Contract validation: verify OpenAPI/SignalR/WebSocket event docs match generated TypeScript client types.
4. Layering validation: confirm `AeroHub.Core` and `AeroHub.Contracts` do not reference ASP.NET, React assets, WPF, or UI packages.
5. Decoder validation: each decoder module gets fixture-based tests before live hardware work is considered complete.
6. Real-time validation: run fixture replay and verify the frontend receives throttled spectrum/waterfall frames, message updates, and decoder state changes without UI stalls.
7. External decoder validation: compare HFDL/VDL2/ADS-B output against known tools before trusting in-process implementations.
8. Import validation: replay sample decoded-data exports from external apps and verify they produce the same normalized messages/tracks as native decoder fixtures where the payloads overlap.

**Decisions**

- App name: `AeroHub`.
- Architecture: .NET backend plus React/TypeScript frontend, not WPF-first.
- Initial backend approach: API + real-time stream endpoints with fixture replay before hardware.
- DSP and protocol parsing stay in .NET backend libraries.
- React frontend owns visualization, filtering, operator workflow, and settings UI only.
- First implementation should favor stable contracts and synthetic/fixture data over immediate full RF demodulation.
- Include HFDL, ACARS, VDL2, ADS-B/Mode S, CPDLC/ADS-C, SATCOM, and WEFAX in the target module catalog.
- Expand features by capability gates: message parsing, transport adapters, surveillance tracking, image decoding, hardware sources, external decoder adapters, and correlation services.
- Support two equal ingestion paths: native decoding from signal sources and imported decoded data from other applications.
- Imported records must preserve provenance and remain visibly distinguishable from native decoder output.

**Backend/Frontend Guardrails**

The backend/frontend split is still the recommended architecture, but implementation must control the extra real-time complexity it introduces.

1. Keep raw IQ/audio samples inside the .NET backend unless a future feature has a measured need for raw client-side samples.
2. Stream reduced live products to React: FFT bins, waterfall rows, decoder status, normalized messages, aircraft tracks, WEFAX image lines, health metrics, and warnings.
3. Use explicit contracts for every live update: schema, timestamp semantics, ordering, reconnect behavior, throttling, and backpressure policy.
4. Prefer SignalR for control/events and message/track/status updates. Use a dedicated binary WebSocket only if spectrum or waterfall profiling shows SignalR/JSON is too expensive.
5. Treat fixture replay as the first debugging tool for contract, timing, and UI bugs. Every live stream should have a deterministic replay source before hardware integration.
6. Keep frontend state derived from backend truth. React may filter, sort, select, and visualize data, but it must not become a second protocol parser or decoder engine.
7. Design packaging early: decide whether the backend runs as a console app, tray-hosted local service, Windows service, or bundled child process that serves the React app.
8. Add structured logs and correlation IDs across decoder/import adapters, API streams, and frontend events so cross-boundary bugs can be traced.
9. Budget every high-rate stream separately. Spectrum and waterfall rendering should have frame-rate caps, coalescing, and drop policies that do not block decoder processing.
10. Make native decoder output and imported decoded output share normalized contracts while remaining visibly distinguishable in storage, diagnostics, and UI.

**Optimization and Reliability Recommendations**

These recommendations should be treated as implementation requirements before adding live hardware or long-running imports.

1. Define the canonical ingestion pipeline before implementing individual decoders or importers: `SourceAdapter -> Decoder/ImportAdapter -> Validation -> Normalization -> Deduplication -> Storage -> Live Broadcast -> UI`.
2. Keep native decoder output, supervised external decoder output, and imported decoded-data feeds on the same normalized pipeline after validation. They may have different adapters, but they should not bypass deduplication, storage, provenance, or live broadcast rules.
3. Add explicit performance budgets in `docs/performance-budgets.md`: target spectrum FPS, waterfall rows/sec, message events/sec, aircraft-track updates/sec, WEFAX line rate, max payload size, max queue depth, and per-stream drop/coalesce policy.
4. Add queue and backpressure rules at every high-rate boundary. Decoder processing must continue even when UI clients are slow; UI streams may coalesce or drop stale visualization frames before dropping decoded messages.
5. Use binary payloads only where measurement proves JSON is too expensive. Start with JSON/SignalR for messages, tracks, health, imports, and decoder state; reserve binary WebSocket frames for spectrum/waterfall products if profiling requires it.
6. Add import quarantine behavior. Malformed, oversized, schema-mismatched, duplicate, out-of-order, or hostile records should be rejected or accepted-with-warning according to adapter policy and surfaced through an import diagnostics stream.
7. Version every external app import format. Each adapter should record source app, source version if known, format version, adapter version, original timestamp, import timestamp, parse warnings, and raw record location.
8. Define storage retention before ingest volume grows. Use separate retention policies for raw captures, imported raw records, normalized messages, aircraft tracks, WEFAX images, logs, replay fixtures, and diagnostics.
9. Add storage indexes for correlation and query performance: ICAO address, registration/callsign, source type, source app, transport, frequency, original timestamp, receive/import timestamp, message hash, and correlation group ID.
10. Supervise external decoder processes explicitly. Process adapters need version detection, startup validation, stdout/stderr parsing contracts, timeouts, restart policy, crash-loop protection, and captured process logs.
11. Split API channels by workload: REST for configuration/query, SignalR for command/status/messages/tracks/import diagnostics, and optional binary WebSocket for high-rate spectrum/waterfall frames.
12. Version live event names and payload contracts so frontend and backend can evolve independently. Breaking contract changes should be visible in `docs/api-contract.md`.
13. Add frontend rendering acceptance gates: virtualized high-volume tables, bounded memory during long replay, canvas render time budget, no interaction stalls during replay, and visible degraded states for disconnected or slow streams.
14. Add reliability tests beyond correctness: replay at 1x, 5x, and 20x speed; disconnect/reconnect frontend clients; restart the backend mid-stream; corrupt import records; kill external decoder processes; fill queues; and verify graceful degradation.
15. Decide packaging in Phase 1 or Phase 2. Record whether the dev target is separate backend/frontend processes, and whether packaged AeroHub runs as a console app, tray-hosted local service, Windows service, or bundled backend serving the React build.

**Further Considerations**

1. Choose real-time transport early: SignalR is idiomatic for .NET + React and good for events; raw WebSockets may be better for high-rate binary spectrum frames. Recommended: SignalR for control/events and a binary WebSocket endpoint for spectrum/waterfall if profiling demands it.
2. Choose map strategy before implementing aircraft track UI. Recommended starter: table plus simple map placeholder; add Leaflet/MapLibre once track contracts settle.
3. Decide whether the first decoder vertical slice is ACARS text parsing or ADS-B tracking. Recommended: ACARS first for message contracts, then ADS-B for aircraft track/map workflow.