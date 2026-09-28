---
name: generic-sdr-hardware
description: "Use when adding support for consumer/hobbyist SDR hardware other than Flex radios — RTL-SDR, HackRF, Airspy, SDRplay, PlutoSDR, USRP, or similar tuners — including the rtl_tcp protocol, gain models, decimation strategy, and safe PTT/TX gating. Grounded in hampi-dashboard's real rtl_tcp client and Digirig PTT interface; complements radio-protocol-conventions (Flex-specific) with a broader, simpler-hardware-class view."
---

# Generic SDR Hardware Support (Beyond Flex Radios)

## Purpose

`radio-protocol-conventions` documents the Flex-specific protocol (`FlexLib`/`Radio.cs`):
text commands, VITA-49 streaming, sequence-number reply correlation. That pattern assumes
a fairly sophisticated radio with its own DSP and multiple simultaneous named streams. Most
consumer/hobbyist SDR hardware is much simpler — a single wideband IQ stream plus a handful
of tuning/gain parameters — and needs a different, lighter integration shape. This skill
covers that shape, grounded in `hampi-dashboard`'s real RTL-SDR integration (see
`signal-decoding-analysis`'s local reference repos).

Use this skill when you need to:

- add support for an RTL-SDR, HackRF, Airspy, SDRplay, PlutoSDR, USRP, or similar tuner
  as an alternative or additional hardware backend
- decide how to abstract multiple hardware backends behind one interface so
  `DecoderManager`/`SpectrumDisplayManager` don't need to know which tuner is attached
- implement a decimation strategy from a wide native SDR sample rate down to a usable
  decode/audio rate
- add transmit (PTT) support safely, if the new app/hardware combination supports TX at all

## Reference: `hampi-dashboard`'s RTL-SDR integration

### rtl_tcp: a real, simple binary tuner-control protocol

`backend/sdr.py` talks to `rtl_tcp` (the standard RTL-SDR network server) over a plain TCP
socket:

- **Handshake**: the server sends a fixed 12-byte header on connect — 4-byte magic `b"RTL0"`,
  4-byte tuner type, 4-byte tuner gain count — before any IQ data flows. Always validate this
  magic before trusting the rest of the stream; a wrong magic means you're not talking to
  what you think you are.
- **Commands are a single fixed-size wire format**: a 1-byte opcode (`CMD_FREQ = 0x01`,
  `CMD_SAMPLE_RATE = 0x02`, `CMD_GAIN_MODE = 0x03`, `CMD_GAIN = 0x04`, and similarly-numbered
  opcodes for the rest of the tunable parameters) followed by a 4-byte big-endian parameter,
  sent one-way over the same socket — there is no reply/ack for a tuning command, unlike
  Flex's reply-table pattern. **This means tuning is fire-and-forget**: the app must assume
  the command took effect after a short settle time rather than waiting for confirmation, and
  should re-send critical parameters (gain, sample rate) after reconnecting rather than
  assuming the device remembers app-side intent.
- **IQ data is a raw unsigned-8-bit interleaved I/Q byte stream** immediately following the
  header, with no further framing — contrast this with VITA-49's packet/header-per-chunk
  structure in `radio-protocol-conventions`. A generic hardware abstraction needs to handle
  both "continuously flowing raw bytes, no per-chunk metadata" and "explicitly framed packets
  with sequence/stream IDs" as equally valid stream shapes.
- **Kernel driver ownership is a real, hardware-specific gotcha**: the RTL-SDR's USB device
  is claimed by the kernel's own DVB driver (`dvb_usb_rtl28xxu`) by default and must be
  blacklisted before `rtl_tcp`/`librtlsdr` can open it — a real Linux example, but the
  general point applies on Windows too (WinUSB/Zadig driver replacement for RTL-SDR is the
  Windows-side equivalent). Document this kind of one-time host-level setup requirement
  explicitly per supported hardware family rather than assuming "plug and play."

### Two-stage decimation: a documented, verified pattern

`sdr.py`'s comments document a real, measured decimation strategy worth reusing verbatim as
a template (see `dsp-fundamentals` for the underlying theory): going from a 2.4 MHz native
capture rate down to 48 kHz audio in **two stages** (10× then 5×) rather than one 50×
stage, with a lowpass filter sized correctly *at each stage's own rate* — the comment
explicitly notes a filter is "completely ineffective" (−0.7 dB attenuation) if sized for the
final rate but applied at the original high rate, versus effective (−72 dB) when applied
after decimation at the rate it was actually designed for. **Always filter at the rate the
filter was designed for, immediately before or as part of that stage's decimation** — this
is a specific, concrete instance of the general "decimate only after an anti-alias filter at
the *new* Nyquist rate" rule in `dsp-fundamentals`, made more precise: multi-stage
decimation needs a correctly-scaled filter at every stage, not just the first and last.

### Per-mode filter state isolation

`sdr.py` keeps entirely separate filter state (`_fm_dec_zi`, `_am_dec_zi`, `_nbfm_dec_zi`,
etc.) for each demodulation mode even though they share the same filter coefficients and
run on the same IQ stream — explicitly so that "a scanner retune cannot smear another mode's
filter state mid-decode." This generalizes directly into
`signal-decoding-analysis`/`sdr-generated-app.instructions.md`'s rule that `DecoderManager`
owns per-slice/per-mode decoder instances: **never share IIR/FIR filter delay-line state
(`zi`) across two logically independent decode paths**, even if they're structurally
identical, because one path's transient history leaking into another produces subtle,
hard-to-diagnose artifacts rather than an obvious failure.

### Safe PTT/TX gating

`backend/radio.py` (Digirig serial-RTS PTT keying) is a good template for adding transmit
capability to any new app, regardless of hardware family:

- The serial port is opened with RTS/DTR **immediately deasserted**, so simply opening the
  device can never key the radio — keying is a separate, explicit action.
- Every transmit call is **hard-gated behind two independent conditions**
  (`tx_enable` config flag **and** a non-empty configured callsign) checked in a single
  `_guard()` method called from every TX entry point — not scattered inline checks that
  could be missed on one path.
- PTT access is serialized with a lock (`_tx_lock`) since TX methods run on a thread pool —
  two concurrent callers must never both believe they hold the key line.
- A `ready` property exposes whether TX is actually usable (`tx_enable and callsign and
  port open`) so the UI can reflect true TX readiness rather than just "is TX theoretically
  configured."

Any new app adding transmit capability — even just a calibration tone or a keyed carrier —
should copy this shape: explicit safe-by-default device open, a single centralized guard
covering every precondition, and serialized access to the keying mechanism. This is a real
safety concern (uncontrolled/unintended RF transmission), not just a code-quality nicety.

## Abstracting multiple hardware backends behind one interface

A new app that might support both a sophisticated radio (Flex-style, see
`radio-protocol-conventions`) and a simple tuner (RTL-SDR-style, this skill) should define
one hardware-source abstraction that both implement, e.g.:

```
IRadioHardwareSource
  IReadOnlyList<TuningParameter> SupportedGainModes   // discrete steps vs. AGC vs. LNA+VGA split, etc.
  void SetFrequency(double hz)
  void SetSampleRate(double hz)
  void SetGain(GainMode mode, double value)
  event IqSamplesReceived                              // raw stream, framed or unframed
  event ConnectionStateChanged
```

Differences to model explicitly rather than assume away:
- **command acknowledgment**: some hardware confirms a tuning command (Flex); some doesn't
  (rtl_tcp) — the abstraction should not assume a reply always arrives.
- **gain model**: some hardware has a single discrete gain list; others split LNA/VGA/mixer
  gain, or only expose AGC on/off — don't hardcode a single-slider gain UI assumption.
- **stream framing**: packetized-with-metadata (VITA-49) vs. raw continuous bytes (rtl_tcp)
  need different demux handling upstream of any decoder.
- **exclusive hardware access**: this ties directly into `signal-decoding-analysis`'s
  hampi-dashboard-derived single-hardware-arbiter pattern — whichever backend is active still
  needs the same `DecoderManager`-level arbitration described there.

## Quick implementation checklist

- validated any handshake/magic bytes before trusting a hardware stream
- confirmed whether the hardware's control channel acknowledges commands or is
  fire-and-forget, and handled reconnection accordingly (re-send state, don't assume memory)
- decimation implemented as filter-then-decimate at *each* stage's own rate for multi-stage
  decimation, not just filtered once at the original or final rate
- filter/decoder delay-line state kept fully separate per mode/slice, never shared across
  logically independent decode paths
- any TX/PTT capability hard-gated behind an explicit, centralized precondition check and
  serialized access to the keying mechanism
- one hardware-source abstraction defined if more than one hardware family is supported,
  explicitly modeling ack-vs-fire-and-forget, gain model, and stream framing differences
- documented any one-time host/driver setup required for a given hardware family
  (kernel driver blacklist, WinUSB/Zadig replacement, udev rules, etc.)
