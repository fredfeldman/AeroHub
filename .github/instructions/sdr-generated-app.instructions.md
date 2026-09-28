---
description: "Use when writing or reviewing code inside a new SDR/signal-analysis WPF app that was scaffolded from the smart-sdr-architecture pattern. Covers layering rules, naming, and where new state/UI/DSP code belongs."
applyTo: "**/*.cs,**/*.xaml"
---

# Generated SDR App — Companion Instructions

This project was scaffolded from the `smart-sdr-architecture` skill. It follows the same
layered pattern as SmartSDR: shell → app state → managers → view models → views → low-level logic.
Keep all new code consistent with that layering. For anything involving a signal decoder,
demodulator, or classifier, also consult the `signal-decoding-analysis` skill — it defines
the `IDecoderModem`/`IBatchDecoderModem` shapes, mode catalog, and testing strategy referenced
below.

## Layer map (where code goes)

| Concern | Layer / folder | Notes |
|---|---|---|
| App bootstrap, command-line args, singleton enforcement | `App` | no business logic here |
| Main shell, docked panels, tool windows | `MainWindow` / `Views`, `UserControls` | XAML + code-behind only for layout, no state |
| Root app state (active slice, active radio, selected mode) | `MainViewModel` | single root state container, one instance |
| Per-channel / per-signal state | `SliceManager` / `SliceViewModel` (or your app's channel equivalent) | one view model per receiver/channel/analysis stream |
| Live spectrum / waterfall display state | `SpectrumDisplayManager` / `SpectrumDisplayViewModel` | mirrors `PanafallManager` / `PanafallViewModel` |
| Signal decoders, demodulators, classifiers | `DecoderManager` + one view model per decoder | keep DSP out of view models |
| Protocol, hardware, DSP, radio-connection logic | a non-WPF library project (mirrors `FlexLib`) | must not reference WPF assemblies |
| Reusable MVVM plumbing (`ObservableObject`, `RelayCommand`, converters) | a shared UI framework project (mirrors `Flex.UiWpfFramework`) | no app-specific logic here |

## Rules

1. **State lives in view models, not controls.** Never store tuning/frequency/mode/signal state as
   fields on a `UserControl` or code-behind class. Bind to a view model property instead.
2. **Managers own lifecycle, not individual features.** When a new dynamic collection of
   objects is needed (new decoder type, new display type, new channel type), add a manager
   class responsible for creating/destroying/tracking that collection — don't scatter
   `new SomethingViewModel()` calls across the UI layer.
3. **DSP/protocol code must not depend on WPF.** Any class that talks to hardware, parses
   protocol frames, or runs a DSP algorithm belongs in a plain class library, not the WPF app
   project. It communicates upward through events/interfaces that a view model adapts.
4. **One feature = one module.** A new signal-analysis feature must include (at minimum):
   a model/service for the raw data, a view model exposing state/commands, a view/control
   bound to it, and a registration point in the owning manager or the app shell. Do not
   half-implement a feature by only adding a view.
5. **Root state stays singular.** There is exactly one root state object (the `MainViewModel`
   equivalent). Do not create a second global state holder — extend the existing one or add
   a manager it owns.
6. **Persist only user preferences.** Save settings (layout, selected options, thresholds)
   through the app's settings/persistence layer. Never persist transient signal data or
   live measurement values.
7. **Follow existing naming conventions.** Use `<Feature>Manager`, `<Feature>ViewModel`,
   `<Feature>View` suffixes to match the pattern already used in this codebase.
8. **Real-time data flows one direction.** Model/service raises events → manager or view model
   subscribes and adapts → WPF view renders. Do not let a WPF control reach back into the
   DSP/protocol layer directly.
9. **Decoders implement `IDecoderModem` (streaming) or `IBatchDecoderModem` (windowed).**
   Pick the shape based on the mode (see `signal-decoding-analysis`'s mode catalog); expose
   AFC frequency and squelch/confidence as modem state/events, never computed in the UI layer.
10. **`DecoderManager` arbitrates exclusive hardware.** If a decoder needs sole ownership of a
    physical resource (a second SDR device, an external decoder process), `DecoderManager`
    (or a resource arbiter it owns) must stop the previous owner before starting the next —
    never let two decoder instances assume they can both claim the same exclusive resource.
11. **A new decoder is not done without a golden-recording test.** Add a captured or
    faithfully synthesized recording of the target mode as a test fixture and assert against
    a known-correct decode, plus unit tests for individual DSP primitives (filter, resampler,
    sync tracker) in isolation. See `signal-decoding-analysis`'s Testing strategy section.

## Before adding a feature, answer

1. Is this app-wide state, per-channel state, or display-only state?
2. Does it belong in the model/service layer, the view model layer, or the view layer?
3. Does it need to react to live data updates, or is it static/one-time UI?
4. Does it need a new manager, or does it belong under an existing one?

If unclear, default to: root state in the root view model, per-channel features under the
channel manager/view model, spectrum-related features under the display manager/view model,
UI surfaces under `Views`/`UserControls`, and low-level logic in the non-WPF library project.
