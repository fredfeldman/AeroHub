---
name: radiosonde-telemetry-reception
description: "Use when adding reception/decoding of weather-balloon radiosonde telemetry (Vaisala RS41/RS92, Meteomodem M10/M20, Graw DFM06/09/17, InterMet iMet, LMS6) to an SDR app: 400-406 MHz / 1680 MHz GFSK/2FSK demodulation, per-vendor frame sync and subframe parsing, GPS/PTU (pressure-temperature-humidity) telemetry normalization, and SondeHub/APRS upload integration."
---

# Radiosonde Telemetry Reception

## Purpose

Radiosondes are single-use, expendable telemetry transmitters carried aloft by weather
balloons. They broadcast GPS position/altitude plus pressure/temperature/humidity (PTU)
sensor data on a narrowband FM channel, roughly every 1-2 seconds, for the ~2 hour flight
until the balloon bursts and the sonde descends on a parachute. They are airborne moving
objects with a position history exactly like the ADS-B aircraft tracks this app already
models — treat a decoded sonde as a second "track type" that can reuse the same map,
position-cache, and trail infrastructure documented in `smart-sdr-architecture`, not as an
unrelated feature needing its own map stack.

Recommended processing boundary:

`IQ samples -> narrowband FM discriminator -> bit-clock recovery -> per-vendor sync-word correlation/auto-detect -> frame deinterleave + CRC/parity check -> subframe/calibration reassembly -> normalized telemetry record -> map track + optional SondeHub/APRS upload`

Keep steps left of "normalized telemetry record" in a decoder/core layer with no UI
dependency, mirroring the ADS-B and WEFAX decoder boundaries in this codebase. The
normalized record is what the UI, storage, and any upload integration should consume.

## Local references

`D:\source\repos\RadioSonde` is a real, actively-developed local reference: a Windows WPF
app (.NET 10, MVVM) that tunes an SDR (SoapySDR-abstracted BladeRF/RSPdx-R2/HackRF/AirSpy),
band-scans 400-406 MHz, and decodes radiosonde telemetry onto a live map. It does **not**
reimplement per-vendor frame decoding itself — it wraps the GPL-licensed `rs1729/RS`
decoder family (`rs41mod`, `dfm09mod`, `m10mod`, etc.) as child processes and streams IQ to
them over stdio, which is the pragmatic middle ground between "reimplement every vendor's
bit-level framing from scratch" and "link GPL code into the app." Treat it as an
architecture/behavior reference, not a source of code to copy — apply
`third-party-license-compliance` before pulling anything from its `SDRReferences/` clones
or the vendored `rs1729` sources under `native/` into this codebase.

Confirmed from the actual source and its `RadioSondePlan.md` build log:

- **Project layout mirrors this skill's pipeline boundary almost 1:1**: `RadioSonde.Sdr`
  (device abstraction) → `RadioSonde.Dsp` (channelizer/decimation/FM demod) →
  `RadioSonde.Decoders` (per-sonde subprocess adapters + JSON parsing) → `RadioSonde.Data`
  (SQLite sonde catalog + SondeHub import) → `RadioSonde.Mapping` (MapsUI track/prediction)
  → `RadioSonde.Export` (KML/GPX/CSV). A clean multi-project split like this is a reasonable
  template if a radiosonde feature in *this* app grows large enough to warrant its own
  sub-namespace under `Decoders/`.
- **`DecoderProcessHost`** (`src/RadioSonde.Decoders/DecoderProcessHost.cs`) is the concrete
  "external decoder process supervised + its output parsed" pattern also called out in
  `signal-decoding-analysis`'s hampi-dashboard reference: it launches a decoder, streams
  interleaved float32 IQ to stdin, parses JSON emitted on stdout, forwards stderr as
  diagnostics, and restarts on crash with a cap — the same shape this app should use for any
  wrapped external decoder, not just radiosondes.
- **`SondeJsonParser`** maps rs1729's stdout JSON fields to a `SondeTelemetry` model
  field-by-field against the decoder's own `fprintf` format string (`demod/mod/rs41mod.c`) —
  i.e. the parser was built by reading the upstream decoder's actual output format rather
  than guessing field order, which is the right way to consume any of these decoders.
- **`DftDetectAdapter`** wraps rs1729's `dft_detect` tool to score candidate audio/IQ against
  every known sonde type and report which one matched — a working example of the
  "auto-detect type via sync-word/type correlation" step in this skill's decoder workflow,
  implemented by delegating to the reference decoder rather than reimplementing correlation.
- **`BandScanner` + `MultiChannelDecodeManager`** (Phase 5) sweep 400-406 MHz with FFT peak
  detection/hysteresis to find candidate carriers, then allocate one channelizer slice +
  decoder process per active sonde under a CPU budget — directly reusable guidance for
  "scan the band, then multi-decode whatever is found" rather than requiring a manually
  tuned frequency per sonde.
- **A real, documented Windows interop pitfall worth avoiding if reusing rs1729 executables
  directly**: `rs41mod.c` only enables binary stdin mode under `#ifdef CYGWIN`, which MinGW
  builds don't define, so piped IQ was silently corrupted by CRLF translation and a stray
  `0x1A` byte being treated as EOF — 477 frames decoded from a file argument, 0 from the
  identical data piped via stdin. The fix required **both** compiling with `-DCYGWIN` *and*
  patching the decoder's streaming read to wait on a momentarily-empty pipe instead of
  treating it as end-of-stream (a plain file must still hit real EOF, so the patch only
  changes pipe behavior). A related follow-on: once the process stays alive long-term, its
  stderr becomes block-buffered by the C runtime and won't appear until exit — don't assert
  on decoder stderr in tests or rely on it for live signal-quality display.
- **Golden-sample validation example**: a real capture (31,250 S/s, 16-bit IQ, center
  405,800,240 Hz) decoded end-to-end through WAV → channelizer → decoder process →
  telemetry produced a Vaisala RS41-SG, serial `S1720982`, 43 frames in 45 s with 0 restarts,
  ascent 10,299→10,524 m, and matching PTU (−59.3 °C / 56.9% RH / 10 satellites) — the carrier
  was located by averaging spectra and taking the midpoint of the two FSK tones (4,799 Hz
  apart, the ~4.8 kHz shift that identifies an RS41). This is a good model for this skill's
  own "golden capture" test fixtures: store the expected serial, frame count, and first/last
  telemetry values alongside the IQ/WAV fixture, not just "it decoded something."

Treat `D:\source\repos\RadioSonde` as read-only inspiration; do not copy its code or the
vendored `rs1729` sources wholesale. If this app ever wraps the same decoders, get its own
license posture right first via `third-party-license-compliance` rather than assuming the
other project's "subprocess isolation" approach automatically clears every licensing concern.

## Radiosonde facts to model

Radiosondes are not one protocol — each manufacturer uses its own framing, sync word, and
field layout on a shared modulation style. A decoder needs per-type profiles, not a single
hard-coded format:

| Type | Band | Modulation | Baud | Frame notes |
|---|---|---|---|---|
| Vaisala RS41 | 400.15-406 MHz (some regions to 404-406) | GFSK | 4800 | Fixed-length frame with a distinct sync word; GPS position/velocity, PTU, and a rolling subframe carrying calibration constants and burst-kill status spread across many frames |
| Vaisala RS92 | 400-406 MHz | GFSK/PCM | 4800 | Older design; needs GPS almanac/ephemeris data from the frame stream (or an external source) to resolve position, unlike RS41's self-contained GPS solution |
| Meteomodem M10 / M20 | 400-406 MHz | 2FSK | ~9600 (M10), ~9600 (M20, differs in framing) | Distinct sync words per variant; M20 is a later revision with a changed frame layout |
| Graw DFM06 / DFM09 / DFM17 | 400-406 MHz | 2FSK/2GFSK | 2500-4800 (varies by revision) | Frame includes a repeated/duplicated structure for redundancy; revision-specific sync words |
| InterMet iMet-4 / iMet-1(-RS) | 400-406 MHz and 1680 MHz variants | AFSK/2FSK | varies | Simpler frame structure; less common in hobbyist decoders than RS41/M10/DFM |
| LMS6 / LMS6-403 | 400-406 MHz | GFSK | 4800 | Similar shape to RS41 framing conventions |

Other properties to carry per decoder instance regardless of type:

- selected/auto-detected sonde type and the confidence of that auto-detection
- serial number (the field that uniquely identifies a physical sonde/flight)
- frame sequence number, used to detect gaps/dropped frames
- GPS fix quality/number of satellites where the protocol exposes it
- burst-kill timer / termination status where present (RS41 in particular)
- battery voltage and other auxiliary telemetry fields when present
- per-frame CRC/checksum pass or fail
- signal metrics: RSSI/SNR and frequency offset from the nominal channel (sondes drift
  slightly off their nominal frequency; a decoder should tolerate and report this rather
  than requiring an exact center frequency)

Do not assume a single fixed frequency. Operational sondes are typically found scanning
400.0-406.0 MHz (regional band plans vary), and some networks additionally watch
1676-1680 MHz for specific InterMet variants. A practical receiver either scans this range
looking for FM-modulated bursts with a known baud rate, or is tuned manually from a known
local launch schedule.

## Normalized decoder output

Model a normalized sonde telemetry record the same way this codebase normalizes ADS-B
aircraft tracks and WEFAX lines — one shape regardless of which vendor's frame produced it:

- serial number (primary identifier, analogous to an aircraft's ICAO hex/registration)
- sonde type/subtype and auto-detection confidence
- receive timestamp, tuned frequency, and measured frequency offset
- latitude, longitude, altitude (GPS), ground speed, and vertical/ascent rate
- pressure, temperature, humidity (PTU) where the frame carries them
- frame sequence number and a running gap/dropped-frame count
- CRC/checksum pass state for the most recent frame
- burst-kill/termination state where the protocol exposes it
- battery voltage / auxiliary fields, when present, without inventing values the frame
  does not actually carry
- signal quality (RSSI/SNR) and raw frame reference for replay diagnostics
- decoder warnings for CRC failures, missing subframe data, ambiguous type detection, or
  implausible GPS jumps

This record should upsert into the same kind of track store the ADS-B pipeline uses (keyed
by serial number instead of ICAO hex), so the existing map, trail/position-history cache,
and "position only"/altitude-range filters apply to sondes with no separate UI stack.

## Decoder workflow

1. Tune/scan the 400-406 MHz (and optionally 1680 MHz) band for FM-modulated bursts.
2. Run a narrowband FM discriminator sized to the modulation (2FSK/GFSK), not a
   general-purpose wideband demodulator.
3. Recover the bit clock from the demodulated signal; radiosonde baud rates are fixed per
   type (commonly 4800, sometimes ~9600) and known in advance once a type is assumed.
4. Correlate incoming bits against each supported type's sync word to both detect presence
   of a sonde signal and auto-identify which vendor/type it is.
5. Deinterleave/decode the frame body (framing differs by vendor: some use Reed-Solomon or
   simple parity, others rely mainly on a checksum) and verify CRC/checksum before trusting
   the payload.
6. Parse GPS and PTU fields directly from the current frame; reassemble the
   slowly-rotating calibration/subframe data across multiple frames where the protocol
   splits it that way (notably RS41).
7. Emit a normalized telemetry record only after CRC/checksum validation; still surface a
   diagnostic for a failed-CRC frame rather than silently dropping it, mirroring how this
   app already quarantines invalid ADS-B/import records instead of discarding them silently.
8. Upsert the record into a serial-number-keyed track store; treat a new serial number as a
   new "flight" the same way a new ICAO hex starts a new aircraft track.
9. Optionally forward validated telemetry to SondeHub (`https://github.com/projecthorus/sondehub-infra`,
   the community aggregator most hobbyist trackers report to) or an APRS-IS gateway, the
   same way this app treats dump1090/ADS-B network feeds as one of several possible
   external ingestion sources rather than the only one.

## Testing strategy

Every radiosonde decoder implementation should have focused tests for:

- bit-clock recovery from a synthetic 2FSK/GFSK bitstream at each supported baud rate
- sync-word correlation and type auto-detection, including near-miss/no-match cases
- CRC/checksum validation for both valid and deliberately corrupted frames
- GPS field decoding against known-good reference frames per vendor
- subframe/calibration reassembly across a sequence of frames (RS41-style rolling data)
- frequency-offset tolerance (decode should not require an exact nominal center frequency)
- gap/dropped-frame counting when sequence numbers skip
- burst-kill/termination state transitions where the protocol models one
- malformed, truncated, or wrong-type frames producing a diagnostic instead of a crash or
  a silently wrong telemetry record

Use synthetic bitstream fixtures for deterministic per-field decode tests, and captured
IQ/WAV replay fixtures (one per supported sonde type) for end-to-end validation. Store the
sonde type, expected serial number, and expected first-N-frame field values alongside each
fixture so regressions are caught precisely.
