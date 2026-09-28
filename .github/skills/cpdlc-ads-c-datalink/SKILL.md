---
name: cpdlc-ads-c-datalink
description: "Use when parsing or displaying CPDLC, ADS-C, FANS, or ATN aviation datalink messages, including controller-pilot clearances, contract-based position reports, message elements, acknowledgements, and correlation with ACARS, HFDL, VDL, or SATCOM transports."
---

# CPDLC and ADS-C Datalink

## Purpose

Use this skill for controller-pilot datalink communications and contract-based
surveillance messages carried by HFDL, VDL Mode 2, SATCOM, or ACARS-family transports.
CPDLC and ADS-C are application-level datalink content; the radio transport should
validate frames before this parser sees the payload.

Recommended layering:

`transport frame -> datalink payload -> CPDLC/ADS-C message -> normalized clearance/report`

Do not treat every HFDL or SATCOM payload as printable ACARS text. Some aircraft
operational messages are structured CPDLC or ADS-C content with acknowledgements,
message references, and encoded elements.

## Message categories

Keep these categories distinct in models and UI:

- CPDLC uplink clearances, instructions, requests, and free text
- CPDLC downlink responses, requests, acknowledgements, and reports
- ADS-C periodic, event, demand, and emergency contract reports
- FANS-style application messages carried through ACARS-compatible transports
- ATN B1/B2-style messages carried over VDL2 where supported
- unknown or proprietary airline/ATC application payloads

Preserve direction, message reference numbers, response requirements, and closure
state. A clearance text string alone is not enough for a useful operational record.

## Normalized model

A normalized CPDLC or ADS-C record should retain:

- receive timestamp and transport source
- aircraft identifier and address evidence
- ground facility or station identity when available
- transport validation state
- raw payload bytes and decoded text/fields
- application type: CPDLC, ADS-C, FANS, ATN, unknown
- direction: uplink, downlink, or unknown
- message/reference numbers and acknowledgement state
- message element identifiers and parameters
- normalized clearance/report category
- position, altitude, speed, route, intent, or weather fields when present
- parser confidence and warnings

For ADS-C positions, record contract type and trigger source. Do not merge ADS-C
position reports into ADS-B tracks without retaining their lower update rate and
different accuracy semantics.

## Parsing discipline

Prefer a conservative parser that preserves unknown elements over a parser that
guesses operational meaning. Structured datalink messages are safety-sensitive; mark
unknown, proprietary, or partial fields explicitly.

When adding support for a message element:

- cite the public standard, decoder fixture, or captured example used as evidence
- validate bounds and enumerations
- preserve unknown enumerated values
- separate display text from parsed operational fields
- test both uplink and downlink direction when applicable

## Testing strategy

Tests should cover:

- payload classification from HFDL, VDL2, and SATCOM adapters
- message reference and acknowledgement correlation
- CPDLC clearance/request element parsing
- ADS-C position/report normalization
- unknown element preservation
- malformed or truncated payloads
- duplicate and retransmitted messages
- correlation with aircraft tracks without losing source metadata

Use libacars, dumpvdl2, dumphfdl, or public fixture output as interoperability
references where appropriate. Do not copy implementation code without license review.