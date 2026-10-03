# Commercial Drone Tracking

AeroHub accepts decoded ASTM F3411-style Remote ID location reports as normalized drone tracks. Remote ID is kept distinct from ADS-B: drone serial numbers are not ICAO addresses, and their positions do not pass through the ADS-B importer.

## Sample Import

- Import ID: `local-remote-id-json`
- Source format: `application/x-ndjson; domain=remote-id`
- Fixture: `fixtures/imports/remote-id/commercial-drone-sample.ndjson`
- API trigger: `POST /api/imports/local-remote-id-json/start`
- Tracks: `GET /api/aircraft` and the existing `aircraft.updated` / `aircraft.snapshot` events
- Message observations: `GET /api/remote-id/observations?limit=100` and the `remote-id.observation` / `remote-id.observation.snapshot` SignalR events

The fixture is demonstration data, not a live receiver. Live tracking requires an external Remote ID receiver or feed adapter that supplies decoded location reports in this record shape.

## Record Fields

- `serialNumber` or `uasId`: Remote ID aircraft/session identity used to correlate observations; identifier type remains explicit.
- `operatorId`: Remote ID operator identifier, when broadcast.
- `operationType` and `operationTypeSource`: optional source-supplied category and its provenance; AeroHub does not infer commercial use from Remote ID identity.
- `messageType`: report family such as `Basic ID`, `Location/Vector`, or `System`.
- `latitude` and `longitude`: position in decimal degrees.
- `altitudeGeodeticMeters`, `altitudeBarometricMeters`, and `heightAboveGroundMeters`: distinct altitude values with `altitudeReference`; they are not collapsed into generic aircraft feet.
- `speedMetersPerSecond`: horizontal speed in meters per second, normalized to knots.
- `directionDegrees`: direction of travel in degrees.
- `verticalSpeedMetersPerSecond` and the optional accuracy fields: measurement details retained with the observation.
- `operatorLatitude`, `operatorLongitude`, `areaCount`, `areaRadiusMeters`, `areaCeilingMeters`, and `areaFloorMeters`: optional System-message fields.
- `selfIdText`, `authenticationStatus`, and `authenticationVerifiedBySource`: optional message details. Source-reported authentication is not independently verified by AeroHub.
- `radio`, `rssi`, `channel`, `receiverId`, and `sourceMac`: reception metadata; source MAC is not used as a durable aircraft identity.
- `seenSeconds`: age of the location report.
- `sourceApp`, `sourceFormat`, and `originalTimestampUtc`: optional source provenance.

Each decoded message is retained as a separate observation, including valid messages that have no position yet. The in-memory store keeps at most 1,000 recent observations. Tracks use a `RID:`-prefixed identifier and retain selected latest metadata; they appear in the shared aircraft map and Commercial Drone Tracking panel. UAS ID, session/serial, operator ID, observed radio MAC, and ADS-B fields remain distinct.

## Validation

Malformed JSON, missing UAS identity, duplicate source observation IDs, reports older than 120 seconds, and out-of-range coordinates are quarantined with import diagnostics. Repeated reports without a source observation ID are retained rather than deduplicated by matching values. Existing track-store ordering and expiry rules apply to accepted position updates.