# Decoded-Data Imports

Sprint 4 adds the first decoded-data import framework. Imports are treated as a first-class ingestion path equal to native decoding, but imported records still pass through validation, normalization, deduplication, storage/message-bus, live broadcast, and UI review.

## Implemented Adapter

The starter adapter is a file-based NDJSON importer:

- Import ID: `local-acars-ndjson`
- Source format: `application/x-ndjson; domain=acars`
- Fixture: `fixtures/imports/acars/local-acars.ndjson`
- API trigger: `POST /api/imports/local-acars-ndjson/start`

## Record Shape

Each NDJSON line may include:

- `rawPayload`: decoded ACARS payload text.
- `kind`: currently `Acars`.
- `sourceApp`: external application name when known.
- `sourceFormat`: source format name or media type.
- `sourceVersion`: source app or export version when known.
- `originalTimestampUtc`: timestamp assigned by the source application.
- `transport`: imported transport label.
- `frequencyMHz`: receive frequency when known.

## Validation and Quarantine

The import manager accepts valid ACARS decoded records and quarantines records that fail starter validation.

Current quarantine diagnostics:

- `IMPORT_SCHEMA_MISMATCH`: required fields are missing, such as `rawPayload`.
- `IMPORT_DUPLICATE_RECORD`: raw payload duplicates an earlier record in the same import run.
- `IMPORT_OUT_OF_ORDER`: source timestamp moves backward during a single import run.
- `IMPORT_RECORD_OVERSIZED`: record exceeds the starter size budget.
- `IMPORT_JSON_INVALID`: line is not valid JSON for the import schema.
- `IMPORT_UNSUPPORTED_KIND`: record kind is not supported by the starter importer.

Quarantined records are surfaced through REST and SignalR diagnostics. They do not crash the import run.

## API and Events

- `GET /api/imports`: current import source state.
- `GET /api/imports/diagnostics?limit=50`: recent import diagnostics.
- `POST /api/imports/{importId}/start`: start an import source.
- SignalR `import.updated`: import source state changed.
- SignalR `import.diagnostic`: import diagnostic emitted.
- SignalR `import.diagnostic.snapshot`: recent diagnostics sent to one caller.

## Provenance

Accepted imported messages use `IngestionPath.ImportedDecodedData` and preserve:

- source ID
- source name
- source app
- source format
- adapter name and version
- original timestamp
- import/received timestamp
- raw record reference

Native decoder output and imported output share the same normalized message shape, but the provenance path remains visible in API responses and the React UI.