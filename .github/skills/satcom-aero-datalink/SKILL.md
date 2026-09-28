---
name: satcom-aero-datalink
description: "Use when working with aircraft SATCOM or Aero datalink messages, including Inmarsat/Iridium-style ACARS or CPDLC/ADS-C traffic, channel metadata, burst/message reassembly, aircraft identity correlation, and handoff to ACARS or CPDLC parsers."
---

# SATCOM Aero Datalink

## Purpose

Use this skill for aviation SATCOM datalink traffic that may carry ACARS, CPDLC,
ADS-C, airline operational data, or related aircraft communications over satellite
bearers. SATCOM is a transport and network path; application parsing should be handed
to ACARS or CPDLC/ADS-C parsers after validation and classification.

Keep the processing path explicit:

`RF/source capture -> SATCOM channel decoder -> bearer/session record -> payload classifier -> ACARS or CPDLC/ADS-C parser`

Do not assume SATCOM messages have the same timing, frequency, or station semantics
as HFDL or VDL2. Preserve satellite/channel metadata separately from aircraft message
content.

## Message families

Model these families independently:

- ACARS-over-SATCOM airline operations traffic
- CPDLC and ADS-C over FANS-style satellite paths
- network management, logon, or routing messages
- fragmented or reassembled payloads
- unknown proprietary payloads

The same application message may arrive over HFDL, VDL2, or SATCOM. Normalize the
application layer while retaining the transport path so users can inspect reliability,
latency, and duplicate delivery.

## Fields to preserve

A normalized SATCOM datalink record should retain:

- receive timestamp
- source device or feed
- satellite/channel/bearer metadata when known
- tuned frequency or feed channel
- signal level, SNR, burst timing, and frequency offset when available
- aircraft identifier and address evidence
- ground endpoint or network identity when available
- raw frame or payload bytes
- validation and reassembly status
- payload classifier: ACARS, CPDLC, ADS-C, management, unknown, corrupted
- downstream parser result and warnings

For fragmented payloads, keep reassembly state bounded by time, size, and source. Emit
partial-message diagnostics instead of silently discarding incomplete sequences.

## Integration guidance

SATCOM adapters should feed the same transport-neutral ACARS and CPDLC/ADS-C models
used by HFDL and VDL2. Avoid satellite-specific fields leaking into the application
parser APIs; attach them as source metadata.

When correlating with aircraft tracks, distinguish SATCOM-derived ADS-C positions from
ADS-B and HFDL-derived positions. They have different latency, accuracy, and update
patterns.

## Testing strategy

Tests should cover:

- payload classification and handoff
- fragmented message reassembly and expiry
- duplicate messages arriving over multiple transports
- unknown and proprietary payload preservation
- SATCOM metadata propagation
- malformed, partial, or validation-failed frames
- downstream ACARS and CPDLC parser integration with fixed fixtures

Use public decoder output or captured fixtures as interoperability references where
possible. Keep implementation original, and perform a third-party license review
before adapting code from any SATCOM decoder project.