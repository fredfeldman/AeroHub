# Commercial Drone Tracking

AeroHub accepts decoded ASTM F3411-style Remote ID location reports as normalized drone tracks. Remote ID is kept distinct from ADS-B: drone serial numbers are not ICAO addresses, and their positions do not pass through the ADS-B importer.

## Sample Import

- Import ID: `local-remote-id-json`
- Source format: `application/x-ndjson; domain=remote-id`
- Fixture: `fixtures/imports/remote-id/commercial-drone-sample.ndjson`
- API trigger: `POST /api/imports/local-remote-id-json/start`
- Tracks: `GET /api/aircraft` and the existing `aircraft.updated` / `aircraft.snapshot` events

The fixture is demonstration data, not a live receiver. Live tracking requires an external Remote ID receiver or feed adapter that supplies decoded location reports in this record shape.

## Record Fields

- `serialNumber`: Remote ID aircraft serial; required and used to correlate track updates.
- `operatorId`: Remote ID operator identifier, when broadcast.
- `operationType`: operation category such as `Commercial`; it is not inferred from the identifier.
- `latitude` and `longitude`: position in decimal degrees.
- `altitudeMeters`: altitude in meters, normalized to feet in the shared aircraft track.
- `speedMetersPerSecond`: horizontal speed in meters per second, normalized to knots.
- `directionDegrees`: direction of travel in degrees.
- `seenSeconds`: age of the location report.
- `sourceApp`, `sourceFormat`, and `originalTimestampUtc`: optional source provenance.

Tracks use a `RID:`-prefixed identifier and retain the Remote ID serial, operator ID, operation type, source provenance, and raw record reference. They appear in the shared aircraft map and in the Commercial Drone Tracking panel.

## Validation

Malformed JSON, missing serial numbers, duplicate serials within one import, reports older than 120 seconds, and out-of-range coordinates are quarantined with import diagnostics. Existing track-store ordering and expiry rules apply to accepted updates.