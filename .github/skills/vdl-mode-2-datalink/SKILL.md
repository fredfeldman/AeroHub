---
name: vdl-mode-2-datalink
description: "Use when working with VDL Mode 2 or VHF ACARS datalink traffic, including VDL2 reception, AVLC frames, ACARS-over-VDL routing, aircraft addresses, ground stations, message validation, and integration with ACARS application parsing."
---

# VDL Mode 2 Datalink

## Purpose

Use this skill for VHF Data Link Mode 2 work: receiving VDL2 bursts, validating link
frames, extracting aircraft and ground-station metadata, and handing validated ACARS
or ATN payloads to the appropriate application parser.

VDL2 is a transport/link layer, not an ACARS label parser. Keep the pipeline split:

`RF/audio samples -> VDL2 modem -> AVLC/link frame -> payload classifier -> ACARS/ATN parser`

The ACARS skill owns text label parsing after a valid ACARS payload exists. CPDLC and
ADS-C application data should route to the CPDLC/ADS-C skill when carried over ATN or
FANS-style payloads.

## Implementation boundaries

For this workspace:

- keep VDL2 modem and frame validation independent from WPF controls
- normalize aircraft and ground-station addresses before UI binding
- preserve raw frame bytes and validation state beside parsed payloads
- expose payload type and transport metadata to downstream parsers
- avoid mixing VDL2 channel metrics with ACARS application fields

Do not pass demodulated but unvalidated payload bytes into the ACARS parser. Record
partial or CRC-failed frames as diagnostics instead.

## Fields to preserve

A useful normalized VDL2 record should include:

- receive timestamp
- tuned frequency, channel name, and demodulator settings
- signal level, SNR, frequency offset, and burst timing
- source and destination link addresses
- frame type and sequence/control fields
- raw frame bytes
- FCS/CRC validation status
- payload classifier: ACARS, ATN, management, unknown, or corrupted
- decoded ACARS/ATN payload when validated
- parser warnings

The same aircraft may appear via VDL2, HFDL, SATCOM, and ADS-B. Correlate by stable
aircraft identifiers only after each transport has preserved its own evidence.

## Testing strategy

Tests should cover:

- frame length and FCS validation
- address extraction and direction
- payload classification
- ACARS-over-VDL handoff using text fixtures
- corrupted or truncated burst preservation
- duplicate frames and sequence behavior
- frequency/channel metadata propagation

Use dumpvdl2 or other mature decoders as comparison tools where helpful, but keep the
implementation original and verify licenses before adapting code.