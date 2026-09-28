# Storage, Retention, and Query Reliability

Sprint 10 adds a local JSONL-backed record store under the existing runtime pipeline. It is intentionally simple and inspectable while the data model is still changing.

## Storage Decision

The starter storage technology is JSON Lines files with in-memory indexes loaded at startup.

This keeps early fixtures, exports, and debugging easy while preserving the option to move to SQLite or another indexed store after the schema stabilizes.

## Stored Records

The current store persists:

- normalized aviation messages
- aircraft track snapshots
- import diagnostics

The current runtime path writes to storage through the message bus, aircraft track store, and import diagnostic path. Native fixture replay, decoded-data imports, external decoder imports, ADS-B tracks, ADS-C tracks, and import diagnostics now share this persistence layer.

## Files

The runtime store uses:

- `messages.jsonl`
- `aircraft-tracks.jsonl`
- `import-diagnostics.jsonl`

Exports copy the current snapshot into an `exports/<name>` folder with the same file names.

## API

- `GET /api/storage`: storage counts, byte usage, root path, and timestamp.
- `POST /api/storage/retention/apply?messageDays=30&trackDays=30&diagnosticDays=14`: apply retention cleanup.
- `POST /api/storage/export?exportName=manual-snapshot`: export replayable JSONL files.

## Retention

Current retention policies are day-based:

- messages by `ReceivedAtUtc`
- tracks by `UpdatedAtUtc`
- diagnostics by `OccurredAtUtc`

Retention rewrites the JSONL files after removing expired records.

## Query Reliability

The store maintains in-memory indexes for current process queries:

- message IDs for deduplication
- aircraft identifier for latest track replacement
- diagnostic timestamp ordering for recent diagnostics

This is enough for Sprint 10 replay and UI workflows. Larger data volume should move indexes into a dedicated storage engine during a future hardening pass.

## Tests

Storage tests cover:

- message deduplication by ID
- reload from disk
- latest-track replacement
- stale retention cleanup
- export and replay from exported JSONL files