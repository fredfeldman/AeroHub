---
name: audio-streaming-pipeline
description: "Use when wiring up microphone/speaker or virtual-audio-device I/O in a WPF SDR app — WASAPI capture/render, exclusive-vs-shared mode fallback, buffered/resampled streaming, and virtual audio device enumeration. Grounded in this repo's actual DAXWPF/DAXWPF.Audio implementation."
---

# Audio Streaming Pipeline (WASAPI Capture/Render)

## Purpose

Decoders and analysis features (`signal-decoding-analysis`) ultimately need real audio
samples from somewhere — a microphone, a speaker, or a virtual audio cable exposing a
per-slice/per-channel stream (this repo's DAX virtual audio devices). This skill covers the
concrete Windows audio I/O layer underneath that, grounded in the actual implementation in
`SmartSDR/DAXWPF/` and `SmartSDR/DAXWPF.Audio/`.

Use this skill when you need to:

- capture from or render to a Windows audio device (microphone, speaker, virtual cable)
- decide between WASAPI exclusive mode and shared mode, and handle the fallback between them
- resample a device's native format to whatever rate a decoder/DSP stage needs
- enumerate and select a specific virtual audio device (e.g. a DAX-style per-channel device)
  by name rather than by relying on the OS default device

## Reference implementation

- `DAXWPF.Audio/WasapiCaptureWaveProvider.cs` wraps NAudio's `WasapiCapture`:
  - Requests a specific `WaveFormat` (default: 24 kHz IEEE float, 2 channels) but **checks
    `IsFormatSupported` first** and falls back to the device's closest match / mix format
    rather than assuming the requested format is always available — catches both
    `NotSupportedException` and a specific `COMException` error code as the two ways WASAPI
    signals "can't do that format."
  - Feeds captured audio through a `DmoResampler` (Windows' built-in DSP/Media-Object
    resampler) into a `BufferedWaveProvider` with `DiscardOnBufferOverflow = false` and
    `ReadFully = false` — i.e. it back-pressures rather than silently dropping audio if a
    downstream consumer falls behind, and a `BufferDuration` sized off the requested latency
    (`_latency * 10` ms).
- `DAXWPF.Audio/WasapiRenderWaveProvider.cs` wraps NAudio's `WasapiOut`:
  - **Tries exclusive mode first, falls back to shared mode on any exception** (a `fallback`
    flag controls whether the fallback happens or the exception propagates) — exclusive mode
    gives lower latency but can fail to open if another app already holds the device.
  - The shared-mode fallback path also drops `useEventSync` to `false`, since event-driven
    sync is a WASAPI exclusive-mode assumption which doesn't reliably apply in shared mode.
  - Its output `BufferedWaveProvider` uses `DiscardOnBufferOverflow = true` — the opposite
    choice from capture — because render is a "best effort to keep playing" path where
    dropping stale audio is preferable to blocking or growing an unbounded buffer.
- `DAXWPF/MMDeviceHelper.cs`:
  - `FindExclusiveWaveFormat` tries a short prioritized list of common formats
    (44.1/48 kHz × float/16/32-bit) against `IsFormatSupported(Exclusive, ...)` and picks the
    first one the device actually accepts, rather than assuming a single hardcoded format
    works across all hardware.
  - `GetAudioDeviceWithNameMatching` enumerates active endpoints and matches by
    `FriendlyName` exactly — this is how a specific virtual audio device (e.g.
    `"DAX RESERVED AUDIO RX {channel} (FlexRadio Systems DAX Audio)"`) is found instead of
    relying on whatever the OS considers the "default" device. Throws if not found rather
    than silently falling back to a wrong device.

## Design principles to carry into a new app

1. **Never assume a requested format is supported — probe first, fall back deliberately.**
   Every device (physical or virtual) may reject a specific sample rate/bit depth/channel
   count combination; check `IsFormatSupported` and have an explicit fallback chain (closest
   match → device mix format → a short list of known-common formats) rather than letting the
   exception surface to the user.
2. **Exclusive vs. shared mode is a tradeoff, not a fixed choice.** Exclusive gives lower,
   more deterministic latency (valuable for a decoder needing tight timing) but can fail to
   acquire the device or conflict with other apps; always have a shared-mode fallback path,
   and make the fallback behavior (throw vs. silently degrade) an explicit parameter, not an
   implicit assumption.
3. **Choose overflow behavior per direction, deliberately.** Capture paths generally should
   not silently discard data a decoder might need (`DiscardOnBufferOverflow = false`
   backpressures instead); render/playback paths generally should discard stale audio rather
   than let latency grow unbounded (`DiscardOnBufferOverflow = true`). Don't default both
   directions to the same setting without thinking about which failure mode is worse for it.
4. **Resample at the audio-I/O boundary, not inside every consumer.** A `DmoResampler` (or
   the rational resampler from `dsp-fundamentals`/`isobar/core/resample.h`) converting the
   device's native rate to a single canonical internal rate once is simpler and cheaper than
   every decoder/DSP stage handling arbitrary input rates itself — this mirrors
   `signal-decoding-analysis`'s `RequiredSampleRateHz` contract, just applied one layer lower
   (device rate → internal canonical rate, then internal canonical rate → per-modem rate).
5. **Enumerate and match virtual devices explicitly by name; never assume "default."**
   Per-channel virtual audio devices (DAX-style, or any per-slice/per-decoder virtual cable
   in a new app) must be found by their exact expected name and fail loudly if missing —
   silently falling back to the system default device would route audio to/from the wrong
   channel without any obvious symptom besides "wrong audio."
6. **Size buffers off latency requirements, and know who owns the tradeoff.** A larger buffer
   duration absorbs more scheduling jitter before an under/overrun but adds latency; a
   decoder needing tight timing (e.g. CW/Morse envelope timing) wants this pushed as low as
   the device reliably supports, while a background monitoring feature can tolerate more
   buffering for stability.

## Applying this to a new decoder feature

When a new decoder needs live audio from a device (as opposed to tapping an already-flowing
slice/IQ pipeline), it should go through this same layer rather than opening its own raw
WASAPI handle: reuse (or mirror) `WasapiCaptureWaveProvider`'s probe-then-fallback format
negotiation, feed its output through the canonical resample step, and only then hand samples
to an `IDecoderModem.ProcessSamples` per `signal-decoding-analysis`.

## Quick implementation checklist

- format support checked via `IsFormatSupported` before use, with an explicit fallback chain
- exclusive-mode attempts have a shared-mode fallback path, with the throw-vs-degrade choice
  made explicitly rather than left to whatever exception happens to propagate
- overflow behavior (discard vs. backpressure) chosen per direction (capture vs. render)
  based on which failure mode is worse for that path
- resampling happens once at a clear boundary (device rate → canonical rate), not
  ad hoc inside multiple consumers
- virtual/named devices are matched by exact name and fail loudly if not found, never
  silently substituted with the OS default
- buffer/latency sizing documented against the specific feature's timing requirements
