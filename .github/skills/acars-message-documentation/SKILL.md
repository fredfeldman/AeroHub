---
name: acars-message-documentation
description: "Use when parsing ACARS text payloads, decoding ACARS labels and sublabels, handling OOOI/position/weather/maintenance messages, or integrating ACARS application parsing with HFDL, VDL, VHF, or SATCOM in ExpandedSDR."
---

# ACARS Message Documentation

## Purpose

Use this skill for the application-layer portion of ACARS decoding after a transport
such as HFDL, VDL, VHF ACARS, or SATCOM has produced a validated message payload.
It is grounded in the Airframes community research repository:

- https://github.com/airframesio/acars-message-documentation
- https://raw.githubusercontent.com/airframesio/acars-message-documentation/main/README.md
- Related decoder project: https://github.com/airframesio/acars-decoder-typescript

The repository is a collaborative research notebook. Its purpose is to organize
observations that can improve ACARS decoder libraries. Treat each label document as
evidence with a confidence level, not as a universal standard unless independently
confirmed.

## Layer boundary

ACARS application parsing belongs after transport and PDU validation:

`radio samples -> demodulator -> link PDU/FCS -> ACARS envelope -> label/sublabel parser -> typed application data`

For this workspace:

- `ExpandedSDR.Core/Decoding/` should own transport-independent ACARS records and parsers.
- HFDL MPDU/LPDU/HFNPDU parsing remains separate from ACARS application parsing.
- `ExpandedSDR.ViewModels/` should expose normalized messages and positions, not parse raw
  labels inside WPF controls.
- A single ACARS parser must be reusable by HFDL, VDL, and future SATCOM adapters.

Do not pass an unvalidated byte stream directly to an application parser. Preserve the
raw payload and validation status alongside normalized fields.

## Repository structure and confidence

The upstream repository organizes research by ACARS label, with optional sublabel and
preamble documents. Examples in the current index include:

- `20/POS` for position-related content
- `2L/DAT-Jetstar` for Jetstar Australia OOOI data
- `30/forward-slash-EA`
- `40` for ground-to-air free text, clearance, operations, and weather-related traffic
- `44` for OOOI plus positions
- `4J` for position and weather
- `5Z/forward-slash`
- `B6/forward-slash` and `B9/forward-slash`
- `H1` with `#CFB`, `#CFB.01`, `FPN`, `POS`, and Qantas-specific documents
- `MA` for media advisories, apparently MIAM-related
- `SQ` for squitters/transponder pings

The index is a routing table, not a complete parser specification. A label can have
multiple airline, application, or preamble variants. Parse the stable prefix first,
then dispatch to a label/sublabel/preamble handler.

Use explicit confidence states:

- `Observed`: seen in a capture but not generalized.
- `Candidate`: a proposed field layout supported by limited examples.
- `Confirmed`: independently verified across multiple examples or a trusted decoder.
- `Unknown`: preserve the raw text rather than guessing.

Never silently turn an `Observed` or `Candidate` layout into a confirmed aircraft
position.

## Normalized ACARS envelope

A useful transport-neutral model should retain at least:

- receive timestamp
- transport/source: HFDL, VDL, VHF ACARS, SATCOM, or unknown
- channel frequency and frequency offset when available
- signal level/SNR when available
- aircraft address and address type
- acknowledgement or retry/status indicators
- mode and label
- optional sublabel
- optional preamble/application discriminator
- block sequence/control fields when provided by the link decoder
- raw payload bytes and decoded text
- parsed application object, if recognized
- parser confidence and warnings
- FCS/CRC/link-validation status

Do not use the label alone as the message type. Include direction, sublabel,
preamble, and payload shape in the dispatch key.

## Parsing workflow

1. Confirm link-layer validity before application parsing.
2. Decode the ACARS character representation without discarding control characters
   prematurely.
3. Identify the label and direction.
4. Check for a documented sublabel or preamble discriminator.
5. Select the narrowest parser supported by the evidence.
6. Parse fixed fields with bounds and character validation.
7. Parse numeric values with explicit units and missing-value handling.
8. Preserve the original text and unconsumed suffix.
9. Emit warnings for malformed or partial messages rather than throwing away the frame.
10. Add a fixture from the exact label variant before promoting a parser.

Unknown labels should still produce a valid `AcarsMessage` record with raw content.
A decoder that displays unknown traffic is more useful than one that drops it.

## Application families

The upstream index indicates several recurring ACARS application families:

### Position and OOOI

Position and OOOI messages can appear under different labels and airline-specific
layouts. OOOI means Out, Off, On, In and commonly carries operational event times.
Do not assume a label such as `44` or `2L` has one global fixed layout; route through
variant-specific parsers and include the airline or preamble where available.

Normalize positions as:

- latitude and longitude in decimal degrees
- altitude with an explicit unit and source field
- timestamp with timezone/UTC interpretation recorded
- optional track, groundspeed, vertical rate, and accuracy
- aircraft identifier plus whether it is an ICAO address, registration, or dynamic
  HFDL aircraft ID

Reject impossible coordinates and flag stale or ambiguous timestamps.

### Weather and operational data

The repository index associates labels such as `4J` and `40` with position/weather,
ground-to-air clearance, operations, and weather information. Keep weather, NOTAM,
clearance, maintenance, and free text in separate typed result categories even when
they share a label family.

### Squitters and transponder pings

`SQ` is indexed for squitters/transponder pings. These are not ordinary text messages;
represent them as control/status events and preserve their raw content.

### Media/MIAM and reassembly

`MA` is associated with media advisories and appears MIAM-focused. Large ACARS or
MIAM content may require block reassembly before application parsing. Keep reassembly
state keyed by transport/session/message identifiers, enforce size/time limits, and
emit partial-message diagnostics when blocks are missing.

## Research discipline

When adding a parser based on the upstream repository:

- link the exact label document in the code or test documentation
- record whether the layout is observed, candidate, or confirmed
- add multiple fixtures when the repository shows airline-specific variants
- preserve bytes/text that the parser does not understand
- avoid turning comments or community notes into protocol constants without tests
- distinguish a preamble marker from an ACARS label and from an application subtype

The upstream repository says confirmed research is compiled into documentation later.
Mirror that discipline in this codebase: experimental parsers should be isolated and
clearly marked until their fixtures support promotion.

## Testing strategy

Every parser should have focused tests for:

- known-good complete payload
- truncated payload
- invalid characters/control fields
- missing or unknown sublabel
- variant preamble
- incorrect numeric fields and units
- extra trailing text preservation
- conflicting aircraft identifiers
- position bounds and timestamp handling

Use golden fixtures that assert both normalized fields and raw preservation. For HFDL,
keep the application parser tests independent from demodulation so ACARS parsing can be
verified with text/byte fixtures even when a live RF capture has no valid FCS yet.

## License and attribution

The upstream repository is a community research resource. Use its factual observations
and linked references, but do not copy repository prose or example implementations
without checking their license. Keep new parser code original, cite the source label
document, and preserve attribution in documentation where a field mapping depends on
that research.
