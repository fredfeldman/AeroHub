---
name: drone-remote-id
description: "Use when receiving, decoding, importing, or displaying Drone Remote ID (ASTM F3411 / applicable regional profiles) in AeroHub, including Bluetooth and Wi-Fi broadcast reception, network-fed reports, serial/operator identity, location freshness, altitude references, and commercial UAS tracking."
---

# Drone Remote ID

## Purpose

Use this skill for cooperative UAS identification and location reports. Remote ID is
not ADS-B: a Remote ID serial number is not an ICAO address, the report formats and
transports differ, and a Remote ID position must not be routed through an ADS-B
decoder or keyed as if it were an aircraft hex address.

Keep the processing boundary explicit:

`RF or network input -> transport/frame decode -> Remote ID message validation -> normalized report -> UAS track`

AeroHub currently accepts decoded NDJSON reports through
`RemoteIdDroneImportRecord` and `local-remote-id-json`, retains individual
`RemoteIdObservation` records, and projects position-bearing reports into the
shared aircraft track store. Recent observations are available at
`GET /api/remote-id/observations` and via SignalR observation events. The bounded
observation window is in memory and holds at most 1,000 entries. The bundled
fixture is demonstration data; it does not receive Bluetooth or Wi-Fi broadcasts.
Add and test a receiver/decoder adapter before describing a deployment as live
Remote ID tracking.

## Message and transport model

Treat the broadcast transport separately from the Remote ID message semantics. A
receiver may deliver Bluetooth advertising data or Wi-Fi Neighbor Awareness
Networking / beacon data; a network service may deliver already-decoded reports.
Record the actual receiver, transport, channel/frequency when known, receive time,
and raw-frame reference. Do not assume one transport or one report equals one
complete aircraft observation.

Preserve and distinguish applicable message categories, including:

- Basic ID: one or more aircraft identifiers and their ID type.
- Location/Vector: position, speed, direction, and vertical velocity when present.
- System: operator location/radius and aircraft/operator classification when present.
- Operator ID and Self-ID text when present.
- Authentication or other profile-specific data when supported.

Follow the applicable ASTM F3411 edition and regional profile for exact wire
formats, field encodings, and validation. Do not infer absent fields or interpret
reserved values as valid data. Keep raw frames and decoder warnings available for
diagnostics, subject to storage and privacy policy.

## Identity and privacy

Keep aircraft serial, session ID, registration ID, operator ID, and transport
address in separate fields. They have different meanings and may have different
lifetime or privacy properties. Never use a transient Bluetooth/Wi-Fi address as a
stable aircraft identity unless the profile explicitly defines it as such.

Correlate reports only when their identifiers and profile rules support it. A
serial number can identify an aircraft, but it does not prove the operation is
commercial. Set operation type from an explicit, trusted decoded field or source
metadata; otherwise leave it unknown. Do not infer a person's identity or operator
details from a device identifier.

Minimize retention and display of operator or personal-location information. Keep
access, export, retention, and raw-frame behavior aligned with applicable privacy
rules and local regulations. Avoid presenting Remote ID reception as authorization
to intercept, identify, or track a person.

## Normalized report and track

Preserve, where supplied:

- stable aircraft identifier and its identifier type
- operator ID and its provenance, separately from aircraft identity
- operation category and whether it was explicit or inferred by an upstream source
- latitude/longitude, coordinate reference, timestamp, and position source
- horizontal speed/direction and vertical speed with units
- altitude value, units, and vertical reference
- message category, receiver/transport metadata, receive timestamp, and raw reference
- validation result, parser confidence, warnings, and source provenance

Altitude deserves special care. Remote ID profiles can express height relative to
the takeoff point or a geodetic reference, and upstream formats may use different
vertical datums. Do not silently treat an unqualified altitude in meters as
barometric altitude or mean-sea-level altitude. AeroHub's current shared
`AircraftTrackSnapshot` exposes `AltitudeFeet` without a vertical-reference field;
when integrating reports whose reference is not equivalent, preserve the original
value/reference in an appropriate contract extension or keep it out of that field
until the model can represent it without ambiguity.

Likewise, do not place a drone's operation type into an ADS-B emitter category, or
an operator identifier into a civil registration field. Keep Remote ID metadata
explicit in the contract.

## Freshness and validation

Validate before updating track state:

- required UAS identity and valid message/profile encoding
- latitude and longitude range, decoded coordinate precision, and invalid/sentinel
  values defined by the applicable profile
- timestamp freshness, clock skew, and ordering against the current track
- altitude, speed, direction, and vertical-speed ranges with their units and
  references
- message/frame integrity and any profile-required authentication checks
- duplicate source observation IDs and conflicting reports without discarding
  useful raw evidence

Use receiver receive time separately from the report's source timestamp. Reject or
quarantine stale/out-of-order reports according to configured policy; do not let a
stale report refresh a live track. Keep thresholds configurable where different
receiver and network-feed latencies require different treatment. Do not mark a
report authenticated merely because its JSON schema is valid.

## AeroHub integration

- Keep Remote ID contracts in `AeroHub.Contracts` and receiver/decoder adapters in
  `AeroHub.Infrastructure`; keep transport decoding separate from UI code.
- Reuse `IAircraftTrackStore` only with an unambiguous `RID:`-style identifier and
  explicit Remote ID metadata. Preserve ingestion provenance and raw references.
- Keep the existing aircraft REST and SignalR track events if the shared track
  contract represents the report correctly; add dedicated query/filter APIs only
  when consumers need a distinct lifecycle or retention policy.
- Keep import diagnostics for malformed, duplicate, stale, invalid-position, and
  out-of-order records. Do not route Remote ID through ACARS parsing.
- Retain each decoded message as an observation even if it has no position yet.
  Deduplicate only when a source supplies a stable observation ID; matching
  payload values alone do not prove two broadcasts are duplicates.
- Frontend labels must distinguish Remote ID serial/operator values from ICAO,
  callsign, registration, and ADS-B-derived classification. Do not label every
  Remote ID track "commercial"; use an explicit operation category or show it as
  unknown.
- Update fixtures, contract documentation, retention/export behavior, and tests
  when the record schema changes.

## Verified DroneAware Node Receiver Transports

The [DroneAware Node release repository](https://github.com/fduflyer/DroneAware-Node-Releases)
documents a Raspberry Pi receiver with two capture paths:

- BLE Remote ID service data using service UUID `0xFFFA`.
- Wi-Fi Beacon vendor information elements (OUI `FA:0B:BC`) and Wi-Fi NAN action
  frames (OUI `50:6F:9A`), captured with a monitor-mode-capable adapter.

Use its documented local output as an adapter input, not as an assumed AeroHub API.
The local publisher emits one JSON object per decoded message event; a record can
contain only a subset of a drone's state because Basic ID, Location/Vector, System,
and other messages arrive separately. The README documents these consumer options:

- UDP JSON lines, default destination `255.255.255.255:9999`; unicast targets can
  be configured on the node.
- If its optional local Web UI is installed, `GET /api/detections` supplies a
  current snapshot and `GET /events` supplies Server-Sent Events. Both are
  read-only and documented as unauthenticated LAN services.
- On the node itself, `/run/droneaware/detections.jsonl` is a rolling local file;
  it is not a remote integration endpoint.

The documented flat event shape includes `t` (Unix seconds), `mac` (observed
source MAC), `radio`, `rssi`, `channel`, `type`, `lat`, `lon`, `alt`, `speed`,
`hdg`, and `id`. Additional decoded fields are also included, such as `id_type`,
`ua_type`, `height_agl`, and operator-location fields. The README describes `alt`
as geodetic altitude in meters; `height_agl` is a separate field. Preserve both
and their references rather than mapping height AGL into AeroHub's unqualified
`AltitudeFeet`. `id` is the UAS ID from a Basic ID event and can be null on other
message types; `mac` identifies the observed radio source and must not silently
replace the UAS ID as a durable identity.

For a LAN adapter, prefer the HTTP snapshot plus SSE when reliable connection
management and initial state are needed. If using UDP, expect lossy delivery and
provide a snapshot/reconciliation path where possible. Parse every line as
independent input, tolerate optional fields, merge compatible message types using
their actual identifiers and timestamps, and retain the original event for
diagnostics. Treat the optional Web UI API as trusted-LAN-only unless protected by
an authenticated proxy or equivalent network controls.

This repository is a receiver interoperability reference, not code to copy. Its
repository states that its software is under the DroneAware Feeder Node Software
License and all rights are reserved. Review that license before reusing any code,
binary, or other protected material; keep AeroHub's adapter implementation
independent unless the required permission and terms are established.

## Validation tests

Cover at least:

- each supported message family and transport/profile dispatch
- known-good records and malformed, truncated, or invalid-encoding frames
- identifier types, multiple IDs, and correlation across report messages
- position, altitude-reference, direction, speed, and vertical-speed decoding
- invalid coordinates, sentinel values, stale timestamps, and clock skew
- duplicate and out-of-order updates without refreshing the live track
- authentication success, failure, and unsupported authentication behavior, where
  authentication is implemented
- provenance, privacy-sensitive fields, map/list labeling, and track expiry

Use independent fixtures and implement from the applicable standards. If using a
third-party decoder as an interoperability oracle or source, review its license and
do not copy implementation code without satisfying its license terms.