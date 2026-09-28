---
description: "Scaffold a brand-new WPF signal-analysis SDR app using the SmartSDR layered architecture pattern (shell, root view model, channel manager, spectrum display, decoder modules)."
name: "New SDR Analysis App"
argument-hint: "App name and any specific decoders/analysis features to include"
agent: "agent"
---

# New SDR / Signal-Analysis App Scaffold

Scaffold a new WPF desktop application named `${input:appName:MySdrApp}` that follows the
architecture described in [smart-sdr-architecture](../skills/smart-sdr-architecture/SKILL.md).
Ask the user for the app name, target .NET version, and which signal-analysis features
(decoders/demodulators/classifiers) to include if not already specified, before scaffolding.

## What to build

Create a solution with this project layout, mirroring the layering in this repo:

1. **`${input:appName}`** (WPF executable project)
   - `App.xaml` / `App.xaml.cs` — bootstraps settings, parses command-line args, enforces
     single-instance if requested.
   - `MainWindow.xaml` / `.cs` — app shell: docked panels, tool windows, menu/header area.
   - `Views/` and `UserControls/` — XAML views bound to view models, no business logic.

2. **`${input:appName}.ViewModels`** (class library)
   - `MainViewModel` — the single root state container: active channel, active display,
     selected mode, app-wide settings, references to the managers below.
   - `ChannelManager` / `ChannelViewModel` — creates/owns per-channel (per-slice) state:
     frequency, mode, filter, gain, live metrics. One `ChannelViewModel` per active channel.
   - `SpectrumDisplayManager` / `SpectrumDisplayViewModel` — owns live spectrum/waterfall
     display objects, one per channel or per view, subscribes to raw spectrum data events.
   - `DecoderManager` — owns signal decoder/demodulator/classifier view models, keyed by
     channel or global scope depending on the feature.

3. **`${input:appName}.UiFramework`** (class library, WPF-dependent, mirrors `Flex.UiWpfFramework`)
   - `ObservableObject`, `RelayCommand`, `DialogService`, safe/sorted observable collections,
     shared value converters. No app-specific logic — this is pure MVVM plumbing.

4. **`${input:appName}.Core`** (class library, NO WPF reference, mirrors `FlexLib`)
   - Radio/hardware/protocol connection logic.
   - Raw spectrum/IQ data provider that raises events consumed by `SpectrumDisplayManager`.
   - DSP building blocks (FFT, filtering) used by decoder/classifier modules.
   - Must not reference `PresentationFramework`/`PresentationCore`/WPF — enforce this via
     project reference direction only (Core has no reference to the WPF projects).

5. **Signal-analysis feature modules** (one folder/namespace per feature, e.g. `Decoders/Rtty`,
   `Decoders/Cw`, `Analysis/Classifier`), each with:
   - a model/service class in `${input:appName}.Core` performing the actual signal processing
   - a view model in `${input:appName}.ViewModels` exposing state/commands
   - a view/control in `${input:appName}` bound to that view model
   - a registration line in `DecoderManager` (or the owning manager)
   - follow the `signal-decoding-analysis` skill for the `IDecoderModem`/`IBatchDecoderModem`
     shape, mode characteristics, and AFC/squelch/confidence conventions

6. **`${input:appName}.Core.Tests`** (test project, references `${input:appName}.Core` only)
   - a `Fixtures/<ModeName>/` folder per decoder holding a captured or faithfully synthesized
     recording plus its known-correct decoded output (the "golden recording" pattern)
   - one test per decoder asserting the golden recording decodes correctly, plus unit tests
     for individual DSP primitives (filter, resampler, sync tracker) in isolation
   - see the `signal-decoding-analysis` skill's Testing strategy section for the full rationale

## UI conventions to apply

- Dark theme, high-contrast status colors.
- Dense, information-rich panels with strong grouping/borders.
- The live spectrum/waterfall display is the dominant visual element.
- Show signal metrics inline with the channel/slice, not in a separate disconnected panel.

## Steps

1. Confirm app name, .NET target framework, and requested decoder/analysis features with the user
   if not provided.
2. Create the solution and the five projects above with correct project references
   (`${input:appName}` → `ViewModels` + `UiFramework`; `ViewModels` → `Core` + `UiFramework`;
   `Core` has no WPF reference; `Core.Tests` → `Core` only).
3. Add `MainViewModel`, `ChannelManager`/`ChannelViewModel`, `SpectrumDisplayManager`/
   `SpectrumDisplayViewModel`, and `DecoderManager` as empty-but-wired classes with the
   properties/events described above.
4. Wire `App` → `MainWindow` → `MainViewModel` so the shell boots into a visible window with
   a placeholder spectrum display and channel list bound to the view models.
5. Scaffold one requested decoder/analysis feature end-to-end (model + view model + view +
   manager registration) as a working example for the pattern, plus a `Core.Tests` project
   with at least one golden-recording test fixture for that decoder.
6. Build the solution and fix any compile errors.
7. Apply [sdr-generated-app.instructions.md](../instructions/sdr-generated-app.instructions.md)
   conventions to all new code going forward.

Do not deviate from the layering above: no business/state logic in code-behind, no WPF
references in the Core project, and one manager per dynamic collection of feature objects.
