# ACARS Parser Starter

Sprint 3 adds a conservative ACARS application parser. It is intentionally a starter parser: it extracts stable envelope fields, preserves raw payloads, and emits warnings instead of dropping unknown or malformed traffic.

## Current Behavior

1. Accepts a validated ACARS text payload from fixture replay, imported decoded data, or a future transport adapter.
2. Extracts a two-character label when present.
3. Extracts a candidate sublabel or preamble from the second token.
4. Extracts a likely aircraft identifier from registration-like or ICAO-like tokens.
5. Classifies known starter label families with conservative confidence.
6. Preserves raw payload text and unconsumed text.
7. Emits warnings for missing labels, unsupported characters, and unknown labels.

## Starter Label Families

| Label | Category | Confidence |
|---|---|---|
| `2L` | OOOI/position candidate | Candidate |
| `44` | OOOI/position candidate | Candidate |
| `4J` | Position/weather candidate | Candidate |
| `40` | Operational/free text candidate | Candidate |
| `H1 POS` | Position candidate | Candidate |
| `H1 #DFB` | Weather/operational observed | Observed |
| `H1` | H1 application observed | Observed |
| `SQ` | Squitter/status observed | Observed |
| `MA` | Media/MIAM observed | Observed |

Unknown labels still produce normalized messages with `Unknown` confidence and an `ACARS_UNKNOWN_LABEL` warning.

## Fixtures

- `fixtures/acars/local-vhf/oooi-candidate.txt`
- `fixtures/acars/local-vhf/weather-observed.txt`
- `fixtures/acars/local-vhf/unknown-label.txt`

## Local Frequencies

The Sprint 3 fixture/source profile seeds use the local ACARS frequencies recorded in `docs/local-frequencies.md`:

- `136.800 MHz`
- `136.975 MHz`
- `136.650 MHz`

## Reliability Rules

1. Do not use ACARS label alone as a final operational message type.
2. Do not silently promote `Observed` or `Candidate` layouts to confirmed positions.
3. Keep transport validation separate from ACARS application parsing.
4. Preserve unknown labels and malformed payloads for operator review.
5. Imported decoded ACARS data must use the same normalized shape as native decoder output while retaining import provenance.