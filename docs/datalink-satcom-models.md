# CPDLC, ADS-C, and SATCOM Models

Sprint 8 adds starter structured models for CPDLC, ADS-C, and SATCOM Aero datalink messages. These models preserve payloads and metadata without pretending the full operational parser exists yet.

## Implemented Records

- `DatalinkMessageDetails`: application type, direction, message reference, acknowledgement state, category, aircraft identifier, optional position fields, and raw text.
- `SatcomTransportMetadata`: satellite, channel, bearer, ground endpoint, and reassembly state.

These details are optional fields on `NormalizedAviationMessage`, so ACARS and ADS-B paths remain compatible while CPDLC/ADS-C/SATCOM records gain structure.

## SATCOM Fixture Import

- Import ID: `sample-satcom-json`
- Source format: `application/x-ndjson; domain=satcom-aero`
- Fixture: `fixtures/imports/satcom/sample-satcom.ndjson`
- API trigger: `POST /api/imports/sample-satcom-json/start`

The fixture includes:

1. CPDLC uplink instruction with reference `UM79`.
2. ADS-C periodic report with aircraft position.
3. Unknown/proprietary SATCOM management payload.

## Correlation

ADS-C position reports can contribute aircraft observations through `InMemoryAircraftTrackStore`, but they are not treated as ADS-B tracks. Sprint 8 uses source type `ADS-C over SATCOM` or `ADS-C over HFDL` and correlation group IDs such as `adsc-N701SA`.

## Reliability Rules

1. Preserve unknown/proprietary SATCOM payloads with raw text and warnings.
2. Preserve CPDLC/ADS-C message reference and acknowledgement state when present.
3. Keep SATCOM transport metadata separate from application details.
4. Do not merge ADS-C and ADS-B semantics; keep source type and provenance visible.
5. Treat the current CPDLC/ADS-C detail mapper as a structured placeholder until authoritative parser fixtures are added.