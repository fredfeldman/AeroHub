# WEFAX Image Decoder Skeleton

Sprint 9 adds a synthetic WEFAX/weather-fax image path. This is an image-mode decoder skeleton, not a full HF FM-subcarrier demodulator.

## Implemented Contracts

- `WefaxDecoderState`: source state, sync state, IOC, line rate, polarity, slant correction, image width, line count, timestamps, and warnings.
- `WefaxToneMetrics`: start/stop tone levels and measurement timestamp.
- `WefaxImageLine`: sequence, line number, width, sync confidence, tone metrics, and grayscale pixels.
- `WefaxLineBatch`: one or more streamed WEFAX lines plus decoder state.
- `WefaxReplayResult`: replay summary and final sync state.

## Implemented Service

- Service: `SyntheticWefaxReplayService`
- Source ID: `synthetic-wefax-replay`
- API state endpoint: `GET /api/wefax/state`
- API replay endpoint: `POST /api/wefax/replay?lineCount=96&partial=false`
- API controls endpoint: `POST /api/wefax/controls?isInverted=false&slantCorrection=0`
- SignalR events: `wefax.lines` and `wefax.state`

## Decoder Behavior

The synthetic replay emits deterministic grayscale scan lines with:

- IOC 576
- 120 rpm line rate
- searching, locked, corrected, coasting, and lost sync states
- start/stop tone metrics
- polarity inversion
- slant correction
- partial-capture warning support

Full FM subcarrier demodulation, real tone detection, and captured WAV/IQ replay are intentionally deferred until image contracts and UI behavior are stable.

## UI Behavior

The React WEFAX panel includes:

- full replay
- partial replay
- inversion toggle
- slant slider
- resync/reset controls
- canvas-backed grayscale image rendering
- state, line count, IOC, line rate, slant, and warning display

The frontend keeps a bounded WEFAX line buffer so long image streams do not grow UI memory without limit.