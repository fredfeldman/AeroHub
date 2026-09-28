# Synthetic Spectrum and Waterfall Streams

Sprint 5 adds synthetic RF stream replay so the backend/frontend real-time visualization contracts can be tested before live hardware exists.

## Implemented Source

- Source ID: `synthetic-rf-replay`
- API replay endpoint: `POST /api/streams/synthetic/replay?speedMultiplier=1`
- API metrics endpoint: `GET /api/streams/synthetic/metrics`
- SignalR events: `spectrum.frame`, `waterfall.rows`, and `stream.metrics`

## Replay Speeds

The starter replay supports bounded speed multipliers:

| Speed | Spectrum Frames | Waterfall Rows | Purpose |
|---:|---:|---:|---|
| 1x | 5 | 5 | Smoke test and UI baseline |
| 5x | 25 | 25 | Moderate replay burst |
| 20x | 100 | 100 | High-rate burst and buffer behavior |

## Contracts

Spectrum frames include:

- source ID
- sequence number
- generated timestamp
- center frequency
- span
- reduced FFT bins

Waterfall row batches include:

- source ID
- first sequence number
- generated timestamp
- row width
- one or more intensity rows

Stream metrics include:

- state
- speed multiplier
- spectrum frames produced
- waterfall rows produced
- dropped visualization frames
- queue depth
- slow client count
- last generated timestamp

## Current Budgets

The Sprint 5 implementation uses SignalR/JSON. It does not stream raw IQ or audio samples to the frontend.

React keeps a bounded waterfall buffer of the latest 80 rows. The frontend may discard stale visualization state because spectrum/waterfall frames are reduced visual products, not authoritative decoded messages.

Binary WebSocket transport remains deferred until profiling proves SignalR/JSON is too expensive.

## Validation

Backend tests cover 1x, 5x, and 20x replay frame counts, row width, event emission, and final metrics. Browser validation should confirm that replay buttons update the canvas views and stream metrics without blocking the message/import UI.