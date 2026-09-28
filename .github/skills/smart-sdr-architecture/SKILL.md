---
name: smart-sdr-architecture
description: "Use when building or extending a SmartSDR-style WPF desktop app, recreating the same look and feel, organizing a radio/spectrum UI, or adding signal decoders, analyzers, waterfall rendering, or real-time RF display features."
---

# SmartSDR Architecture and UI Pattern

## Purpose

This skill helps an agent understand how this codebase is structured and how to build a new application with the same feel and architecture: a WPF desktop app with a rich spectrum display, a slice-based tuner model, live RF visualization, and modular feature areas for decoders and signal analysis.

Use this skill when you need to:

- map the repo to a set of responsibilities
- recreate the app’s UI patterns and state flow
- add new signal-analysis tools without breaking the architecture
- extend the radio/spectrum display subsystem
- keep the look and feel consistent with this project

## What the repo is doing

This application is not a simple CRUD app. It is a real-time desktop UI built around:

- WPF and MVVM
- a root `MainViewModel` as the global state holder
- one or more radio objects and many slice objects
- a panadapter/waterfall rendering layer
- multiple feature panels and overlays for signal work
- a lower-level library layer for radio/control logic and a reusable WPF framework layer

## High-level architecture

### 1. App entry and boot

Look first at:

- `SmartSDR/SmartSDR.Applications/App.cs`
- `SmartSDR/SmartSDR/MainWindow.cs`
- `SmartSDR/SmartSDR.ViewModels/MainViewModel.cs`

The app boot flow is:

1. `App` starts and parses command-line options.
2. the app sets WPF rendering settings and singleton enforcement.
3. `MainWindow` creates the shell and associates major child windows.
4. `MainViewModel` becomes the root state container.
5. `SliceManager`, `PanafallManager`, and radio view models are initialized and connected.

This is the correct mental model: the app shell is a container, while the view models hold runtime state.

### 2. Root state model

The most important class for app behavior is `MainViewModel`.

It is the central coordinator for:

- selected radio
- selected slice or active channel
- global window state
- WAN / SmartLink concerns
- feature managers such as `SliceManager` and `PanafallManager`
- root UI state and shared settings

The pattern here is classic desktop MVVM: logic in the view model, UI surfaces in the view layer, and real radio state data in lower-level models/libraries.

### 3. Slice lifecycle

Look at:

- `SmartSDR/SmartSDR.ViewModels/SliceManager.cs`
- `SmartSDR/SmartSDR.ViewModels/SliceViewModel.cs`

A slice is the app’s conceptual “receiver/channel” object.

The lifecycle is roughly:

- radio emits a `SliceAdded` event
- `SliceManager` receives it
- it creates a `SliceViewModel`
- the active slice is assigned to `MainViewModel.ActiveSlice`
- the slice carries GUI-facing state such as frequency, mode, filter, gain, AGC, and meter values

This is the model to follow for any new channel-based feature such as:

- signal decoders
- demodulators
- filters
- frequency-based analysis panels
- per-slice overlays and readouts

### 4. RF display system

Look at:

- `SmartSDR/SmartSDR.ViewModels/PanafallManager.cs`
- `SmartSDR/SmartSDR.ViewModels/PanafallViewModel.cs`
- `SmartSDR/SmartSDR.UserControls/PanafallView.cs`

This is the core of the live RF UI.

The flow is:

- radio creates `Panadapter` and `Waterfall` objects
- `PanafallManager` subscribes to those events
- it creates `PanafallViewModel` objects
- `MainViewModel.ActiveSlice` determines the active display context
- `PanafallView` renders the spectrum and waterfall visuals
- drag and click interactions update tuning and frequency state

When a decoder is attached to a slice, its passband/AFC center frequency (see
`signal-decoding-analysis`) should be driven by the same click/drag-to-tune interaction
as the slice's main frequency — clicking the waterfall to select a signal should move both
the slice frequency (or a decoder-specific offset within the slice passband) and the
decoder's tuned frequency together, not just one of the two.

This is the design to emulate for any future SDR-like app:

- one model object for the live spectrum data
- one view-model object for display state
- one WPF control for painting and interaction
- a manager that knows how to create and bind the dynamic display set

For how `PanafallViewModel` actually paints the waterfall efficiently (WriteableBitmap
dirty-rect scrolling, Dispatcher-scoped pixel writes, full-redraw vs. incremental-scroll
choice) see the `wpf-realtime-rendering` skill — this section covers where the display
fits in the architecture, that skill covers how to make it perform.

### 5. Reusable UI framework

Look at:

- `Flex.UiWpfFramework/`
- `Flex.UiWpfFramework.Mvvm/`
- `Flex.UiWpfFramework.Utils/`

This layer provides the shared infrastructure for:

- `ObservableObject`
- `RelayCommand`
- `DialogService`
- safe observable collections
- common WPF conversions and helpers
- reusable UI data-binding plumbing

If you want a new app to feel like this one, use the same pattern: small reusable WPF foundation, then app-specific view models and controls on top.

Note: `FlexLib` itself references `Flex.UiWpfFramework.dll` (for `ObservableObject` and similar base types), so the separation in this repo is "no direct WPF/XAML dependency in FlexLib," not a hard assembly boundary. A new app that wants a stricter split should either duplicate a minimal `ObservableObject` into its non-WPF library or accept the same compromise.

### 6. Threading model for real-time data

Radio/DSP events (`SliceAdded`, `PanadapterAdded`, meter updates, audio buffers, etc.) are raised from background/network threads, not the UI thread. Every manager or view model that reacts to one of these events marshals back onto the UI thread before touching bound state or WPF objects, using `Application.Current.Dispatcher.BeginInvoke(...)` (see `PanafallManager.cs`, `RadioViewModel.cs`, `OpusStreamViewModel.cs`, `PanSpotManager.cs`).

When adding a new feature that subscribes to a model/service event:

- assume the event fires off the UI thread
- marshal to the Dispatcher before mutating any `ObservableObject` property or touching a WPF control
- do not assume `DispatcherTimer`-driven UI polling is optional insulation from this — it isn't a substitute for marshaling raw event callbacks

## Project roles and where to put code

### App shell and windows

Use these areas for overall navigation and shell behavior:

- `SmartSDR/SmartSDR/` for main shell and core windows
- `SmartSDR.Views/` and `SmartSDR.UserControls/` for actual UI surfaces

When adding a new feature that should feel native to the app, it usually belongs here if it is:

- a top-level window
- a docked panel
- a tool overlay
- a custom interaction surface

### Application state and orchestration

Use these areas for business rules and app coordination:

- `SmartSDR.ViewModels/`

Put state there when it needs to:

- be bound to UI
- react to radio or slice events
- coordinate display and feature interactions
- persist settings or selection state

### Lower-level behavior and radio logic

Use:

- `FlexLib/`
- `FlexControl/`

These are the parts that should be insulated from WPF concerns.

Add protocol, hardware, DSP, or radio-connection logic here rather than in the UI layer.

For the actual shape of a command/response protocol plus high-rate streaming channel to a
radio/hardware device, see the `radio-protocol-conventions` skill (grounded in `FlexLib`'s
real `Radio.cs`). For device audio I/O specifically, see `audio-streaming-pipeline`.

### Shared infrastructure

Use:

- `Flex.UiWpfFramework/`

This keeps the new app consistent with the existing framework patterns and avoids mixing app logic into arbitrary UI classes.

## Design principles from this repo

### 1. UI is view-model driven

The application does not usually scatter state into individual controls. Instead, the state is carried by view models and bound into XAML.

### 2. Managers handle lifecycle

The app uses managers to assemble dynamic model/view-model relationships:

- `SliceManager` owns slices
- `PanafallManager` owns panadapter/waterfall display objects
- `MainViewModel` owns the overall app lifecycle and active selection

This pattern scales well when you add new feature domains.

### 3. Real-time data is separated from presentation

The radio/stream/data layer is not directly coupled to the WPF visuals. A model emits events; a view model adapts them; the UI paints the result.

### 4. Settings are persisted in a predictable way

The project saves settings through the app settings layer (see `SmartSDR/PortableSettingsProviderSSDR.cs`) rather than hardcoding values. Follow that pattern when adding new UI options. See the `settings-persistence-pattern` skill for the concrete implementation to mirror (roaming vs. machine-scoped values, corrupt-file recovery).

### 5. Signals and decoding are feature modules, not ad hoc UI hacks

For signal decoders and analysis tools, create small feature modules with:

- a feature view model
- a view/control
- a model or DSP adapter
- a registration point in the app shell

## Recommended pattern for a new app with the same look and feel

If the goal is to create a new app with expanded features and the same visual language, use this structure:

### App shell

- root `MainWindow`
- docked panels and tool windows
- a header/menu area with strong task focus
- active slice/channel selection visible at all times

### Core state container

- `MainViewModel` singleton or root application state object
- properties for active slice, active radio, selected mode, active panel, app settings

### Spectrum display

- `SpectrumDisplayViewModel`
- `SpectrumDisplayView`
- same pattern as `PanafallViewModel` / `PanafallView`

### Slice model

- per-channel receivers or analysis streams
- per-slice frequency/mode/filter state
- per-slice validation and controls

### Signal analysis modules

Create modules for:

- decoder manager
- signal list and selection panel
- waterfall overlay and markers
- FFT or spectrum analysis panels
- demodulation and classification plugins
- audio or digital signal analysis tools

A strong pattern is:

- data provider layer
- analysis layer
- view-model adapter
- WPF display/control

### UI conventions

To preserve the visual style:

- use dark themes and high-contrast status colors
- favor dense, information-rich panes
- use strong control grouping and panel borders
- keep the live spectrum as the dominant visual element
- show signal metrics inline with the slice or display
- use a minimal but technical control aesthetic rather than a consumer app layout

For a starting point rather than reinventing the look from this description, adapt the existing resource dictionaries:

- `SmartSDR/resources/resourcedictionary.xaml`
- `SmartSDR/resources/resourcestyles.xaml`
- `SmartSDR/simple-20styles.xaml`

## Adding signal decoders and analysis tools

The repo already follows a pattern that is a good fit for decoder work. For the internal
design of a decoder itself (modem interface shape, mode catalog, AFC/squelch, batch vs.
streaming processing, testing strategy) see the `signal-decoding-analysis` skill — this
section only covers *where* that code plugs into the app shell described above.

### Good implementation shape

1. Create a feature model for the decoder backend.
2. Expose its state through a view model.
3. Attach the view model to the app shell or selected slice.
4. Add a visual panel for results and signal metadata.
5. Decide whether the decoder is per-slice or global.

### Examples of likely feature modules

- `DecoderManager`
- `SignalListViewModel`
- `SignalDetailsViewModel`
- `WaterfallOverlayManager`
- `SpectrumAnalysisViewModel`
- `AudioAnalyzerViewModel`
- `DemodulatorViewModel`
- `SignalClassifierViewModel`

### Keep responsibilities clean

- keep DSP or protocol logic in non-UI classes
- keep binding and state in view models
- keep rendering in WPF controls
- keep orchestration in managers or main app state

## How to think when extending the app

When you are asked to add a new feature, start by answering these questions:

1. Is this app-wide state, slice-specific state, or display-only state?
2. Does it belong to the radio model, the view model, or the WPF view?
3. Does it need to react to live data updates or just present static UI?
4. Is this a custom panel, overlay, or manager-driven feature?
5. Does it require a new manager, or should it belong under an existing one?

If the answer is not obvious, the correct default is this:

- root state stays in `MainViewModel`
- per-slice features go under `SliceViewModel` or `SliceManager`
- spectrum-related features go under `PanafallManager` / `PanafallViewModel`
- UI surfaces go under `Views` or `UserControls`
- low-level protocol or DSP work goes into `FlexLib` / `FlexControl`

## Quick implementation checklist

When creating a new feature in this style:

- define the type of data source
- create a model or service for the raw signal data
- create a view model to expose state and commands
- bind a WPF control to the view model
- attach it to the main UI shell or selected slice
- persist only user preferences, not transient signal state
- test the behavior through real update flow rather than mock-only binding assumptions

## Example target architecture for a new SDR analysis app

A future app based on this design should look like:

- `App` bootstraps app shell and settings
- `MainWindow` owns layout and feature windows
- `MainViewModel` holds active slice, active display, selected signal, and feature state
- `SliceManager` creates per-channel analysis state
- `SpectrumDisplayManager` creates real-time display objects
- `DecoderManager` owns signal decoders and analysis plugins
- `SignalResultsView` shows decoded results and metadata
- `WaterfallPanelView` renders the live RF display
- `FlexLib` handles communication and radio data before the UI sees it

This mirrors the structure already present in this repo while leaving room for more analysis features.

## Final rule

The app’s pattern is not “one giant view.” It is a layered architecture:

- shell
- app state
- managers
- view models
- views
- lower-level logic

If you want the new app to feel like this one, preserve that layering and let the real-time display and channel state remain the center of the system.
