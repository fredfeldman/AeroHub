# Hardware Source Adapters

Sprint 11 adds the first live-input adapter surface without requiring attached SDR hardware.

The implementation is simulator-first: source capability, ownership, metrics, diagnostics, and failure behavior are exercised through deterministic adapters before real device I/O is added.

## Sources

The current source catalog contains:

- `file-audio-iq`: file/audio IQ input adapter for recorded or virtual-audio workflows.
- `rtl-tcp-local`: rtl_tcp-style SDR adapter skeleton for future RTL-SDR network input.

## Capabilities

Each source publishes:

- sample-rate range
- maximum bandwidth
- frequency range
- gain control names
- exclusive-ownership requirement
- stream framing model
- whether tune/control commands are acknowledged

The rtl_tcp source explicitly models fire-and-forget control and raw unsigned 8-bit interleaved IQ framing. The file/audio source models acknowledged control and timestamped audio buffers.

## Ownership

Hardware sources require an owning decoder. Exclusive sources reject a second active owner while another source is online for a decoder.

This keeps decoder/source compatibility explicit and prevents two decoders from assuming they own the same physical input.

## Metrics

Hardware source metrics include:

- configured sample rate
- configured bandwidth
- tuned frequency
- buffers received
- dropped buffers
- reconnect count
- signal level
- SNR
- queue depth
- clipping state
- last buffer timestamp

## Diagnostics

The simulated scenarios are:

- `buffer-drop`
- `reconnect`
- `clipping`
- `failure`

Diagnostics are exposed through REST and SignalR so the UI can display actionable source health state.

## API

- `GET /api/hardware-sources`
- `GET /api/hardware-sources/diagnostics`
- `POST /api/hardware-sources/start`
- `POST /api/hardware-sources/{sourceId}/stop`
- `POST /api/hardware-sources/{sourceId}/simulate/{scenario}`

## Next Hardware Work

Real adapters should keep the same manager contract and add device-specific I/O behind it. The rtl_tcp implementation should validate the `RTL0` handshake before consuming IQ bytes, resend critical state after reconnect, and surface host driver/setup problems as diagnostics.
