# ADS-B Track Import

Sprint 6 adds the first aircraft-track producing feature. ADS-B/readsb-style decoded data imports create normalized aircraft tracks without routing surveillance data through ACARS parsing.

## Implemented Import Source

- Import ID: `local-adsb-readsb-json`
- Source format: `application/x-ndjson; domain=adsb-readsb`
- Fixture: `fixtures/imports/adsb/local-readsb-aircraft.ndjson`
- API trigger: `POST /api/imports/local-adsb-readsb-json/start`
- Track API: `GET /api/aircraft`

## Imported Fields

The starter ADS-B fixture accepts readsb/dump1090-style aircraft records with:

- `hex`: six-character ICAO aircraft address.
- `flight`: callsign/flight ID.
- `lat` and `lon`: position in decimal degrees.
- `altBaro`: barometric altitude in feet.
- `gs`: ground speed in knots.
- `track`: track in degrees.
- `seenPos`: seconds since the position was seen.
- `messages`: source message count.
- `rssi`: source signal level.
- `sourceApp`, `sourceFormat`, `sourceVersion`, and `originalTimestampUtc`.

## Track Model

Aircraft tracks preserve:

- aircraft identifier
- callsign
- position
- altitude
- ground speed
- track
- source ID and source type
- confidence
- correlation group ID
- stale-state flag
- import provenance and raw record reference

## Validation and Quarantine

ADS-B import records are quarantined when they are malformed, duplicated, stale, or physically invalid.

Current ADS-B-specific diagnostics:

- `IMPORT_SCHEMA_MISMATCH`: missing or invalid six-character aircraft address.
- `IMPORT_DUPLICATE_RECORD`: duplicate aircraft address in the same import run.
- `IMPORT_ADSB_STALE`: position age exceeds the starter stale threshold.
- `IMPORT_ADSB_INVALID_POSITION`: latitude or longitude is out of bounds.

The track store also expires current tracks after 15 minutes without a fresher update.

## UI

The React Aircraft panel shows:

- current aircraft track count
- a simple map placeholder
- virtualizable track-list structure for callsign, ICAO, confidence, source, position, altitude, speed, heading, provenance, and correlation group

This is intentionally a starter view. A real map library can be added after track contracts and storage are stable.