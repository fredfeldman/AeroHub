---
name: radio-protocol-conventions
description: "Use when implementing or extending communication with a radio/hardware device over a command-response protocol plus a separate high-rate streaming channel (VITA-49-style) — command/reply correlation, per-stream dataflow demuxing, and reconnect/timeout handling. Grounded in this repo's actual FlexLib Radio.cs implementation."
---

# Radio/Hardware Protocol Conventions

## Purpose

`smart-sdr-architecture` says protocol/hardware logic belongs in `FlexLib`/`FlexControl`,
non-WPF; this skill covers the actual shape of that protocol layer, grounded in
`FlexLib/Flex.Smoothlake.FlexLib/Radio.cs` — a real, working implementation of "one
text command/response channel plus multiple high-rate binary streams" that generalizes to
any SDR/radio protocol, not just Flex hardware.

Use this skill when you need to:

- implement or extend a command/response protocol to a radio, SDR, or other hardware device
- correlate an asynchronous reply back to the specific command that triggered it
- demux several concurrent high-rate binary streams (spectrum/FFT, meters, audio, IQ) that
  arrive interleaved on the same transport
- decide how much of the protocol's parsing/dispatch logic should be allocation-free

This skill covers Flex-style hardware (sophisticated radio, acknowledged commands, multiple
named streams). If the hardware is a simpler tuner (RTL-SDR-class, fire-and-forget tuning,
a single raw IQ stream) see `generic-sdr-hardware` instead, and its guidance on abstracting
both shapes behind one interface if an app needs to support more than one hardware family.

## Reference implementation: `Radio.cs`

- **Text commands, correlated replies via sequence number.** Commands are plain strings
  (`SendCommand("radio set mute_local_audio_when_remote=" + ...)`) — a simple,
  human-debuggable text protocol rather than a binary command format. Each command that
  expects a reply is registered in a `Hashtable _replyTable` keyed by a sequence number
  (`_cmdSequenceNumber`) with a `ReplyHandler` delegate; when a reply referencing that
  sequence number arrives, the handler is looked up, removed from the table, and invoked.
  This is a general async request/response correlation pattern independent of the specific
  transport — the same shape works for any protocol where requests and responses can arrive
  out of order or be delayed.
- **A separate high-rate binary channel, not multiplexed through the text protocol.** VITA-49
  packets (meters, FFT/spectrum, audio, IQ) arrive over UDP as `UdpVitaPacket`s and are
  processed through an `ActionBlock<UdpVitaPacket> _udpPacketPipeline` (TPL Dataflow) — i.e.
  a dedicated, back-pressure-aware async pipeline separate from the synchronous-feeling
  command/reply channel, because streaming data has completely different volume and latency
  characteristics than command/response traffic.
- **Per-stream demuxing via a dictionary of dataflow blocks.** Once a stream is identified,
  `_streamWorkers` (a `ConcurrentDictionary<uint, ActionBlock<(byte[] Data, int Bytes)>>`)
  gives each individual stream ID its own worker block, so one slow/stalled stream's
  processing doesn't block another stream's — this is the protocol-layer analogue of
  `signal-decoding-analysis`'s "run multiple lightweight decoders concurrently" pattern, just
  one layer lower (demuxing raw stream data, before any decoder ever sees it).
- **Allocation avoidance on the hot path.** `t_reusableIFDataPacket` (a static reusable
  `VitaIFDataPacket`) and `ConcurrentQueue<VitaMeterPacket>`/`ConcurrentQueue<VitaFFTPacket>`
  paired with `AutoResetEvent` signals (`_semNewMeterPacket`, `_semNewFFTPacket`) show a
  deliberate low-allocation, signal-driven consumer pattern for the highest-rate packet
  types — worth mirroring for any new stream type that arrives at audio-rate or faster.
- **Reply table is cleared on disconnect.** `_replyTable.Clear()` runs as part of teardown —
  any pending reply handlers for a connection that's going away are simply dropped rather
  than left to fire (or leak) after the connection is gone.

## Design principles to carry into a new app

1. **Separate the command/response channel from the streaming channel.** Don't try to
   correlate high-rate binary stream packets through the same sequence-number/reply-table
   mechanism as text commands — they have fundamentally different cardinality and timing.
2. **Correlate async replies by an explicit id, with an explicit table, not implicit
   ordering assumptions.** Never assume replies arrive in the same order commands were sent;
   a hashtable/dictionary keyed by sequence number (or request id) plus a registered handler
   is the general-purpose pattern, and it needs an explicit teardown path (clear the table)
   on disconnect so stale handlers can't fire against a dead connection.
3. **Demux concurrent streams into independent processing units.** One dataflow block (or
   equivalent bounded queue + worker) per stream id/channel means a problem in one stream
   (slow decoder, backed-up consumer) doesn't stall unrelated streams sharing the same
   transport.
4. **Keep the hottest paths allocation-free.** For packet types arriving at high rate (FFT
   bins, meter updates, audio), prefer reusable buffers/objects and lock-free queues
   (`ConcurrentQueue`) with a signal (`AutoResetEvent`) over allocating a new object per
   packet — this matters more here than almost anywhere else in the app because it runs
   continuously for the life of the connection.
5. **Plan explicit reconnect/timeout handling, not just the happy path.** A protocol layer
   for real hardware needs to handle: the device disappearing mid-session (teardown pending
   replies, stop stream workers), a command that never gets a reply (a timeout, not an
   indefinite wait), and reconnection re-establishing whatever per-stream state existed
   before the drop. `Radio.cs`'s reply-table-clear-on-disconnect is the minimum version of
   this; a new protocol layer should also decide explicit timeout behavior for commands that
   never get an answer.
6. **A text command protocol is a legitimate choice, not just a legacy one.** Human-readable
   commands (`"radio set X=Y"`) trade a small amount of parsing/bandwidth efficiency for much
   easier debugging (you can read a packet capture or log line and understand it immediately)
   — a reasonable default for a new device's control channel unless there's a specific reason
   (bandwidth, latency) to go binary.

## Quick implementation checklist

- command/response channel and high-rate streaming channel are architecturally separate,
  not multiplexed through the same correlation mechanism
- async replies correlated by an explicit id in an explicit table, with a teardown path that
  clears/cancels pending handlers on disconnect
- each concurrent stream gets its own processing unit (dataflow block/bounded queue), not a
  single shared queue serializing unrelated streams
- hottest-path packet types use reusable buffers/objects and lock-free queues rather than
  allocating per packet
- explicit timeout handling exists for commands that never receive a reply
- reconnect behavior is designed deliberately (what state is dropped vs. re-established),
  not left as an accident of whatever the disconnect path happens to do
