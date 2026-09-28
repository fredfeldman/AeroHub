---
name: hfdl-airframes
description: "Use when working on HFDL reception, decoding, aircraft positions, ACARS over HF, dumphfdl integration, Airframes feeding, HFDL frequency selection, or Airframes REST/Socket.IO data in ExpandedSDR."
---

# HFDL and Airframes

## Purpose

Use this skill for HFDL work in ExpandedSDR: live HF reception, diagnostic replay,
aircraft position extraction, ACARS/ADS-C/CPDLC message handling, dumphfdl
interoperability, and optional Airframes feed/API integration.

Airframes is an authoritative source for operational HFDL usage, decoded-message
semantics, feeder integration, and public data access. Its public documentation is
not a complete HFDL physical-layer or ARINC 635 waveform specification. Do not infer
symbol framing, convolutional-code polynomials, interleaver direction, or FCS coverage
from the high-level Airframes pages. Use an authoritative protocol implementation or
specification for those details, and treat dumphfdl output as the interoperability
oracle when validating this app's in-process decoder.

The linked HFDL Specification Item-1c mirror is useful for protocol structure. Use it
as a reference for facts and independently implement the algorithms; do not copy its
document text, figures, or tables wholesale into the repository.

## Specification facts relevant to this decoder

The specification mirror identifies these physical/data-layer facts:

- The prekey segment contains 448 2-PSK symbols and lasts about 249 ms.
- The preamble segment contains 531 2-PSK symbols.
- A data segment contains either 72 or 168 data-segment frames, giving 1.8 s or
  4.2 s interleaver spans.
- Each data-segment frame contains 45 M-PSK symbols: 30 information-carrying data
  symbols and 15 known 2-PSK probe symbols.
- User-data modulation is data-rate dependent: 1800 bps uses 8-PSK with 3 coded
  chips per PSK symbol; 1200 bps uses 4-PSK with 2 chips; 600 and 300 bps use
  2-PSK with 1 chip. Gray mapping is used for the higher-order PSK cases.
- The documented data formation order is user data plus flush/fill bits, then coding,
  interleaving, PSK mapping, and scrambling.

These facts are more authoritative for frame extraction than the current heuristic
profile constants. In particular, do not treat all 45-symbol frames as data: the 15
probe symbols must be removed or modeled before deinterleaving/FEC. Do not equate
data rate with raw symbol rate or infer coding rate from modulation arity.

The mirror is a technical reference, not a license to copy its prose or artwork. Keep
new code original and cite the URL in comments or documentation when a protocol fact
is important.

## Source documentation

Primary sources consulted:

- HFDL overview: https://docs.airframes.io/docs/technology/hfdl/intro
- HFDL applications and utilities: https://docs.airframes.io/docs/technology/hfdl/apps
- dumphfdl installation and configuration: https://docs.airframes.io/docs/decoders/install-dumphfdl
- Decoder client overview: https://docs.airframes.io/docs/decoders/clients
- Feeding workflow: https://docs.airframes.io/docs/feeding/what
- Airframes API overview: https://docs.airframes.io/api
- API reference: https://docs.airframes.io/api-reference
- Realtime Socket.IO API: https://docs.airframes.io/api/realtime
- HFDL technical specification mirror: https://www.scribd.com/document/90805607/HFDL-Specification-Item-1c
- dumphfdl repository and README: https://github.com/szpajder/dumphfdl
- dumphfdl wiki: https://github.com/szpajder/dumphfdl/wiki

## Airframes facts to preserve

### What HFDL carries

Airframes describes HFDL as ACARS over HF for long-range air-to-ground links,
including oceanic, polar, and remote coverage. HFDL can carry:

- ordinary ACARS operational messages
- ADS-C position reports
- CPDLC communications
- weather, maintenance, flight-operation, and free-text data

Position extraction must therefore accept more than one message shape. A valid HFDL
frame does not necessarily contain a position, and a position report may be structured
as ADS-C rather than plain printable text.

### Reception model

Airframes documents the receiver chain as:

`antenna -> HF-capable SDR -> decoder -> structured message -> Airframes`

The documented practical requirements are:

- an HF-capable SDR or receiver
- a suitable HF antenna
- an HFDL decoder such as dumphfdl
- an active frequency list, because ground-station frequencies change with propagation,
time of day, and location

Airframes states that HFDL ground stations operate roughly across 2-22 MHz and that
a single receiver can monitor multiple frequencies when the SDR bandwidth permits it.
The current ExpandedSDR path is a Flex DAX audio path, so do not copy SoapySDR/RTL-SDR
commands into the Flex integration layer.

### Frequency operations

Use HFDLObserver or hfdl.observer for current ground-station and frequency activity:

- https://hfdl.observer/
- https://github.com/hfdl-observer/hfdlobserver

Airframes' documented example sets include frequencies such as 8942, 8957, 8927,
8912, and 8885 kHz for North Atlantic/Shannon coverage, and 8927, 8912, 8894,
8885, and 8834 kHz for North Pacific coverage. These are examples, not permanent
assignments. Do not hard-code them as universally active frequencies.

For ExpandedSDR live tests:

- tune the Flex slice in USB as appropriate for the local decoder chain
- treat the displayed dial frequency and HFDL carrier offset as separate values
- capture the exact audio sent to the HFDL modem
- record the tuned frequency, mode, sample rate, and selected decoder with the WAV
- prefer a current active frequency list over stale examples

## dumphfdl interoperability

Airframes identifies dumphfdl as its primary HFDL decoder client. It is a useful
external oracle when the in-process decoder is uncertain.

Documented dumphfdl characteristics:

- multichannel HFDL decoder
- receives ACARS, ADS-C, and CPDLC over HF
- accepts frequencies in kHz
- supports SoapySDR input
- requires an explicit sample rate
- supports station identifiers
- supports text, JSON, UDP, TCP, and ZeroMQ outputs

Representative documented command shape:

```text
dumphfdl --soapysdr driver=rtlsdr --sample-rate 250000 \
  --output decoded:json:udp:address=feed.airframes.io,port=5556 \
  --station-id MY-STATION-HFDL \
  8927 8912 8894 8885 8834
```

Do not run this command against the Flex-backed ExpandedSDR path without adapting
the input source. For a comparison oracle, use dumphfdl with independent hardware or
feed it a supported sample source, then compare decoded message content and metadata
with ExpandedSDR's replay output.

The documented output grammar is:

```text
--output <what>:<format>:<transport>:<parameters>
```

Examples from the docs:

- `decoded:json:udp:address=feed.airframes.io,port=5556`
- `decoded:text:file:path=/var/log/hfdl.log,rotate=hourly`
- `decoded:json:zmq:mode=server,endpoint=tcp://0.0.0.0:45556`

Keep raw decoder output and normalized application records separate. The decoder's
JSON is an interoperability boundary, not a reason to copy its internal implementation.

### dumphfdl as an oracle

The dumphfdl README describes a mature multichannel decoder with these useful
comparison capabilities:

- simultaneous channel decoding limited by available CPU and SDR bandwidth
- direct SoapySDR input
- prerecorded raw IQ input
- FFT channelization for wideband input
- ACARS multiblock and MIAM reassembly
- ground-station enrichment from a system table
- aircraft enrichment from a Basestation SQLite database
- aircraft-position extraction and Basestation output
- human-readable, JSON, and Basestation output formats

Its documented protocol coverage includes MPDU, LPDU, SPDU, HFNPDU Direct Link
Service messages, ACARS, ADS-C, CPDLC, and libacars-supported applications. Use
these message categories to expand ExpandedSDR's parser and position tests instead
of assuming every valid HFDL frame is printable text.

The dumphfdl header fields are a useful diagnostic comparison surface:

- receive timestamp
- channel frequency in kHz
- frame frequency offset
- signal level and noise floor in dBFS
- signal-to-noise ratio
- transmitted bit rate: 300, 600, 1200, or 1800 bps
- single- or double-slot frame length (`S`/`D`)

When comparing a capture, log equivalent fields in ExpandedSDR. A mismatch in bit
rate, slot length, channel frequency, or frequency offset is more actionable than a
generic FCS failure.

### IQ replay comparison

dumphfdl accepts raw IQ files using:

```text
dumphfdl --iq-file <file> --sample-rate <rate> --sample-format <U8|CS16|CF32> \
  --centerfreq <kHz> <hfdl-frequency-kHz> ...
```

The sample rate must be one the source actually delivered; an unsupported rate may
be silently rounded by hardware and makes the time scale invalid. `--centerfreq`
must match the SDR tuning used for the recording. This is distinct from the current
ExpandedSDR audio WAV fixture: a DAX USB-audio WAV is not automatically a valid
dumphfdl IQ input. Use an IQ capture or an independent dumphfdl-compatible source
for cross-decoder replay.

For debugging, dumphfdl documents `--raw-frames` and
`--output-corrupted-pdus`. Preserve corrupted frame output when investigating CRC
failures; filtering it out hides precisely the evidence needed to compare data
region, scrambling, and FEC hypotheses.

### Position and identity semantics

dumphfdl's documentation warns that many HFDL messages carry dynamic aircraft IDs,
not an ICAO hex code. Logon confirm messages establish the mapping from a ground
station's assigned aircraft ID to ICAO. Position feeds may therefore omit an aircraft
until that mapping has been observed. Positions can also be stale; the documented
Basestation path discards positions older than five minutes.

ExpandedSDR should keep these distinctions explicit:

- `AircraftId` is not automatically an ICAO address.
- An unvalidated position candidate must not be displayed as a confirmed aircraft
  identity.
- Position timestamps must be retained and checked for staleness.
- A local decoded position table may show candidates even when an external feed would
  suppress them because identity or freshness requirements are not met.

### System-table enrichment

dumphfdl ships a system table containing HFDL ground-station IDs, names, locations,
and assigned frequencies. It can update the in-memory table from over-the-air system
table requests and optionally save a new table file. This is a better source for
station-name enrichment than hard-coded frequency lists. Keep it separate from the
decoder's physical-layer state and cache it as operational metadata.

### Licensing boundary

dumphfdl is GPL-3.0 and includes code from other projects such as libfec, Rocksoft
CRC tooling, and csdr. Learn from its public behavior, documentation, output formats,
and protocol coverage, but do not copy its C implementation into ExpandedSDR unless
the repository's licensing decision explicitly permits that. Prefer an independent
implementation, an external dumphfdl process with a documented output adapter, or
test fixtures generated from dumphfdl.

## Airframes feed contract

Airframes documents feeder flow as decoded ACARS data plus metadata such as:

- receive frequency
- signal level
- station identifier
- decoded message content

The aggregator validates structure, deduplicates messages from multiple feeders,
enriches aircraft/airline/flight information, attempts further message decoding, and
publishes results through the web app, API, and tracking displays.

If ExpandedSDR later feeds Airframes:

- add a separate output adapter above the decoder/core layers
- never put HTTP, Socket.IO, or API-key handling in DSP classes
- make station identity and privacy explicit settings
- preserve raw diagnostics locally before transmitting normalized data
- use bounded queues and backpressure so network delays cannot block DAX audio
- never log API keys or bearer tokens

## Airframes REST API

The documented stable base URL is:

```text
https://api.airframes.io/v1
```

The docs state that public data endpoints may work anonymously, while an API key
in `Authorization: Bearer <key>` or `X-API-KEY` increases limits and attributes use.
API keys must remain outside source control and diagnostics logs.

Relevant documented endpoint groups include:

- `GET /v1/hfdl/ground-stations` for HFDL ground stations and active frequencies
- `GET /v1/messages` and `GET /v1/messages/{id}` for messages
- `GET /v1/flights/{id}/messages` for flight-associated messages
- `GET /v1/flights/{id}/positions` for position history
- `GET /v1/decode/positions/recent` for recent decoded positions
- `GET /v1/airframes/icao/{icao}` and related aircraft lookups
- `GET /v1/stations/ident/{ident}` for feeder station lookup

API conventions documented by Airframes:

- prefer the `/v1` prefix
- use `since` and `until` for ISO-8601 time ranges where supported
- respect pagination headers such as `X-Total-Count`, `X-Result-Count`, and page metadata
- respect `X-RateLimit-*` and `Retry-After` on HTTP 429
- propagate `X-Request-ID` into troubleshooting logs when useful
- use `ETag`/`If-None-Match` for cacheable polling
- expect errors shaped like `{ statusCode, message, error, timestamp }`

For current HFDL frequency display, prefer a short-lived cache of
`/v1/hfdl/ground-stations`; active frequencies are time- and propagation-dependent.

## Airframes realtime API

Airframes documents Socket.IO over WebSocket at:

```text
wss://ws.airframes.io
```

Use a Socket.IO client with WebSocket transport, not a raw WebSocket implementation.
Documented streams include:

- API-key-authenticated `feed:message` events for the user's own stations
- sampled global messages after `messages:sniff`
- station-specific updates after `station:monitor:start`

Important event shapes from the docs:

- `feed:authenticated` contains user identity and station summaries
- `feed:message` carries a full Message object
- `message` is the sampled global firehose event
- `station:monitor:data` contains a station, station id, and `newMessages`
- `feed:error`/`chat:error` report authentication and streaming errors

The global firehose is sampled and is not suitable for complete capture. Use the
own-station feed for complete messages from an authenticated feeder.

## ExpandedSDR architecture rules

Keep the existing layering:

- `ExpandedSDR.Core/Decoding/` owns HFDL DSP, framing, FEC, PDU validation, and
  protocol-independent decoded records.
- `ExpandedSDR.ViewModels/` owns HFDL state, collections, user commands, and UI
  projection.
- `ExpandedSDR/` owns WPF windows and controls.
- A future Airframes adapter belongs in a separate Core integration/service class or
  ViewModels manager, not in `HfdlBurstDemodulator`, `HfdlPayloadDecoder`, or a WPF view.

The current HFDL chain is:

`DAX audio -> burst detection -> timing/phase hypotheses -> A/M1 detection -> profile -> frame extraction -> deinterleave -> Viterbi/FEC -> PDU/FCS -> ACARS/ADS-C/CPDLC parsing -> aircraft positions`

At each stage log counts and quality rather than only final positions. A failure such
as `20 A locks, 13 M1 locks, 8 extracted frames, 4 FEC outputs, 0 valid FCS` is much
more useful than an empty aircraft table.

## Diagnostic workflow

When a live or replayed capture produces no positions:

1. Confirm the WAV is the exact mono modem input and that its sample rate is known.
2. Confirm burst count and A-sequence locks.
3. Compare carrier and timing hypotheses.
4. Confirm M1 profile and expected frame length.
5. Count extracted data regions.
6. Compare absolute and differential phase only as a diagnostic experiment.
7. Test interleave direction, bit order, inversion, data start, and FCS byte order
   against a known-good decoded frame or dumphfdl oracle.
8. Only promote a transform to the live path after it produces a valid FCS on a
   captured fixture and passes synthetic unit tests.
9. Parse positions only after PDU/FCS validation, unless the UI explicitly labels the
   result as an unvalidated candidate.

## Safety and data handling

HFDL carries aviation operational communications. Keep implementation and test work
focused on receiving, decoding, validating, and displaying data. Do not add transmit,
message injection, replay-over-the-air, or control-plane features.

Treat aircraft positions and message content as operational data. Avoid unnecessary
retention, redact sensitive fields in shared bug reports, and never commit recordings,
API keys, or private station credentials to the repository.
