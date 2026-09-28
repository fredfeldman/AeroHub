---
name: wpf-realtime-rendering
description: "Use when implementing or optimizing a live spectrum/waterfall/scope display in a WPF SDR app — WriteableBitmap scrolling, dirty-rect updates, frame-rate throttling, and keeping the UI thread responsive under a high-rate data feed. Grounded in this repo's actual PanafallViewModel waterfall renderer."
---

# WPF Real-Time Rendering for Spectrum/Waterfall Displays

## Purpose

`smart-sdr-architecture` says the waterfall is "one model object, one view-model, one WPF
control, one manager" but doesn't cover *how* that view-model actually paints tens of frames
per second without stalling the UI thread. This skill fills that gap using the real
implementation already in this repo: `SmartSDR/SmartSDR.ViewModels/PanafallViewModel.cs`.

Use this skill when you need to:

- implement a scrolling waterfall, a live spectrum trace, or any other high-refresh-rate
  WPF visual fed by a background data stream
- decide between full-frame redraw and incremental/dirty-rect updates
- keep the Dispatcher from becoming a bottleneck when data arrives faster than the display
  can (or should) redraw
- diagnose "waterfall stutters / spectrum lags behind live audio" style problems

## Reference implementation: `PanafallViewModel`'s waterfall renderer

This is a real, shipping implementation, not a hypothetical pattern — read it directly
before implementing a new one.

- The waterfall is a `WriteableBitmap` (`PixelFormats.Pbgra32`) held on the view model
  (`WfallBitmap` / `_wfallBitmap`), not the view. The view binds an `Image.Source` to it;
  the view model owns all pixel manipulation.
- Two render modes exist, chosen based on whether the bitmap size changed and whether the
  scroll delta (`row_delta`) fits inside the existing bitmap:
  - **Full render** (`WaterfallFullWriteToDisplay`): allocates a fresh `WriteableBitmap` and
    writes the whole frame with one `WritePixels` call. Used on resize or when the scroll
    delta exceeds the bitmap height (nothing to reuse).
  - **Incremental scroll**: `CopyPixels` shifts the existing rows by `row_delta` (scrolling
    the whole waterfall up or down by however many new spectrum rows arrived), then
    `WritePixels` draws only the newly-exposed rows into an `Int32Rect` band. This means a
    full-height buffer is **never** repainted per frame — only the height of new data.
- Every pixel write is bracketed by `_wfallBitmap.Lock()` / `.Unlock()` (WPF's back-buffer
  locking for `WriteableBitmap`), and happens inside
  `Application.Current.Dispatcher.Invoke(DispatcherPriority.Normal, ...)` — i.e. explicitly
  marshaled onto the UI thread (see the threading model in `smart-sdr-architecture`), but
  only for the pixel-buffer mutation itself, not for the upstream data processing that
  produced the row.
- Failures during `WritePixels`/`Lock` are caught and swallowed rather than crashing the
  render loop — a dropped/corrupt frame should never take down the whole display.

## Design principles to carry into a new app

1. **Never repaint more than changed.** Model the display buffer as "mostly stable pixels
   plus a small dirty region" (`Int32Rect`) rather than redrawing the whole surface every
   tick. This is the single biggest cost lever for a waterfall/spectrum display — a full
   1920×1000 Pbgra32 redraw at 20+ fps is enormous compared to writing a handful of new rows.
2. **Scroll by shifting pixels, not by reallocating.** `CopyPixels` (read the current buffer)
   followed by `WritePixels` (write the shifted result back, then the new rows) avoids
   allocating a new `WriteableBitmap` every frame. Only allocate a new bitmap when the
   control's pixel dimensions actually change (resize) or the incoming delta is larger than
   the whole buffer (can't reuse anything anyway).
3. **The view model owns the bitmap; the view only binds to it.** Keeps rendering logic
   testable and keeps the view a thin XAML layer, consistent with `smart-sdr-architecture`'s
   "UI is view-model driven" principle — just extended to pixel buffers, not only bound
   properties.
4. **Marshal only the pixel-buffer mutation to the Dispatcher, not the upstream math.**
   Convert raw samples/dBm values into row-color bytes (`_fallColorDisplayData`-equivalent)
   off the UI thread; only the `Lock`/`WritePixels`/`Unlock` sequence itself needs to run on
   the Dispatcher. Doing color-mapping/FFT work inside the Dispatcher call turns the UI
   thread into the bottleneck for the whole receive pipeline.
5. **Decouple arrival rate from paint rate if they can diverge.** If the data source can
   produce rows faster than the display should reasonably redraw (e.g. capped at the
   monitor's refresh rate), coalesce multiple pending rows into one dirty-rect update rather
   than issuing one Dispatcher call per row. A `DispatcherTimer`-driven or
   `CompositionTarget.Rendering`-driven paint tick that drains a small queue of pending rows
   is a common way to do this without touching the async event-marshaling rule from
   `smart-sdr-architecture` (raw events must still be captured off-thread as soon as they
   arrive; only the *paint* can be throttled).
6. **Swallow, don't propagate, transient render failures.** A single bad frame (bitmap
   resized mid-write, disposed control, etc.) should log/ignore and continue, not crash the
   render loop or the app — mirror the try/catch-and-continue shape in the reference
   implementation.

## Applying this to a new decoder/analysis panel

Any new live-updating visual in a decoder or analysis feature module (constellation plot,
eye diagram, per-decoder confidence meter, spectrogram overlay) should follow the same shape:

- a `WriteableBitmap` (or equivalent low-level surface) owned by that feature's view model
- an explicit full-redraw path and an incremental-update path, chosen by what actually changed
- pixel-buffer mutation marshaled to the Dispatcher; data-to-pixel conversion done off-thread
- a coalescing/throttling point if the underlying decoder can produce updates faster than a
  useful redraw rate

## Quick implementation checklist

- buffer type matches the data (`Pbgra32` for color displays; consider `Gray8`/indexed
  formats for palette-based single-channel data like WEFAX video bytes)
- dirty-rect/scroll-shift update path implemented before assuming a full redraw is "good
  enough" — profile before committing to full-redraw-every-frame
- `Lock()`/`WritePixels`/`Unlock()` (or equivalent) wrapped in a try/catch that swallows
  transient failures without crashing the render loop
- Dispatcher marshaling scoped to the pixel-buffer mutation only, not the upstream DSP/color
  mapping
- a coalescing/throttle point exists if the data source can outpace a sane paint rate
