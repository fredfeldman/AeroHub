# AeroHub Architecture

## Sprint 0 Summary

AeroHub will use a .NET backend and React/TypeScript frontend. The backend owns signal ingestion, decoded-data imports, DSP, protocol parsing, normalization, storage, correlation, process supervision, and real-time APIs. The frontend owns presentation, filtering, selection, visualization, and operator commands.

## Core Boundary

Raw IQ/audio samples stay in the backend by default. React receives reduced live products only:

- normalized aviation messages
- aircraft tracks and observations
- decoder/import status
- health and diagnostics
- FFT bins and waterfall rows
- WEFAX image lines and image metadata

Frontend code must not become a protocol parser, import parser, or decoder engine.

## Canonical Ingestion Pipeline

```text
SourceAdapter -> Decoder/ImportAdapter -> Validation -> Normalization -> Deduplication -> Storage -> Live Broadcast -> UI
```

Native decoder output, supervised external decoder output, and imported decoded-data feeds all join this pipeline after adapter-level validation.

## Backend Projects

- `AeroHub.Api`: REST endpoints, SignalR hubs, optional high-rate binary WebSocket endpoints, API composition.
- `AeroHub.Contracts`: DTOs and event contracts shared by backend layers and frontend clients.
- `AeroHub.Core`: domain interfaces, ingestion pipeline contracts, decoder/import manager abstractions, correlation interfaces.
- `AeroHub.Decoders`: ACARS, HFDL, VDL2, ADS-B/Mode S, CPDLC/ADS-C, SATCOM, and WEFAX modules.
- `AeroHub.Infrastructure`: fixture replay, storage, external process supervision, import adapters, settings, and runtime services.

## Frontend Areas

- Live RF: spectrum, waterfall, source state, and stream metrics.
- Messages: normalized ACARS, HFDL, VDL2, SATCOM, CPDLC/ADS-C, and unknown payload inspection.
- Aircraft: track table, observation history, and map placeholder.
- Decoders: native decoder catalog, state, configuration, and diagnostics.
- Imports: external decoded-data source configuration, status, diagnostics, and quarantine.
- WEFAX: image-line rendering, metadata, tone/sync state, polarity, contrast, slant, and export workflow.
- Settings: source profiles, import profiles, display preferences, retention, and diagnostics options.

## Reliability Principles

1. Fixture replay comes before live hardware for each feature.
2. Every decoded or imported record preserves provenance, raw references, confidence, and warnings.
3. High-rate streams require bounded queues, metrics, and drop/coalesce policies.
4. External decoder processes require startup validation, log capture, restart limits, and crash-loop protection.
5. Storage must have retention policies before long-running capture/import work begins.