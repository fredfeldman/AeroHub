---
name: wefax-weather-fax
description: "Use when implementing or reviewing WEFAX/weather-fax decoding in an SDR app, including HF weather fax reception, FM subcarrier demodulation, 300/450 Hz start-stop tones, IOC and scan-rate handling, line sync, slant correction, image rendering, and isobar-style decoder architecture."
---

# WEFAX Weather Fax

## Purpose

Use this skill for WEFAX, also called weather fax or HF radiofax. WEFAX is an image
mode, not a text message mode: the decoder turns an audio/FM subcarrier stream into
scan lines, sync state, and a grayscale image.

Recommended processing boundary:

`audio/IQ samples -> passband filter -> FM subcarrier demodulator -> line timing/sync -> image line buffer -> rendered fax image`

Keep WEFAX image formation in a decoder/core layer. WPF views should display pixels,
decoder state, and controls; they should not own DSP state machines or line-sync logic.

## Local references

The `signal-decoding-analysis` skill identifies `D:\source\repos\Decoders\isobar` as
the strongest local reference for this mode. Treat it as an architectural and behavior
reference, not as code to copy without license review.

Useful isobar patterns to preserve:

- a dependency-free DSP core separate from GUI and CLI consumers
- a streaming `feed(sample, out)` decoder shape
- FM discriminator, filtering, rational resampling, and tone detection as core services
- 300 Hz and 450 Hz start/stop tone detection reported over fixed analysis blocks
- line-sync tracking with explicit confidence states rather than a single locked flag
- image output that can be consumed by either a GUI or batch/CLI tool

Load `third-party-license-compliance` before porting or closely following any external
WEFAX implementation. Original code and independently written tests are preferred.

## WEFAX facts to model

WEFAX implementations commonly need these configurable properties:

- scan rate in lines per minute, often 60, 90, 100, 120, or 240 rpm
- IOC, commonly 576 for many weather charts
- audio subcarrier range, often around a 1500-2300 Hz black/white span
- start and stop tones, commonly 300 Hz and 450 Hz
- polarity: normal or inverted image intensity
- slant correction caused by sample-rate or timing mismatch
- phasing and line-sync confidence
- manual crop, restart, and resync controls

Do not hard-code a single station profile as the protocol. Weather services vary by
schedule, chart type, line rate, and transmitter chain.

## Normalized decoder output

A useful WEFAX decoder model should retain:

- receive timestamp and source channel
- tuned RF frequency, receiver mode, audio sample rate, and passband settings
- selected IOC, line rate, polarity, and slant correction
- start/stop tone detection levels and timestamps
- sync state: searching, locked, corrected, coasting, or lost
- line number, line timing, and confidence per line where practical
- raw or demodulated fixture reference for replay diagnostics
- rendered grayscale image buffer or scan-line stream
- decoder warnings for missing tones, unstable sync, clipping, or sample-rate mismatch

The UI should expose image state and controls through view models. Avoid binding a WPF
control directly to mutable DSP internals.

## Decoder workflow

1. Select a narrow audio passband around the WEFAX subcarrier.
2. Normalize or limit levels enough to prevent clipping without destroying contrast.
3. Demodulate the FM subcarrier into image intensity.
4. Detect start/stop tones independently from the image demodulator.
5. Estimate scan rate and line length from configuration or sync evidence.
6. Track line sync and report confidence per line or block.
7. Apply polarity and slant correction after preserving enough raw line evidence.
8. Emit image lines incrementally so long captures do not block the UI thread.
9. Preserve partial images when sync is lost.
10. Record replay metadata with every saved image fixture.

Unknown or weak captures should still produce diagnostic output. A partial noisy chart
with sync confidence and tone metrics is more useful than a silent decoder failure.

## Rendering guidance

For WPF rendering, pair this skill with `wpf-realtime-rendering`:

- use a stable image buffer or `WriteableBitmap` target
- append or scroll scan lines without reallocating the whole image every frame
- throttle UI updates separately from decoder sample processing
- keep pixel conversion and palette mapping deterministic for tests
- expose manual resync, polarity, slant, contrast, and save-image controls

Do not make rendering cadence control DSP cadence. The decoder should continue
processing samples even when the UI skips frames.

## Testing strategy

Every WEFAX implementation should have focused tests for:

- FM subcarrier demodulation from synthetic tones
- start and stop tone detection thresholds
- configured line-rate to samples-per-line conversion
- IOC-derived image width handling
- sync acquisition, correction, coasting, and loss
- polarity inversion and contrast mapping
- slant correction direction and magnitude
- partial capture preservation
- malformed, clipped, short, or sample-rate-mismatched input

Use synthetic fixtures for deterministic DSP tests and captured WAV/IQ replay fixtures
for end-to-end validation. Store enough metadata with each fixture to reproduce receiver
mode, sample rate, tuned frequency, passband, line rate, IOC, and expected image size.