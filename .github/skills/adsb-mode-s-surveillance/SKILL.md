---
name: adsb-mode-s-surveillance
description: "Use when decoding or displaying ADS-B, Mode S, or Mode A/C surveillance messages, including 1090ES, DF formats, ICAO addresses, CPR position decoding, aircraft identification, velocity, altitude, squitter handling, and multilateration metadata in an SDR aviation app."
---

# ADS-B and Mode S Surveillance

## Purpose

Use this skill for aircraft surveillance messages received from 1090 MHz Mode S,
1090ES ADS-B, or simpler Mode A/C paths. It complements ACARS and HFDL skills:
ADS-B is primarily surveillance broadcast data, while ACARS/HFDL are datalink and
message transports.

Keep this boundary clear in the app:

`RF samples -> demodulator -> Mode S frame -> CRC/parity validation -> message parser -> aircraft state track`

Do not route ADS-B messages through an ACARS text parser. ADS-B payloads are compact
binary fields with strict bit layouts, different validation rules, and stateful CPR
position recovery.

## Message families

Handle these categories separately:

- Mode S all-call and surveillance replies
- Extended squitter ADS-B messages
- aircraft identification and category
- airborne position and surface position
- airborne velocity
- operational status and capability messages
- Mode A/C altitude or identity replies when available
- TIS-B, ADS-R, or MLAT-enriched records from external feeds

Do not assume every 112-bit frame is ADS-B. Decode the downlink format first, then
dispatch by DF, type code, and subtype.

## Normalized model

A normalized surveillance record should retain at least:

- receive timestamp
- source receiver, channel, frequency, and sample rate
- signal level, SNR, and frequency offset when available
- raw frame bits or bytes
- downlink format and type code
- ICAO address when known
- CRC/parity validation result
- callsign or flight ID
- altitude with source and units
- latitude/longitude with CPR method and freshness
- ground speed, airspeed, track, heading, or vertical rate
- emergency, alert, SPI, capability, or status flags
- parser warnings and confidence

Keep raw frames even when parity recovery is uncertain. A track display can tolerate
unknown fields; silent frame drops make decoder troubleshooting much harder.

## CPR and track state

ADS-B position decoding is stateful. Compact Position Reporting needs compatible odd
and even frames, timing checks, and latitude-zone validation. Preserve whether a
position came from global CPR, local CPR, surface CPR, external reference, or an
MLAT/feed source.

Reject impossible coordinates and stale odd/even pairs. Mark positions as unresolved
when only one half of a CPR pair has arrived.

## Validation

Tests should cover:

- DF and type-code dispatch
- CRC/parity pass, fail, and recoverable cases
- known-good aircraft identification
- airborne and surface CPR examples
- velocity subtype variants
- altitude encodings and invalid values
- duplicate frames and stale track expiry
- malformed, short, or bit-shifted frames

Use independent fixtures and original parser code. If referencing dump1090, readsb,
pyModeS, or other decoders for behavior, treat them as interoperability oracles and
check their licenses before copying any implementation detail.