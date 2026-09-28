---
name: dsp-fundamentals
description: "Use when implementing or reviewing the core DSP primitives an SDR app depends on — FFT/windowing, FIR/IIR filter design, decimation/resampling, IQ imbalance/DC-offset correction, envelope/phase detection. Prerequisite knowledge for signal-decoding-analysis; grounded in the isobar reference decoder's DSP core."
---

# DSP Fundamentals for an SDR App

## Purpose

`signal-decoding-analysis` describes *what a decoder module looks like* (modem interfaces,
mode catalog, AFC/squelch, testing) but assumes the underlying DSP primitives already exist
and are correct. This skill covers those primitives themselves — the pieces every decoder,
the spectrum display, and the waterfall all build on. Get these wrong and every downstream
mode (RTTY, PSK31, WEFAX, anything) inherits the bug.

Use this skill when you need to:

- implement or review an FFT/window function, a FIR/IIR filter, a resampler, or an envelope
  or phase detector
- diagnose a decoder or spectrum display that's subtly wrong (frequency offset, image
  artifacts, aliasing, drift) rather than obviously broken
- decide what sample rate/bandwidth a new DSP stage should run at

## Reference: `isobar/core`

`isobar/core/` (see `signal-decoding-analysis`'s local reference repos) is a small,
dependency-free, well-documented DSP core worth reading directly for concrete
implementations of several of the primitives below: `fft.h`/`.cpp`, `filters.h`/`.cpp`
(FIR bandpass/lowpass/Hilbert), `resample.h`/`.cpp` (rational resampler), `tonedetect.h`
(2-pole resonator tone detection), `decoder.h` (FM discriminator via Hilbert transform +
phase difference). Each file's header comment documents the exact math and tap counts used,
which is more useful than re-deriving the formulas from scratch.

## Core primitives

### 1. FFT and windowing

- An FFT of a rectangular (unwindowed) block of samples has severe spectral leakage — a
  strong signal smears energy across many bins, hiding weaker nearby signals. Always apply
  a window (Hann, Hamming, Blackman-Harris) to the block before transforming, unless the
  block itself is already naturally periodic in the transform length.
- Window choice is a tradeoff between main-lobe width (frequency resolution) and side-lobe
  level (leakage suppression) — Hann is a reasonable default for a general-purpose spectrum
  display; Blackman-Harris trades resolution for much lower leakage, useful when a decoder
  needs to distinguish a weak tone near a strong one (e.g. Olivia/MFSK tone detection).
- FFT bin spacing is `sample_rate / fft_size`. A decoder that needs to resolve tones spaced
  `df` Hz apart needs `fft_size >= sample_rate / df`, which directly trades off against
  update rate (`sample_rate / fft_size` samples of latency per transform) — this tension is
  exactly why weak-signal modes (see WSJT-X in `signal-decoding-analysis`) use long,
  infrequent transforms while a live spectrum display uses short, frequent ones.
- Overlapping successive FFT windows (50–75% overlap) smooths a waterfall/spectrum display
  and improves detection of short bursts that might otherwise straddle two non-overlapping
  blocks, at the cost of more compute per second of input.

### 2. FIR/IIR filters

- A FIR filter (isobar's `Fir` bandpass/lowpass/Hilbert types) has linear phase and is
  unconditionally stable, at the cost of needing more taps (more compute, more latency) than
  an equivalent IIR filter for the same rolloff steepness. Prefer FIR for anything where
  phase distortion would break a decoder (varicode/FEC decoders are sensitive to phase
  jitter); IIR is fine for cheap coarse filtering (e.g. an audio-only lowpass before a
  non-phase-sensitive detector).
- A Hilbert transformer (used for FM discrimination and SSB/IQ generation — see isobar's
  `decoder.h`) has an inherent group delay of `(taps-1)/2` samples; any parallel path (e.g.
  an in-phase delay line) must be delayed by the same amount or the two paths desync. This
  is a common source of subtle demodulation artifacts that only show up as slightly wrong
  output rather than an obvious crash.
- Passband/stopband edges should be set from the mode's actual bandwidth requirement (see
  `signal-decoding-analysis`'s mode catalog), not guessed — an over-wide passband lets noise
  and adjacent signals into the demodulator; an over-narrow one clips the signal's own
  sidebands and distorts it.

### 3. Decimation and resampling

- Decimating (downsampling) without first lowpass-filtering below the new Nyquist rate
  causes aliasing — always filter-then-decimate, never decimate raw samples.
- Rational resampling (e.g. isobar's 22050→8000 Hz, a 160/441 ratio) tracks a fractional
  input-sample position and interpolates between the two neighboring samples; simple linear
  interpolation is adequate for audio-rate DSP but introduces its own small lowpass effect —
  acceptable for most digital-mode decoding, not necessarily for high-precision measurement.
- `signal-decoding-analysis`'s `IDecoderModem.RequiredSampleRateHz` contract exists exactly
  so a `DecoderManager` can insert exactly this filter+resample step once, rather than every
  modem re-implementing its own ad hoc rate conversion.

### 4. IQ imbalance and DC offset

- Real SDR front ends have imperfect I/Q balance (gain/phase mismatch between the two ADC
  paths) and DC offset (a spurious spike at 0 Hz baseband, i.e. at the tuned center
  frequency). Both show up as artifacts in the spectrum display and can corrupt a
  low-baud-rate/narrow-bandwidth decoder tuned near center.
- DC offset correction is typically a simple running-average subtraction (a one-pole highpass
  at a very low cutoff) applied to the raw I/Q stream before any further processing.
- IQ imbalance correction (gain/phase compensation) is usually only needed if the front end
  itself doesn't already correct it (many SDR hardware/driver stacks do); check whether the
  IQ stream reaching the app is already balanced before adding a redundant correction stage.

### 5. Envelope and phase detection

- Envelope detection (magnitude of an analytic/complex signal, or a rectify+lowpass on a
  real signal) is the basis for CW/Morse timing detection, tone-presence detection (isobar's
  `ToneDetect`), and squelch. It answers "is there energy here," not "what's the exact
  frequency/phase."
- Phase detection (via `atan2` on successive complex samples, or a PLL) is needed wherever a
  decoder must track frequency drift or recover exact symbol timing (AFC, bit-sync). Phase
  unwrapping (tracking cumulative phase rather than the wrapped -π..π value) is required
  whenever the underlying quantity — like isobar's FM discriminator phase difference — can
  exceed one wrap per sample.

## Sample-rate/bandwidth decision guide

When adding a new DSP stage, decide its sample rate top-down, not bottom-up:

1. What bandwidth does the mode actually need (see `signal-decoding-analysis`'s mode
   catalog)? That sets the *minimum* useful sample rate (Nyquist ≥ 2× the occupied
   bandwidth, with margin for filter rolloff).
2. What rate does the slice/audio tap actually deliver? That's almost always higher than
   (1) — insert a filter+decimate/resample stage rather than running the whole decoder at
   the tap's native rate.
3. Keep every internal stage at the lowest rate that satisfies (1) — running FFTs, filters,
   or symbol-sync loops at a needlessly high rate wastes CPU for zero benefit and, per the
   `wpf-realtime-rendering` skill, DSP cost directly competes with UI thread and render
   budget in a real-time app.

## Quick implementation checklist

- window applied before every FFT unless the block is provably periodic in-window already
- filter type (FIR vs IIR) chosen based on whether phase linearity matters for the consumer
- any parallel signal paths (I vs Q, Hilbert vs delay-line) matched in group delay
- decimation always preceded by an anti-alias lowpass at the new Nyquist rate
- DC offset / IQ imbalance correction verified necessary (not already handled upstream)
  before adding a redundant stage
- each DSP stage runs at the lowest sample rate that still satisfies its mode's bandwidth
  requirement (see `signal-decoding-analysis`'s mode catalog)
