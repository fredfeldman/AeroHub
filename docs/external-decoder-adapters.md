# External Decoder Adapters

Sprint 7 adds starter adapter paths for mature external decoders. These paths preserve the distinction between supervised external processes and imported decoded-data feeds while normalizing accepted records through AeroHub's message pipeline.

## Implemented Import Sources

| Import ID | Source App | Transport | Fixture | Purpose |
|---|---|---|---|---|
| `sample-dumphfdl-json` | `dumphfdl` | HFDL | `fixtures/imports/hfdl/sample-dumphfdl.ndjson` | HFDL ACARS plus ADS-C placeholder validation |
| `sample-dumpvdl2-json` | `dumpvdl2` | VDL2 | `fixtures/imports/vdl2/sample-dumpvdl2.ndjson` | VDL2 ACARS plus CPDLC placeholder validation |

ACARS payloads route through `AcarsMessageParser`. ADS-C and CPDLC payloads are preserved as normalized placeholder messages with an `EXTERNAL_PAYLOAD_PLACEHOLDER` warning until structured application parsers are implemented.

## Process Supervision Skeleton

Sprint 7 does not launch real external binaries yet. It adds supervised process state and failure simulation for the expected tools:

- `dumphfdl`
- `dumpvdl2`
- `dump1090`

Process snapshots preserve:

- process ID and display name
- executable name
- state
- detected version when startup validation succeeds
- restart count and restart limit
- start/stop timestamps
- last error

## Simulated Failure Scenarios

The API supports simulated process states so the UI and tests can validate diagnostics before real process hosting is added.

- `start`: startup/version validation succeeds.
- `crash`: process exits unexpectedly and remains eligible for restart.
- `timeout`: no decoder output arrives before timeout.
- `malformed-output`: stdout/stderr output fails the adapter contract.
- `restart-limit`: restart limit is reached and the process is marked failed.

## API and Events

- `GET /api/external-decoders`
- `GET /api/external-decoders/diagnostics?limit=50`
- `POST /api/external-decoders/{processId}/simulate/{scenario}`
- SignalR `external.snapshot`
- SignalR `external.updated`
- SignalR `external.diagnostic.snapshot`
- SignalR `external.diagnostic`

## Reliability Notes

1. External decoder output is an interoperability boundary, not implementation code to copy.
2. Failed link-validation records are quarantined before normalization.
3. Malformed external records are diagnostics, not app crashes.
4. Real process hosting should keep version detection, stdout/stderr contracts, timeout handling, restart policy, crash-loop protection, and captured logs.
5. HFDL and VDL2 transport metadata stays attached to the normalized message provenance.

## dump1090 (ADS-B) Notes

`dump1090` is supervised as a process (start/crash/timeout/malformed-output/restart-limit) like the other decoders. Its decoded output flows into AeroHub through the existing `local-adsb-readsb-json`-style NDJSON import path rather than a bespoke parser, since `dump1090`/`readsb` JSON aircraft output already matches `AdsbAircraftImportRecord`.

Recommended Raspberry Pi start options for best position/track quality with an RTL-SDR dongle:

```
dump1090 --net --net-json-port 30047 --json --write-json /run/dump1090/data --write-json-every 1 --gain -10 --ppm 0 --net-sbs-port 30003 --net-bo-port 30005 --fix --mlat
```

- `--gain -10` (auto-gain) is a reasonable default; measure with `--gain -10` vs. a fixed max-gain value and pick whichever gives more reliable decodes without saturating on your antenna/preamp.
- `--ppm 0` unless you've measured your dongle's crystal offset (`kalibrate-rtl` or observed frequency error) — set the measured PPM correction if nonzero.
- `--fix` enables single-bit error correction, improving message yield on a Pi with a modest antenna.
- `--net-json-port`/`--write-json` are what AeroHub's import adapter reads/polls; keep `--write-json-every 1` for near-real-time track updates without excessive disk I/O on SD cards (use a tmpfs like `/run/dump1090` to avoid SD card wear).
- `--mlat` only matters if you're feeding a MLAT-capable aggregator; harmless to leave on otherwise.
- Avoid `--interactive`/console-only modes on a headless Pi; run under `systemd` so AeroHub's process supervisor can detect crashes/restarts consistently.

## Live Network Connection to a Remote dump1090

In addition to the local process-supervision skeleton, AeroHub can connect over the network to a dump1090/readsb instance that is already running elsewhere (e.g. a Raspberry Pi on the LAN) and poll its JSON aircraft feed directly, without any file import step.

- `GET /api/imports/dump1090/status` — current connection snapshot (state, host/port, accepted/rejected counts, last poll time, last error).
- `GET /api/imports/dump1090/diagnostics?limit=50` — recent diagnostics for the network connection.
- `POST /api/imports/dump1090/connect` — body `{ "host": "192.168.50.146", "port": 8080, "jsonPath": "/data/aircraft.json", "pollIntervalSeconds": 5 }`. Starts a background poll loop against `http://{host}:{port}{jsonPath}`.
- `POST /api/imports/dump1090/disconnect` — stops the poll loop.
- SignalR `dump1090.updated` (connection state) and `dump1090.diagnostic` (diagnostic events); hub methods `GetDump1090Status` / `GetDump1090Diagnostics` return snapshots on demand.

Notes:

- The default `jsonPath` (`/data/aircraft.json`) matches `dump1090-mutability`/stock `dump1090`. FlightAware's `dump1090-fa` typically serves the same data at `/skyaware/data/aircraft.json` — pass that as `jsonPath` when connecting to a FlightAware install.
- Records are validated with the same rules as the file-based ADS-B import (six-character hex address, valid lat/lon range, stale-position threshold) and are quarantined with the matching diagnostic codes (`IMPORT_SCHEMA_MISMATCH`, `IMPORT_ADSB_INVALID_POSITION`, `IMPORT_ADSB_STALE`) before being upserted into the aircraft track store.
- A failed poll (unreachable host, non-JSON response, timeout) marks the connection `Degraded` with a `DUMP1090_POLL_FAILED` diagnostic and keeps retrying on the configured interval; it does not automatically disconnect.
- This feature intentionally lets an operator point AeroHub at any host/port on their own network, the same trust model as the existing hardware-source adapters (e.g. `rtl-tcp-local`). Treat the connect endpoint as an operator/admin action, not something to expose to untrusted callers.

## Reliability Notes
