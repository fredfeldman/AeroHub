---
name: flight-tracking-data-feeders
description: "Use when forwarding, feeding, or proxying aviation surveillance (ADS-B, Mode S, UAT) and datalink (ACARS, HFDL, VDL2) data to online tracking services (FlightAware, Flightradar24, ADS-B Exchange, OpenSky Network, RadarBox, Planefinder, Airframes.io): Beast binary, AVR hex, BaseStation SBS-1 (port 30003), aircraft.json pushing, UDP/TCP NDJSON, MLAT client sync, and feeder proxy architectures."
---

# Flight Tracking Data Feeders & Protocols

## Purpose

This skill covers sending, proxying, and re-broadcasting aviation surveillance data (ADS-B 1090 MHz, Mode S, UAT 978 MHz) and datalink traffic (ACARS, HFDL, VDL Mode 2, SATCOM) to major online flight-tracking aggregator networks and hobbyist communities.

An SDR monitoring application like AeroHub can act both as a **data producer** (decoding RF and feeding external services) and a **feed proxy / aggregator** (serving standardized local ports like 30005/30003 so standalone feeder daemons like `piaware`, `fr24feed`, or `rbfeeder` can consume its stream).

Recommended processing boundary:

`RF Decoder / Import Engine -> Feeder Pipeline / Multiplexer -> Protocol Framer (Beast / AVR / BaseStation / NDJSON) -> TCP Client / UDP Socket / TLS Stream -> Online Service / Feeder Client`

---

## Major Aggregator Networks & Feeder Software

| Network / Service | Feeder Software | Primary Ingestion Protocol / Port | Data Types Accepted | MLAT Support |
|---|---|---|---|---|
| **FlightAware** | `piaware`, `faup1090`, `faup978` | Mode S Beast Binary (TCP 30005) or dump1090 `aircraft.json` | ADS-B 1090ES, Mode S, UAT 978 | Yes (`mlat-client`) |
| **Flightradar24** | `fr24feed` | Beast Binary (30005), AVR (30002), or BaseStation (30003) | ADS-B, Mode S, Mode A/C | Yes |
| **ADS-B Exchange** | `adsbexchange-feed`, `readsb` | Beast Binary stream (pushed to feed server) + MLAT client | ADS-B, Mode S, UAT, ACARS/VDL2 | Yes (Unfiltered) |
| **OpenSky Network** | `openskyd`, `pyopensky` | Raw Mode S / Beast timestamped frames | ADS-B, Mode S | Yes (High-precision TS) |
| **RadarBox (AirNav)** | `rbfeeder` | Beast Binary (30005) or Raw Mode S | ADS-B, Mode S, Mode A/C | Yes |
| **Planefinder** | `pfclient` | Beast (30005) or AVR (30002) | ADS-B, Mode S | Yes |
| **Airframes.io** | `acars-router`, `dumphfdl`, `dumpvdl2` | UDP / TCP NDJSON feeds, REST API | ACARS, HFDL, VDL2, SATCOM | N/A (Datalink) |
| **SondeHub** | `radiosonde_auto_rx`, REST API | HTTP REST JSON / APRS-IS | Radiosonde Telemetry | N/A (Telemetry) |

---

## Standard Feeder Network Protocols & Formats

### 1. Mode S Beast Binary Protocol (TCP Port 30005)

The **Beast binary protocol** (developed by Jetvision for the Mode-S Beast) is the industry standard binary format for streaming raw Mode S, ADS-B, and Mode A/C frames with precise timestamps and signal strength.

**Frame Structure:**
- Every message begins with an escape byte: `0x1A`.
- **Message Type**:
  - `0x31` ('1'): Mode A/C (2 bytes data)
  - `0x32` ('2'): Mode S Short (7 bytes data / 56 bits)
  - `0x33` ('3'): Mode S Long (14 bytes data / 112 bits)
  - `0x34` ('4'): Signal level / DIP switch status settings
- **Timestamp**: 6 bytes (48-bit clock ticks at $12\text{ MHz}$, $83.33\text{ ns}$ resolution).
- **Signal Level**: 1 byte ($0\text{--}255$ scale, where $255 = \text{0 dBFS}$).
- **Payload**: 2, 7, or 14 bytes raw Mode S / Mode A/C frame.
- **Escape Character Handling**: If byte `0x1A` appears anywhere in the timestamp, signal level, or payload, it is escaped by doubling it (`0x1A 0x1A`).

**Example Mode S Long Packet Breakdown (14 bytes data):**
```
[0x1A] [0x33] [TS0 TS1 TS2 TS3 TS4 TS5] [RSSI] [D0 D1 D2 D3 D4 D5 D6 D7 D8 D9 D10 D11 D12 D13]
```

### 2. AVR / Raw Hex Format (TCP Port 30002 / 30006)

ASCII-encoded hex strings terminated with a semicolon `;` and newline `\n`.

- **Un-timestamped Mode S**: `*8D4840D6202CC371C32CE0576098;\r\n` (`*` prefix + hex frame + `;`).
- **Timestamped Mode S**: `@000000A1B2C38D4840D6202CC371C32CE0576098;\r\n` (`@` prefix + 12 hex-digit clock timestamp + hex frame + `;`).
- **Mode A/C**: `:3102;\r\n` (`:` prefix + 4 hex digits + `;`).

### 3. BaseStation / SBS-1 CSV Format (TCP Port 30003)

Text-based comma-separated fields representing parsed flight telemetry rows.

**Header / Field Layout:**
```
MSG, [MessageType], [TransmissionType], [SessionID], [AircraftID], [HexIdent], [FlightID], [DateLog], [TimeLog], [DateMsg], [TimeMsg], [Callsign], [Altitude], [GroundSpeed], [Track], [Latitude], [Longitude], [VerticalRate], [Squawk], [Alert], [Emergency], [SPI], [IsOnGround]
```

**Common Message Types:**
- `MSG,1`: ES Aircraft Identification (`Callsign`)
- `MSG,2`: ES Surface Position (`Altitude`, `Speed`, `Track`, `Lat`, `Lon`, `Ground`)
- `MSG,3`: ES Airborne Position (`Altitude`, `Lat`, `Lon`, `Alert`, `Emergency`, `SPI`, `Ground`)
- `MSG,4`: ES Airborne Velocity (`Speed`, `Track`, `VerticalRate`)
- `MSG,5`: Surveillance Alt (`Altitude`, `Alert`, `SPI`, `Ground`)
- `MSG,6`: Surveillance ID (`Altitude`, `Squawk`, `Alert`, `SPI`, `Ground`)

**Example Port 30003 Output Row:**
```csv
MSG,3,1,1,4840D6,1,2026/09/11,12:00:00.000,2026/09/11,12:00:00.000,,35000,,,33.1041,-96.7083,,,0,0,0,0
```

### 4. Datalink Feeds (ACARS / HFDL / VDL2) — NDJSON Streams

Datalink aggregators like **Airframes.io** and **ACARS Router** consume raw or decoded JSON rows over UDP or TCP sockets.

**Standard Datalink JSON Payload Structure:**
```json
{
  "timestamp": 1726050000.123,
  "station_id": "KDFW-SDR-1",
  "channel": { "freq": 136800000, "rate": 21000 },
  "app": { "name": "AeroHub", "version": "1.0.0" },
  "acars": {
    "label": "20",
    "mode": "2",
    "flight": "AAL123",
    "tail": "N123AA",
    "text": "OFF 1201 / ON 1245 / ETA 1315 KDFW"
  }
}
```

---

## Multilateration (MLAT) Considerations

Multilateration determines position for non-ADS-B aircraft (Mode S short/long surveillance replies and Mode A/C) by measuring the **Time Difference of Arrival (TDOA)** across three or more synchronized receiver stations.

**Feeder Requirements for MLAT:**
1. **Precise Timestamps**: The SDR receiver must supply nanosecond-accurate timestamps. Hardware with GPSDO (e.g. BladeRF, USRP) or SDRs running sample-counter clock ticks ($12\text{ MHz}$ Beast clock) are required.
2. **Server-Side MLAT Helper (`mlat-client`)**: Feeders run an `mlat-client` daemon that connects to local Port 30005 (Beast stream) to pull raw timestamped frames, submits them to the network MLAT server, and receives calculated MLAT aircraft position updates on Port 30105 (Beast return) or Port 30106 (BaseStation return).
3. **Station Location**: Feeders must configure exact antenna coordinates (Latitude, Longitude, Altitude AMSL) with high accuracy ($\le 10\text{ m}$).

---

## Feeder Architecture Patterns in C# / .NET (AeroHub)

When adding data feeding or feed proxying capabilities to an application like AeroHub:

### 1. Dual-Role Network Listener / Client
Expose standard server ports so feeder daemons can connect locally:
- `Port 30005` (TCP Server): Streams Beast binary frames to local `piaware`, `fr24feed`, or `rbfeeder`.
- `Port 30002` (TCP Server): Streams raw AVR hex strings.
- `Port 30003` (TCP Server): Streams BaseStation CSV lines.

### 2. Outbound Push Client Pattern
For services accepting direct outbound push connections (e.g. custom servers, Airframes UDP/TCP):
- Use non-blocking `SocketAsyncEventArgs` or `System.IO.Pipelines`.
- Maintain a **bounded channel / queue** (`Channel<T>`) between the decoding thread and the network sender.
- **Drop Policy**: Drop oldest raw frames if the network client falls behind (never block the RF decoder pipeline).

### 3. Reconnection & Resiliency
- Implement exponential backoff ($1\text{s} \rightarrow 2\text{s} \rightarrow 5\text{s} \rightarrow 15\text{s} \rightarrow \text{max } 60\text{s}$).
- Monitor connection health metrics: `BytesSent`, `FramesSent`, `FramesDropped`, `LastAckAtUtc`.

---

## Testing & Verification Strategy

- **Beast Framing Tests**: Synthesize Mode S short/long frames containing `0x1A` bytes and verify `0x1A 0x1A` escape insertion and $12\text{ MHz}$ timestamp packing.
- **BaseStation CSV Format Tests**: Verify `MSG,3` and `MSG,1` formatting against reference SBS-1 parsers.
- **Socket Backpressure Tests**: Connect a slow/throttled TCP client, flood 10,000 frames/sec, and verify that the feeder queue drops frames cleanly without exceeding memory bounds.
