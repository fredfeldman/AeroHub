---
name: signal-decoding-analysis
description: "Use when designing or implementing signal decoders, demodulators, or digital-mode analysis features (RTTY, PSK31, FT8/FT4, Olivia, MFSK, WEFAX, digital voice, POCSAG/DTMF, DMR/trunking, ADS-B, APRS, SSTV, etc.) for an SDR app, or when deciding how a decoder module should plug into a SmartSDR-style slice/spectrum architecture. References fldigi's mode/architecture design, the isobar WEFAX decoder, a minimal PSK31 varicode decoder, and the hampi-dashboard multi-mode SDR dashboard, plus patterns from WSJT-X, multimon-ng, and DSD."
---

# Signal Decoding and Signal Analysis

## Purpose

This skill packages the architecture and mode knowledge needed to add signal decoders and
analysis tools to an SDR application. It uses **fldigi** as the primary architectural
reference (it is the most broadly studied open-source multi-mode digital decoder), grounds
several patterns in real local reference repositories (see below), and expands with
patterns from other well-known decoder projects: **WSJT-X** (weak-signal modes),
**multimon-ng** (FM digital sub-modes), and **DSD/DSD+** (digital voice).

This skill assumes the underlying DSP primitives (FFT/windowing, filters, resampling, IQ
correction) are already correct — see `dsp-fundamentals` for that prerequisite knowledge.
Because several of the repos referenced below are GPL-licensed, see
`third-party-license-compliance` before copying or closely porting any of their code rather
than just learning from their architecture.

## Local reference repositories

These repos live at `D:\source\repos\Decoders` and were inspected directly to ground the
patterns below in real code rather than description alone:

| Repo | What it is | Key takeaway |
|---|---|---|
| `dave-w1hkj/fldigi-fldigi` | Actual fldigi source | `src/` has one directory per mode family (see catalog below) — confirms the "one plugin per mode, shared host services" structure |
| `isobar` | From-scratch C++17 WEFAX (weather-fax) decoder, interoperable with KG-FAX | Clean dependency-free DSP core, fully separated from its FLTK GUI; a strong template for a decoder's internal pipeline |
| `psk31` | ~40-line C PSK31 varicode decoder (`decoder.c`) | The simplest possible correct decoder state machine — good reference/test-oracle for a PSK31 varicode implementation |
| `hampi-dashboard` | Raspberry Pi + RTL-SDR multi-mode web dashboard (DMR/trunking, ADS-B, scanner, APRS, SSTV, METEOR LRPT, Meshtastic) | Real example of a `DecoderManager`-equivalent arbitrating **one physical SDR dongle** across many mutually-exclusive decode modes, plus wrapping external decoder processes instead of decoding in-process |

For the RTL-SDR/rtl_tcp hardware layer itself (as opposed to the decoders hampi-dashboard
runs on top of it), see the `generic-sdr-hardware` skill.

### isobar: a concrete streaming-DSP-core pattern

`isobar/core/` is a dependency-free C++17 DSP core (`decoder.h`/`.cpp`, `filters`, `fft`,
`resample`, `syncscan`, `tonedetect`, `palette`, `bmpfile`) with **zero** GUI dependency;
`isobar/gui/` (FLTK) and `isobar/cli/` are thin consumers of it. This is the cleanest local
example of the "DSP core has no UI framework reference" rule from `smart-sdr-architecture`
and this skill's design guidance below — worth pointing a new decoder module at directly:

- `core/decoder.h` (`FmDecoder`): a **streaming** `feed(sample, out)` demodulator — FM
  discriminator via bandpass + Hilbert transform + phase-difference, then a rational
  resampler, matching the `ProcessSamples`-style streaming shape described below.
- `core/tonedetect.h` (`ToneDetect`): a small two-resonator tone detector (300/450 Hz start/
  stop tones) that reports a level every fixed block (100 ms) — a minimal, reusable pattern
  for "squelch-style" tone-present detection independent of the main demod path.
- `core/syncscan.h` (`FaxImage`/`SyncParams`): tracks line synchronization with an explicit
  **4-state confidence model** — `locked` (matched a real sync edge), `corrected` (fallback
  min-brightness search found a plausible edge), `coasting` (no edge, phase held from
  prediction), `not locked` (no sync yet). This is a strong general pattern for reporting
  decoder confidence to the UI (compare to squelch/AFC state in fldigi) and is worth reusing
  for any decoder that needs to show "how sure are we this is really sync'd" rather than a
  binary locked/unlocked flag.

### psk31: minimal correct varicode decoder

`psk31/decoder.c` is a ~40-line state machine: accumulate shifted bits into a varicode
word, and on a "00" gap (two consecutive zero bits) look up the accumulated word in
`varicode_table` and emit the matching ASCII character, then reset. It has no dependencies
and no framing beyond varicode itself. Useful as a from-scratch reference implementation or
as a correctness oracle when unit-testing a PSK31 decoder in this codebase.

### hampi-dashboard: single-hardware-resource arbiter

hampi-dashboard runs many decode modes (`backend/dmr.py`, `sdrtrunk.py`, `adsb.py`,
`scanner.py`, `aprs.py`/`ax25.py`, `sstv.py`, `meteor.py`, `meshtastic_handler.py`,
`satellite.py`) but has only **one** RTL-SDR dongle to feed them from. Its documented
solution (`SDRTRUNK.md`): the dashboard backend is the single arbiter of the SDR hardware —
selecting a mode stops whatever previously owned the dongle before starting the next, and
nothing is allowed to claim the hardware behind the arbiter's back (it disables
auto-start on the SDRTrunk service specifically so the two can't race for the tuner).

This matters for a `DecoderManager`-style component whenever more than one decoder wants
**exclusive** access to a physical device or a scarce, non-shareable resource (as opposed to
tapping a shared audio/IQ stream, which multiple decoders can safely subscribe to at once —
see the multimon-ng pattern below). Two more points worth carrying over:

- Some modes are decoded **in-process** (its own DSP), others by **wrapping an external
  decoder tool/process** and parsing its output — DMR trunking wraps SDRTrunk + `dsd-fme`
  (AMBE vocoder via the JMBE codec jar), METEOR LRPT wraps SatDump, APRS/AX.25 wraps
  `direwolf`. A `DecoderManager` should treat "in-process DSP modem" and "external
  decoder process supervised + its output parsed" as two equally legitimate
  `IDecoderModem`/`IBatchDecoderModem`-style implementations, not assume everything is
  reimplemented from scratch.
- Its mode catalog (DMR digital voice, trunked DMR, ADS-B, AM/FM scanner, APRS, AX.25, SSTV,
  METEOR LRPT, Meshtastic LoRa, satellite telemetry) is a good checklist of "what a
  full-featured SDR decode app eventually grows to support" beyond amateur digital-text modes.

Use this skill when you need to:

- add a new digital-mode decoder (RTTY, PSK, MFSK, Olivia, FT8-style, etc.)
- design the pipeline from audio/IQ samples to decoded text or bits
- decide where AFC, squelch, and waterfall-tuning belong in a decoder module
- support multiple simultaneous decoders per channel/slice
- classify or auto-detect a signal's mode from its spectral characteristics
- decide how much of a decoder is DSP vs. view-model vs. UI

This complements the `smart-sdr-architecture` skill: that skill defines *where* code lives
(managers, view models, views); this skill defines *what goes inside a decoder module*.

## Reference architecture: fldigi

fldigi decodes many digital modes through a common internal pipeline. Modeling a decoder
module after this pipeline keeps DSP concerns cleanly separated from UI concerns:

1. **Audio/IQ source** — a fixed-rate sample stream (fldigi uses the sound card at a
   fixed sample rate, typically 8/11.025/48 kHz; an SDR app instead taps a per-slice
   audio or baseband IQ stream). See `audio-streaming-pipeline` for the concrete WASAPI
   capture/render layer this stream typically comes from or feeds into.
2. **Passband filter** — a narrow bandpass/lowpass around the signal of interest, tuned by
   the waterfall cursor. Keeps downstream DSP from processing the whole receiver bandwidth.
3. **Modem (demodulator)** — one class per mode family, converting the filtered signal into
   a symbol/bit stream. fldigi calls these "modems" and loads them as a set of mode plugins
   sharing a common base interface (`init`, `rx_process`, `tx_process`, `searchDown`/`searchUp`
   for AFC).
4. **Automatic Frequency Control (AFC)** — a feedback loop that nudges the demodulator's
   center frequency toward the strongest correlation peak so small drift doesn't lose sync.
   This is a per-modem concern, not a UI concern, but it needs read/write access to the
   "current tuned frequency" that the UI also displays — so it's usually implemented as a
   small interface the modem holds a reference to, not a UI callback.
5. **Bit/frame sync + FEC/interleave decode** — recovers byte or character boundaries and
   corrects errors (varicode for PSK31, convolutional/Viterbi for Olivia/MFSK, Reed-Solomon
   or LDPC for newer weak-signal modes).
6. **Character/text decode** — maps recovered bits to characters (Baudot/ITA2 for RTTY,
   varicode for PSK31, a custom alphabet for Olivia/Contestia/MFSK).
7. **Squelch** — a threshold (often signal-to-noise ratio or correlation-based, not just raw
   signal level) that gates whether decoded output is treated as "believed valid" or
   suppressed. Distinct from radio squelch — this squelch usually lives at the decoder,
   not the audio-chain, level.
8. **Sink** — decoded text/data delivered to a results view, plus optional macros/logging.

### Mode plugin pattern

fldigi treats each mode as a self-contained plugin with the same lifecycle shape
(construct → configure sample rate/bandwidth → `rx_process(samples)` per audio block →
periodically emit decoded characters/bits → `close`). Following this shape for a new
decoder module makes it trivial to add more modes later without touching the host app.

Confirmed from the actual source (`dave-w1hkj/fldigi-fldigi/src/`): each mode family is
its own top-level directory alongside shared host services — `rtty/`, `psk/`, `mfsk/`,
`olivia/`, `contestia/`, `dominoex/`, `thor/`, `throb/`, `mt63/`, `feld/` (Feldhell),
`navtex/`, `ifkp/`, `fsq/`, `wefax/`, `rsid/` (the reed-solomon **mode-identification**
sub-carrier, not a mode itself), `dtmf/`, `cw/`, `packet/` (AX.25) — plus non-modem
service directories that map directly onto this skill's pipeline stages: `waterfall/`,
`trx/` (the modem host/scheduler), `soundcard/` (audio source), `rigcontrol/` (radio
frequency read/write, the AFC counterpart), `logbook/` and `spot/` (sinks). This confirms
the "one directory/plugin per mode family, shared host services around them" shape is not
just a description — it's the literal directory layout, and is a reasonable folder layout
to mirror in a `Decoders/` namespace in this codebase.

```
IDecoderModem
  ModeName, RequiredBandwidthHz, RequiredSampleRateHz
  void Reset()
  void ProcessSamples(ReadOnlySpan<float> samples)       // audio or baseband IQ block
  event DecodedTextReceived                              // characters/frames as recovered
  event SignalQualityChanged                             // for squelch/confidence display
  double CurrentFrequencyOffsetHz { get; set; }           // AFC read/write point
```

A `DecoderManager` (see `smart-sdr-architecture`) is responsible for instantiating the
right `IDecoderModem` for the selected mode, feeding it samples from the slice's audio/IQ
pipeline, and exposing its events to a `SignalDetailsViewModel`/`SignalListViewModel`.

The batch/windowed counterpart (see the WSJT-X reference below) follows the same shape but
submits whole windows instead of per-block streaming:

```
IBatchDecoderModem
  ModeName, RequiredSampleRateHz, WindowDurationSeconds
  void Reset()
  void SubmitWindow(ReadOnlySpan<float> samples, DateTime windowStartUtc)
  event DecodedMessagesReceived                          // zero or more full messages per window
  event SignalQualityChanged
```

Both interfaces should declare `RequiredSampleRateHz` rather than assume the slice's native
rate. `isobar/core/resample.h` is a concrete, reusable example of the resampler a
`DecoderManager` needs between "whatever rate the slice/audio tap runs at" and "whatever
rate a given modem was written for" — it does rational resampling (e.g. 22050 → 8000, a
160/441 ratio) with linear interpolation, cheap enough to run per-decoder-instance rather
than forcing every modem to accept an arbitrary input rate.

## Mode catalog (fldigi-derived)

Useful baseline characteristics when implementing or auto-classifying these modes.
Numbers are nominal/common configurations, not exhaustive:

| Mode family | Approx. bandwidth | Symbol/baud rate | FEC/structure | Notes |
|---|---|---|---|---|
| RTTY (Baudot) | ~250–450 Hz shift, ~300 Hz BW | 45–100 baud | none (raw Baudot/ITA2) | Mark/space FSK; shift + baud rate identify sub-variant |
| PSK31/PSK63/PSK125 | ~31–125 Hz | 31.25/62.5/125 baud | none, uses varicode (variable-length, self-synchronizing) | BPSK or QPSK; QPSK variants add convolutional FEC |
| Olivia | 125/250/500/1000 Hz (configurable) | tone-count × bandwidth config (e.g. "8/250", "16/500") | MFSK + Walsh-Hadamard + convolutional, strong FEC | Very robust to noise, slow throughput |
| Contestia | similar to Olivia, narrower | configurable tone/bandwidth pairs | lighter FEC than Olivia | Faster than Olivia, less robust |
| DominoEX / THOR | ~100–1500 Hz depending on variant | incremental-frequency MFSK | THOR adds strong interleaved FEC | Both are incremental-keying MFSK families |
| MT63 | 500/1000/2000 Hz | fixed per-bandwidth baud | heavy time+frequency interleave, orthogonal tones | Very robust, higher latency due to interleave depth |
| Throb | narrow, few hundred Hz | very low baud | tone-position encoding | Slow, robust, rarely used now |
| Feldhell (Hellschreiber) | ~300 Hz | image-based, ~2–8 px/character scan rate | none (visual/human error correction) | Decodes to a scrolling pixel image, not text |
| NAVTEX | ~170 Hz shift, ~300 Hz BW | 100 baud FSK | 7-bit SITOR-B (CCIR 476) forward error correction | Fixed-format maritime safety broadcasts on 518 kHz etc. |
| IFKP | narrow, incremental FSK | low baud | similar family to DominoEX | Keyboard-to-keyboard chat mode |
| FSQ (FSQ Call) | ~300–450 Hz | very low baud, long tone duration | strong FEC, designed for very weak signals | Store-and-forward keyboard chat mode |
| DTMF | audio tone pairs (697–1633 Hz) | ~50–100 ms per digit | none | Two simultaneous tones per digit; decode via dual tone-pair correlation, not FSK |
| CW (Morse) | tens of Hz | variable (WPM-based) | none | Decoded via envelope/tone detection + timing classification, not a bit/symbol demod |
| Packet (AX.25) | ~1000–2200 Hz (Bell 202 AFSK typical) | 1200/9600 baud | HDLC framing + CRC | Same AFSK1200 family multimon-ng also decodes (see below) |
| WEFAX (weather fax) | ~1500–2300 Hz FM sub-carrier | 60/90/100/120/240 rpm scan rate, IOC 576 | none (image), start/stop tone framing (300/450 Hz) | Image-based, not text; see the isobar reference below for a full worked implementation |

### RSID: automatic mode identification and switching

fldigi's `rsid/` is not a text mode — it's a short (~1.4 s) Reed-Solomon-coded MFSK burst
that other modems optionally transmit just before or after their own signal, encoding
"which mode/bandwidth is this." fldigi runs an always-on RSID detector in parallel with
whatever modem is currently active; when it decodes a burst it can auto-switch the active
modem/bandwidth to match. For a new SDR app this is a good pattern for **unattended
multi-mode monitoring**: run a lightweight always-on classifier (RSID detector, or the
heuristics in the next section) alongside the active decoder, and let it drive
`DecoderManager` mode switches instead of requiring the operator to pre-select a mode.

## Reference: WSJT-X-style weak-signal modes

WSJT-X (FT8, FT4, JT65, JT9, MSK144, WSPR) uses a fundamentally different pipeline shape
worth knowing when adding "weak signal" modes:

- **Fixed transmission windows**, not continuous streaming: FT8 uses 15-second slots
  aligned to UTC; the decoder processes one full window of samples at a time (an entire
  ~12.6 kHz-wide or narrower spectrum snippet), not a continuous per-symbol stream.
- **FFT-based synchronization**: the decoder searches a 2D time/frequency grid for a known
  sync tone pattern (Costas arrays for FT8/FT4) rather than a PLL-style AFC loop.
- **Strong FEC over the whole message**: LDPC (FT8) or Reed-Solomon (JT65) decodes the
  entire fixed-length message at once — there is no streaming "character at a time" output;
  a message either fully decodes or doesn't.
- **Design implication**: a decoder module for this style needs a *buffering* sink (collect
  N seconds of samples, then run one batch decode) rather than the `ProcessSamples` streaming
  model above. Model it as a second interface, e.g. `IBatchDecoderModem` with
  `SubmitWindow(samples, windowStartUtc)` and a `DecodedMessagesReceived` event, sharing the
  same `DecoderManager` registration point but a different processing shape.

## Reference: multimon-ng-style FM digital sub-modes

multimon-ng demodulates several digital modes carried over analog FM (POCSAG/FLEX paging,
DTMF, AFSK1200/2400, EAS/SAME, ZVEI). Relevant pattern:

- These modes assume the audio has already been FM-demodulated (they operate on discriminator
  audio, not IQ), so in an SDR app they attach downstream of the existing FM demod path, not
  in parallel with it — reuse the slice's demodulated audio output rather than re-deriving it.
- Each mode is a simple, independent bit-slicer (Correlator/PLL-based clock recovery +
  threshold), making them cheap to run several at once on the same audio stream for
  multi-protocol scanning. This favors a design where a `DecoderManager` can run *many*
  lightweight decoders concurrently against one audio tap, distinct from the heavier
  single-active-mode digital-text decoders above.

## Reference: DSD/DSD+-style digital voice decoding

DSD (Digital Speech Decoder) decodes P25, DMR, NXDN, and similar digital voice protocols.
Pattern worth carrying over:

- Digital voice decoding is a two-stage problem: **frame sync + FEC decode** (recovering a
  vocoder bitstream) followed by **vocoder synthesis** (AMBE/IMBE → PCM audio). These are
  naturally two separate components even though they're often bundled: a
  `IDigitalVoiceFrameDecoder` (bits out) and a separate audio-synthesis stage, so a UI can
  show "sync acquired / talkgroup ID / slot" state even before or without audio playback.
- Frame sync uses known bit patterns per protocol (like fldigi's AFC uses tone correlation);
  the same "correlate against a known pattern, track confidence, expose it for squelch/UI"
  idea applies.

## Reference: this repo's own NAVTEX — a fourth decoder pattern (radio-side decode)

The in-process DSP modem (`IDecoderModem`), the batch modem (`IBatchDecoderModem`), and
wrapping an external decoder process (hampi-dashboard, above) aren't the only legitimate
shapes. **This codebase already ships a real decoder that uses none of them**:
[NAVTEX.cs](../../../FlexLib/Flex.Smoothlake.FlexLib/NAVTEX.cs) and
[NAVTEXViewModel.cs](../../../SmartSDR/SmartSDR.ViewModels/NAVTEXViewModel.cs).

- The actual NAVTEX demodulation happens **in the radio's own firmware**, not on the PC.
  `NAVTEX.Toggle`/setup sends ordinary slice commands over the existing command channel
  (`radio-protocol-conventions`) — `"slice create"`, `"slice s 0 mode=NT"`, `"slice tune 0
  <freq>"`, `"sub slice 0"` — to put one of the radio's own slices into NAVTEX decode mode.
  No audio/IQ samples are ever pulled to the PC for this mode.
- The PC side is a **thin state mirror**: `NAVTEX` (an `ObservableObject`, not a modem) has a
  `ParseStatus(string s)` method fed decoded text and status fields (`status=...`) parsed
  directly out of the radio's status messages, and raises `PropertyChanged` for the UI to
  pick up — the identical shape every other radio-mirrored object in this codebase already
  uses (`Slice`, `Panadapter`, etc. per `smart-sdr-architecture`), just applied to a decoded
  message feed instead of tuning/meter state.
- **When this pattern applies**: if the hardware itself already has the DSP/decode
  capability built in (firmware-based modes on real radio hardware, or a vendor SDR with
  on-device decode), don't reimplement that decode on the PC at all — send the mode-select
  command through the existing protocol layer and treat the decoded output as just another
  piece of radio-reported state, parsed the same way meter/status text already is.
- **When it doesn't apply**: any mode the hardware itself can't decode (most digital modes on
  most non-Flex hardware, and even on Flex hardware for modes without firmware support) still
  needs one of the other three patterns — in-process DSP, batch DSP, or external process.

A `DecoderManager` should treat "hardware-side decode, PC mirrors status via the existing
command/reply channel" as a fourth legitimate implementation strategy to check for before
assuming a new mode needs any client-side sample processing at all.

## Design guidance for this codebase

Following `smart-sdr-architecture` layering:

- `IDecoderModem` / `IBatchDecoderModem` implementations and any FFT/FEC/vocoder DSP code
  belong in the non-WPF core/DSP library (mirrors `FlexLib`), never in a view model or view.
  Follow `isobar/core`'s example literally: its DSP core has no reference to its FLTK GUI at
  all, only plain data in/out — that's the bar to hold a new decoder's core to.
- `DecoderManager` owns the set of active decoder instances, keyed by slice/channel, and is
  responsible for feeding them samples and for choosing streaming vs. batch processing shape.
- If a decoder needs **exclusive** access to a physical resource (a second SDR device, an
  external hardware decoder, etc.) rather than just tapping a shared audio/IQ stream, give
  `DecoderManager` (or a resource arbiter it owns) explicit start/stop ownership of that
  resource, mirroring hampi-dashboard's single-arbiter pattern — never let two decoder
  instances assume they can both claim the same exclusive resource.
- Not every decoder has to be reimplemented in-process. hampi-dashboard's pattern of
  supervising an external decoder process (SDRTrunk + dsd-fme for DMR, SatDump for METEOR
  LRPT, direwolf for APRS/AX.25) and parsing its output is a legitimate `IDecoderModem`/
  `IBatchDecoderModem` implementation strategy, especially for protocols with mature,
  complex existing decoders — don't default to a full rewrite when wrapping is viable.
- Check whether the hardware itself can already decode the mode before assuming any
  client-side DSP is needed at all — this repo's own NAVTEX (above) sends a mode-select
  command and mirrors decoded status text, with zero PC-side sample processing.
- A `SignalDetailsViewModel` (or per-mode variant) adapts a modem's events into bound
  properties/collections; a `SignalListViewModel` shows multiple concurrent decodes for
  scanning-style modes (multimon-ng pattern).
- AFC/squelch confidence values are exposed as decoder events/properties, not computed in
  the UI layer — the UI only renders what the modem/manager reports. Prefer a multi-state
  confidence model over a boolean where it fits (isobar's `locked`/`corrected`/`coasting`/
  `not locked` states for WEFAX line sync are a good template for any decoder that can
  degrade gracefully instead of just losing lock outright).
- Remember the threading model from `smart-sdr-architecture`: samples typically arrive off
  the UI thread; only marshal to the Dispatcher at the point decoded text/state updates a
  bound property, not for every sample block.

## Mode auto-classification heuristics

When trying to identify an unknown signal from spectral/waterfall features alone:

1. Measure occupied bandwidth and tone/shift spacing from the FFT — narrow (<150 Hz) with
   two discrete tones suggests RTTY; single narrow tone with phase transitions suggests
   PSK31; several evenly spaced narrow tones suggests Olivia/Contestia/MFSK family.
2. Measure symbol/keying rate via autocorrelation of the amplitude/phase envelope — matches
   against the mode catalog above narrows candidates quickly.
3. For FM sub-audio protocols (POCSAG/DTMF/AFSK), classify at the discriminator-audio stage,
   not the RF stage — bandwidth and tone patterns of digital voice/paging signals are
   determined after FM demod, not before.
4. Fixed-window modes (FT8/FT4/WSPR) are usually best identified by their well-known
   transmission cadence and audio passband (e.g. FT8 default receive passband ~200–2900 Hz
   window) rather than by tone analysis alone.

## Testing strategy for a decoder module

Grounded in the actual test suites of the local reference repos — both go well beyond
"assert the output string matches":

- **Golden-recording tests.** isobar's `cli/offair-test.cpp` decodes a real captured
  off-air recording (`jmh-offair-12k.wav`, checked into the repo) and checks the result
  against known-good output, plus `rpm60-test.cpp` and `wavrate-test.cpp` repeat this at
  a different scan rate / sample rate. For any new decoder module: capture at least one
  real (or faithfully synthesized) recording of the target mode, check it into a test-fixtures
  folder, and assert against a known-correct decode — this catches regressions that a purely
  synthetic unit test misses (real signals have noise, drift, and timing jitter synthetic
  test tones don't).
- **Per-primitive unit tests, not just end-to-end.** isobar also unit-tests individual DSP
  stages in isolation: `fft-test.cpp`, `resample-test.cpp`, `tone-test.cpp`,
  `settings-test.cpp`. Test the filter/resampler/tone-detector/sync-tracker pieces of a new
  modem independently before trusting an end-to-end golden-recording test to localize a bug.
- **Fallback/degraded-path tests.** isobar's `fallback-test.cpp` and `manual-sync-test.cpp`
  specifically exercise the "lost lock, using the fallback search" and "operator manually
  re-syncs" paths — i.e. the non-happy-path states of the confidence model described above.
  Don't only test the `locked` path; test `corrected`/`coasting`/manual-recovery too.
- **Fuzz/robustness tests for bitstream parsers.** psk31's `test/fuzz_codec.c` fuzzes the
  varicode encoder/decoder with malformed or random bit sequences. Real RF input is
  effectively adversarial (noise, dropped bits, mis-sync) even without an attacker, so any
  decoder that parses a recovered bitstream into frames/characters should be fuzzed or at
  least stress-tested with random bit garbage to confirm it degrades (drops/resyncs) rather
  than throwing, hanging, or overrunning a buffer.
- **Interop round-trip tests where a format is shared with another tool.** isobar's
  `kgfax-decode.cpp` round-trips its `.syn` file format against the original KG-FAX tool's
  files. If a new decoder's output format needs to interoperate with an existing ecosystem
  tool (log file format, macro format, `.syn`-equivalent), add an explicit round-trip test
  against a real file produced by that other tool, not just self-consistency.

## Quick implementation checklist

- pick streaming (`IDecoderModem`) vs. batch (`IBatchDecoderModem`) shape based on the mode
- keep DSP/FEC/vocoder code out of the WPF/view-model layers (verify: zero references to
  WPF/view-model types from the DSP core, same bar as `isobar/core`)
- expose AFC frequency and squelch/confidence as modem state, not UI-computed values;
  prefer a multi-state confidence enum over a boolean lock flag where it fits
- let `DecoderManager` own instantiation/lifetime, keyed by slice or by scan target, and own
  arbitration of any exclusive hardware resource a decoder needs
- decide in-process DSP vs. supervising an external decoder tool vs. hardware/firmware-side
  decode (this repo's NAVTEX) and parsing status — pick based on what the hardware itself
  already supports and protocol complexity, not by default assumption
- support running multiple lightweight decoders concurrently for scanning-style modes
- add a mode entry to the catalog above (bandwidth, baud rate, FEC) so classification and
  UI defaults (like passband width) can reference it
- add a golden-recording test fixture plus per-primitive DSP unit tests before considering
  a new decoder module done (see Testing strategy above)
