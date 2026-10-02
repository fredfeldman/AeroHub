# AeroHub API Contract Starter

This document is the editable contract sketch for Sprint 0. Sprint 1 and Sprint 2 should refine it into concrete OpenAPI and live-stream event contracts.

## Workload Split

- REST: configuration, queries, source/import setup, decoder commands, settings, and health snapshots.
- SignalR: status events, message events, aircraft-track updates, import diagnostics, decoder diagnostics, and normal operator events.
- Optional binary WebSocket: high-rate spectrum and waterfall frames if profiling shows SignalR/JSON is too expensive.

## REST Resources

- `GET /api/health`: backend status, version, uptime, active sources, active decoders, active imports, storage state.
- `GET /api/sources`: configured signal sources and capabilities.
- `POST /api/sources/{sourceId}/start`: start a signal source.
- `POST /api/sources/{sourceId}/stop`: stop a signal source.
- `GET /api/imports`: configured decoded-data import sources and current state.
- `POST /api/imports/{importId}/start`: start an import source.
- `POST /api/imports/{importId}/stop`: stop an import source.
- `GET /api/decoders`: decoder catalog, capabilities, supported transports, concurrency rules.
- `POST /api/decoders/{decoderId}/start`: start a decoder.
- `POST /api/decoders/{decoderId}/stop`: stop a decoder.
- `GET /api/messages`: query normalized messages.
- `GET /api/aircraft`: query current aircraft tracks.
- `POST /api/imports/local-remote-id-json/start`: import the bundled Remote ID sample into the shared aircraft-track store.
- `GET /api/settings`: retrieve settings.
- `PUT /api/settings`: update settings.

## Live Events

- `health.updated`: backend health and stream metrics changed.
- `source.updated`: signal source state changed.
- `decoder.updated`: native decoder state changed.
- `import.updated`: decoded-data import source state changed.
- `import.diagnostic`: accepted, rejected, quarantined, malformed, duplicate, or out-of-order import record.
- `message.received`: normalized aviation message received from native decoding, external process output, or import.
- `message.snapshot`: recent normalized aviation messages sent to a single SignalR caller.
- `aircraft.updated`: aircraft track or observation changed.
- `aircraft.snapshot`: current non-stale aircraft tracks sent to a single SignalR caller.
- Remote ID tracks are included in `GET /api/aircraft` and the `aircraft.updated` / `aircraft.snapshot` events. Their identifiers use the `RID:` prefix and include Remote ID serial, operator, and operation-type metadata.
- `spectrum.frame`: reduced FFT/spectrum frame.
- `waterfall.rows`: one or more coalesced waterfall rows.
- `stream.metrics`: synthetic or live stream metrics changed.
- `external.snapshot`: external decoder process snapshots sent to a single SignalR caller.
- `external.updated`: external decoder process state changed.
- `external.diagnostic.snapshot`: external decoder diagnostics sent to a single SignalR caller.
- `external.diagnostic`: external decoder diagnostic emitted.
- `wefax.state`: WEFAX decoder state changed.
- `wefax.lines`: one or more WEFAX image lines plus decoder state.
- `warning.raised`: parser, stream, source, storage, or process warning.

## Sprint 2 Implemented Endpoints

- `GET /api/health`
- `GET /api/messages/recent?limit=50`
- `GET /api/sources`
- `GET /api/imports`
- `GET /api/imports/diagnostics?limit=50`
- `POST /api/imports/{importId}/start`
- `GET /api/aircraft`
- `GET /api/decoders`
- `POST /api/fixtures/replay/acars`
- `GET /api/streams/synthetic/metrics`
- `POST /api/streams/synthetic/replay?speedMultiplier=1`
- `GET /api/external-decoders`
- `GET /api/external-decoders/diagnostics?limit=50`
- `POST /api/external-decoders/{processId}/simulate/{scenario}`
- `GET /api/wefax/state`
- `POST /api/wefax/replay?lineCount=96&partial=false`
- `POST /api/wefax/controls?isInverted=false&slantCorrection=0`
- `GET /api/storage`
- `POST /api/storage/retention/apply?messageDays=30&trackDays=30&diagnosticDays=14`
- `POST /api/storage/export?exportName=manual-snapshot`
- `GET /api/hardware-sources`
- `GET /api/hardware-sources/diagnostics`
- `POST /api/hardware-sources/start`
- `POST /api/hardware-sources/{sourceId}/stop`
- `POST /api/hardware-sources/{sourceId}/simulate/{scenario}`
- SignalR hub: `/hubs/aerohub`

## Contract Rules

1. Every event has an event name, schema version, sequence number, source ID, emitted timestamp, and correlation ID.
2. Every decoded/imported aviation record has provenance: native/import/external-process path, source app when known, source format, adapter version, original timestamp, receive/import timestamp, confidence, warnings, and raw record reference.
3. Reconnect behavior must be explicit. Clients should be able to request a snapshot and resume from a known sequence when feasible.
4. Breaking contract changes must be recorded here before implementation.

## Sprint 3 ACARS Message Detail

Normalized ACARS messages include an `acars` detail object:

- `label`: two-character ACARS label when present.
- `sublabel`: candidate sublabel when the second token is alphanumeric.
- `preamble`: candidate preamble when the second token starts with `#`.
- `applicationCategory`: conservative category from the starter parser.
- `decodedText`: current decoded text, preserving raw payload text.
- `unconsumedText`: remaining text after the label when available.

Unknown or malformed ACARS payloads are preserved as normalized messages with warnings instead of being dropped.

## Sprint 4 Import Diagnostics

Decoded-data import diagnostics include:

- `id`
- `occurredAtUtc`
- `importId`
- `severity`
- `code`
- `message`
- `rawReference`

The starter NDJSON importer emits diagnostics for schema mismatches, invalid JSON, oversized records, unsupported kinds, duplicate records, and out-of-order source timestamps.

## Sprint 5 Synthetic Stream Contracts

Synthetic RF replay emits reduced visualization products only. Raw IQ and audio remain backend-owned.

- `spectrum.frame`: a single reduced spectrum frame with source ID, sequence, timestamp, center frequency, span, and bins.
- `waterfall.rows`: one or more waterfall intensity rows with source ID, first sequence, timestamp, width, and row data.
- `stream.metrics`: state, speed multiplier, frame counters, dropped visualization frames, queue depth, slow client count, and last-generated timestamp.

The frontend keeps a bounded waterfall buffer and may discard stale visualization rows. Decoded/imported messages must not be silently dropped under the same policy.

## Sprint 6 Aircraft Tracks

ADS-B/readsb imports create aircraft track snapshots with:

- aircraft identifier
- callsign
- latitude and longitude
- altitude in feet
- ground speed in knots
- track in degrees
- source ID and source type
- confidence
- correlation group ID
- stale-state flag
- provenance

`GET /api/aircraft` returns current non-stale tracks. SignalR `aircraft.updated` broadcasts track changes and `aircraft.snapshot` returns the current set to one caller.

## Sprint 7 External Decoder Process Contracts

External decoder process snapshots include process ID, display name, executable name, state, detected version, restart count, restart limit, timestamps, and last error.

External decoder diagnostics include diagnostic ID, timestamp, process ID, severity, code, and message. The starter API simulates start, crash, timeout, malformed-output, and restart-limit scenarios before real process hosting is added.

## Sprint 8 Datalink and SATCOM Details

Normalized messages may include optional `datalink` and `satcom` detail objects.

`datalink` contains application type, direction, message reference, acknowledgement state, category, aircraft identifier, optional position fields, and raw text.

`satcom` contains satellite, channel, bearer, ground endpoint, and reassembly state.

The starter `sample-satcom-json` import source preserves CPDLC, ADS-C, and proprietary SATCOM records. ADS-C position observations can update aircraft tracks with source type `ADS-C over SATCOM` while remaining distinct from ADS-B observations.

## Sprint 9 WEFAX Contracts

WEFAX is modeled as an image-mode stream. `wefax.lines` carries one or more grayscale scan lines with sequence, line number, width, sync state, confidence, tone metrics, and pixel intensities. `wefax.state` carries IOC, line rate, polarity, slant correction, line count, sync state, and warnings.

## Sprint 10 Storage Contracts

Storage snapshots expose root path, message count, aircraft track count, import diagnostic count, byte usage, and check timestamp.

Retention applies day-based policies for messages, tracks, and import diagnostics. Export creates replayable JSONL snapshots for messages, tracks, and import diagnostics.

## Sprint 11 Hardware Source Contracts

Hardware source snapshots expose adapter name, input kind, owner decoder, capabilities, metrics, and last error.

Capabilities include sample-rate range, maximum bandwidth, frequency range, gain controls, exclusive-ownership requirement, stream framing, and tune-command acknowledgement behavior.

Metrics include configured sample rate, bandwidth, tuned frequency, buffers received, dropped buffers, reconnect count, signal level, SNR, queue depth, clipping state, and last buffer timestamp.