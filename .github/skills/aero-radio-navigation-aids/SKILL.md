---
name: aero-radio-navigation-aids
description: "Use when receiving, demodulating, or processing aeronautical radio navigation aids (VOR, DVOR, ILS Localizer/Glideslope, Marker Beacons, NDB, DME, TACAN): VHF/UHF/LF frequency bands, AM/FM subcarrier phase comparison, 90/150 Hz DDM calculation, Morse ID decoding, slant-range timing, and normalized navaid signal metrics."
---

# Aeronautical Radio Navigation Aids (Navaids)

## Purpose

Aeronautical Radio Navigation Aids (Navaids) are ground-based radio transmitters used by aircraft
for en-route navigation, approach guidance, and position determination. An SDR application
receives, channelizes, demodulates, and measures signal parameters from these stations:

- **VOR / DVOR**: Very High Frequency Omnidirectional Range (magnetic bearing radial)
- **ILS Localizer & Glideslope**: Instrument Landing System (lateral and vertical guidance)
- **ILS Marker Beacons**: Fixed-position distance checkpoints along an ILS approach
- **NDB**: Non-Directional Beacon (automatic direction finding / LF-MF bearing)
- **DME / TACAN**: Distance Measuring Equipment & Tactical Air Navigation (L-band slant range & bearing)

Recommended processing boundary:

`IQ samples -> Channelizer / Bandpass Filter -> AM/FM Demodulator -> Subcarrier / Tone Extractors -> Navaid DSP Engine (Phase / DDM / Pulse Timing / Morse Decoder) -> Normalized Navaid Metric Record -> UI / Spectrum / Map Overlay`

Keep signal processing left of "Normalized Navaid Metric Record" inside a dedicated core/DSP module with no UI dependency, matching the decoder boundaries across this codebase.

---

## Navaid Systems & Frequency Specifications

| System | Frequency Range | Channel Spacing / Allocation | Modulation / Signals | Primary Parameter |
|---|---|---|---|---|
| **VOR / DVOR** | 108.00–117.95 MHz | 50 kHz (even 100s kHz tenths in 108–112 range) | AM 30 Hz ref phase, 9.96 kHz subcarrier FM-modulated by 30 Hz var phase, 1020 Hz Morse ID AM | Magnetic Radial (0°–359°) |
| **ILS Localizer** | 108.10–111.95 MHz | 50 kHz (odd 100s kHz tenths, e.g. 109.1, 109.3) | AM 90 Hz & 150 Hz tones, 1020 Hz Morse ID | Difference in Depth of Modulation (DDM) for Left/Right Guidance |
| **ILS Glideslope** | 328.60–335.40 MHz | 150 kHz (paired automatically with LOC freq) | AM 90 Hz & 150 Hz tones | DDM for Above/Below Glidepath Guidance |
| **ILS Marker Beacons** | 75.00 MHz | Fixed single frequency | AM 400 Hz (OM), 1300 Hz (MM), 3000 Hz (IM) tones | Marker Type & Keying Pattern |
| **NDB** | 190.00–535.00 kHz | 1 kHz | AM 400 Hz or 1020 Hz Morse ID tone (A9W or A2A) | Relative Bearing via ADF / Signal Strength |
| **DME / TACAN** | 960.00–1215.00 MHz | 1 MHz (X & Y channels, paired with VOR/ILS) | Pulse-pair transponder (12 µs or 30 µs spacing) | Slant-range time delay (12.35 µs / NM) |

---

## Signal Processing & Demodulation Details

### 1. VOR / DVOR Radial Phase Comparison

A VOR station broadcasts two 30 Hz signals:
- **Reference Signal**: 30 Hz AM (conventional VOR) or 30 Hz FM on a 9.96 kHz subcarrier (Doppler VOR).
- **Variable Signal**: 30 Hz FM on a 9.96 kHz subcarrier (conventional VOR) or 30 Hz AM (Doppler VOR).

**DSP Procedure:**
1. AM-demodulate the VHF RF channel to extract baseband audio ($0\text{--}12\text{ kHz}$).
2. Pass baseband audio through a 30 Hz bandpass filter to isolate the 30 Hz AM component.
3. Pass baseband audio through a 9.96 kHz bandpass filter, then FM-demodulate the subcarrier to extract the second 30 Hz component.
4. Calculate the phase difference $\Delta \phi$ between the reference 30 Hz and variable 30 Hz signals:
   $$\text{Radial (Degrees)} = (\arg(S_{\text{var}}) - \arg(S_{\text{ref}})) \pmod{360^\circ}$$
5. Apply magnetic variation offset if configured.

### 2. ILS Localizer & Glideslope Guidance (DDM)

ILS guidance is based on the **Difference in Depth of Modulation (DDM)** of two audio tones:
- **90 Hz tone**: Indicates left of runway centerline (Localizer) or above glidepath (Glideslope).
- **150 Hz tone**: Indicates right of runway centerline (Localizer) or below glidepath (Glideslope).

**DSP Procedure:**
1. AM-demodulate the LOC (VHF) or GS (UHF) carrier.
2. Filter the audio through narrow 90 Hz and 150 Hz bandpass / Goertzel filters.
3. Measure the modulation depth $M_{90}$ and $M_{150}$:
   $$M_{f} = \frac{2 \cdot |A_{f}|}{A_{\text{carrier}}}$$
4. Calculate Difference in Depth of Modulation ($\text{DDM}$) and Sum in Depth of Modulation ($\text{SDM}$):
   $$\text{DDM} = M_{90} - M_{150}$$
   $$\text{SDM} = M_{90} + M_{150}$$
5. Centerline / On-Glidepath occurs when $\text{DDM} = 0.0$.
   - **Localizer Full Scale Deflection**: $\text{DDM} = \pm 0.155$ (approx $\pm 2.5^\circ$).
   - **Glideslope Full Scale Deflection**: $\text{DDM} = \pm 0.175$ (approx $\pm 0.7^\circ$).

### 3. Station Identification Decoding (Morse Code)

VOR, ILS Localizer, and NDB stations transmit 2–4 letter Morse code identifiers via AM tone keying:
- **VOR / ILS**: 1020 Hz tone, 7–10 WPM.
- **NDB**: 400 Hz or 1020 Hz tone.

**DSP Procedure:**
1. Isolate the ID tone using a bandpass filter (1020 Hz or 400 Hz, $\pm 50\text{ Hz}$).
2. Compute Envelope / Energy via moving RMS or Goertzel magnitude.
3. Apply adaptive thresholding to detect Mark (tone present) and Space (tone absent).
4. Measure Dit ($1t$), Dah ($3t$), Element Space ($1t$), Character Space ($3t$), and Word Space ($7t$) durations.
5. Decode Morse elements into station callsign characters (e.g. `DFW`, `I-DAL`).

### 4. DME Slant Range Pulse Pair Detection

DME operates on L-band pulse pairs:
- **Interrogation (Aircraft)** $\rightarrow$ **Reply (Ground Station)**.
- **Pulse Pair Spacing**:
  - **X Channel**: $12\ \mu\text{s}$ spacing (interrogation & reply).
  - **Y Channel**: $36\ \mu\text{s}$ interrogation / $30\ \mu\text{s}$ reply spacing.

**DSP Procedure:**
1. Pulse envelope detection on L-band receiver stream.
2. Match filter / cross-correlation against standard Gaussian pulse pair template ($3.5\ \mu\text{s}$ pulse width).
3. Measure round-trip time delay $T_{\text{delay}}$ (minus fixed station delay $50.0\ \mu\text{s}$):
   $$\text{Slant Distance (NM)} = \frac{T_{\text{delay}} - 50.0\ \mu\text{s}}{12.359\ \mu\text{s/NM}}$$

---

## Normalized Navaid Metric Record Model

A normalized Navaid record should contain:

```csharp
public sealed record NavaidMeasurementSnapshot(
    string StationIdentifier,          // e.g. "CVE", "I-DAL"
    NavaidType Type,                    // Vor, Dvor, IlsLocalizer, IlsGlideslope, MarkerBeacon, Ndb, Dme
    DateTimeOffset RecordedAtUtc,
    double FrequencyMHz,
    double? MeasuredRadialDegrees,      // For VOR/DVOR (0-359°)
    double? DifferenceInDepthModulation,// For ILS (DDM = M90 - M150)
    double? SumInDepthModulation,       // For ILS (SDM = M90 + M150)
    double? DistanceNauticalMiles,      // For DME/TACAN slant range
    double? SignalPowerDbm,             // Received signal strength
    double? SnrDb,                      // Signal-to-noise ratio
    string? DecodedMorseCallsign,       // Morse ID string decoded live
    bool IsIdentValid,                  // True if Morse ID matches station database
    bool IsFlaggedWarning,              // Warning if SDM out of range or carrier unstable
    IngestionProvenance Provenance
);
```

---

## Architectural Integration in SDR Apps

When integrating Navaid processing into a SmartSDR-style WPF / ASP.NET Core SDR application:

1. **Slice / VFO Allocation**:
   - Tune a narrow RF slice to the published Navaid channel (e.g., $114.60\text{ MHz}$ for VOR).
   - Sample rate: $48\text{ kS/s}$ baseband audio is sufficient for VOR/ILS/NDB; DME requires $\ge 2\text{ MS/s}$ L-band IQ.
2. **Multi-Channel VOR/ILS Monitoring**:
   - One wideband SDR stream ($2.048\text{ MS/s}$ centered at $110.0\text{ MHz}$) can simultaneously extract and decode every ILS Localizer and VOR station in the $108\text{--}112\text{ MHz}$ band.
3. **UI / Display Conventions**:
   - Render HSI / CDI (Horizontal Situation Indicator / Course Deviation Indicator) needle graphics for VOR radials and ILS localizer/glideslope DDM.
   - Display raw audio spectrum and audio waterfall for the 30 Hz, 90 Hz, 150 Hz, and 1020 Hz subcarrier tones.

---

## Testing & Validation Strategy

Tests for Navaid modules should include:

- **VOR Test Vectors**: Synthesized IQ with known phase offsets ($0^\circ, 45^\circ, 90^\circ, 180^\circ, 270^\circ$) verifying radial decoding to within $\le 0.5^\circ$.
- **ILS DDM Test Vectors**:
  - $M_{90} = 0.20, M_{150} = 0.20 \rightarrow \text{DDM} = 0.00$ (On centerline).
  - $M_{90} = 0.35, M_{150} = 0.20 \rightarrow \text{DDM} = +0.15$ (Fly Right / Left of centerline).
  - $M_{90} = 0.20, M_{150} = 0.35 \rightarrow \text{DDM} = -0.15$ (Fly Left / Right of centerline).
- **Morse ID Keying**: Synthesized 1020 Hz audio bursts encoding `DAL` (`-.. .- .-..`) and verifying character extraction.
- **DME Timing**: Piped pulse pairs with simulated delays ($123.59\ \mu\text{s} + 50.0\ \mu\text{s}$) verifying $10.0\text{ NM}$ slant range measurement.
