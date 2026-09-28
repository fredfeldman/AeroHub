# AeroHub Performance Budgets

These are Sprint 0 starter budgets. They are intentionally conservative and should be revised after measurement.

## Live Stream Budgets

| Stream | Starter Budget | Drop/Coalesce Policy |
|---|---:|---|
| Spectrum frames | 10-20 fps per visible channel | Drop stale frames before queuing new frames |
| Waterfall rows | 20-60 rows/sec per visible channel | Coalesce rows into batches; drop oldest visualization batches when client lags |
| Message events | 250 events/sec aggregate | Do not silently drop; backpressure, persist, and mark client lag |
| Aircraft track updates | 10 updates/sec visible aggregate | Coalesce by aircraft ID and keep latest state |
| WEFAX lines | Mode-dependent, normally low rate | Batch lines; preserve image state even if UI skips paint |
| Health metrics | 1 update/sec | Coalesce to latest |
| Import diagnostics | 100 events/sec aggregate | Persist summaries; sample repeated identical errors |

## Queue Budgets

| Boundary | Starter Limit | Behavior |
|---|---:|---|
| Decoder to message bus | 10,000 records | Backpressure decoder/import adapter if persistent storage cannot keep up |
| Message bus to SignalR client | 2,000 records/client | Mark client lag and ask client to resync snapshot |
| Spectrum/waterfall producer to broadcaster | 3 seconds of visual data | Drop/coalesce visualization frames only |
| Import adapter quarantine | 10,000 records or configured disk cap | Stop import and require operator action if exceeded |
| External process stdout buffer | 10 MB/process | Kill or pause process adapter according to supervision policy |

## Latency Targets

- Backend health snapshot: under 250 ms locally.
- Message visible in UI after normalization: under 1 second during normal load.
- Aircraft track visible in UI after update: under 1 second during normal load.
- Spectrum/waterfall UI interaction: no visible stalls during replay at 1x.
- Long replay memory behavior: bounded over at least 30 minutes.

## Measurement Requirements

1. Each high-rate stream reports producer rate, broadcast rate, queue depth, dropped/coalesced count, and slow-client count.
2. Replay tests run at 1x, 5x, and 20x speed before live hardware work depends on the stream.
3. Frontend tables with high-volume messages or tracks must use virtualization before large imports are enabled.
4. Any binary stream decision must be justified by measurement against the JSON/SignalR baseline.