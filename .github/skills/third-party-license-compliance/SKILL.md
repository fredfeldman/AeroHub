---
name: third-party-license-compliance
description: "Use when porting, adapting, or closely following code from a third-party or open-source reference project (fldigi, isobar, multimon-ng, DSD, WSJT-X, or any other external repo) into this codebase. Covers the difference between reusing architectural knowledge vs. copying implementation, and what to check before either. Grounded in the actual licenses of the repos signal-decoding-analysis references."
---

# Third-Party License Compliance

## Purpose

`signal-decoding-analysis` deliberately studies several external decoder projects
(`fldigi`, `isobar`, `psk31`, `hampi-dashboard`, plus described-from-knowledge WSJT-X,
multimon-ng, DSD) for architecture and mode characteristics. That's legitimate — reading
and learning from how something works isn't a licensing event. **Copying or closely
translating their actual source code into this codebase is a different, separate action
with real license obligations**, and this skill exists to keep those two things from being
conflated by whoever implements a decoder next.

Use this skill whenever you are about to:

- copy, port, or closely translate a function/algorithm from an external repo into this
  codebase (not just use it as a description of *how* something works)
- add a dependency on an external library/binary that itself has a license (e.g. calling
  out to an external tool, per `signal-decoding-analysis`'s "wrap an external decoder
  process" pattern)
- decide whether a new decoder module can be written by "reading the reference
  implementation side-by-side" vs. must be written from a specification only

## Verified licenses of the repos `signal-decoding-analysis` references

Checked directly in the local copies at `D:\source\repos\Decoders` — do not assume, verify
against the actual `LICENSE`/`COPYING` file in a repo before relying on any of this:

| Repo | License (verified) | What that means for this codebase |
|---|---|---|
| `dave-w1hkj/fldigi-fldigi` | GPLv3 (`COPYING`) | Copying/porting fldigi source code into this codebase would obligate this codebase (or at least the derived portion) to be GPLv3-compatible on distribution. **Architecture/mode-characteristics knowledge only, no code copying.** |
| `isobar` | GPLv3 (`LICENSE`), despite being an independent clean-room reimplementation (see its `README.md`/`NOTICE`) | Same obligation as above applies to isobar's *code*, even though isobar itself was written without copying from its own reference (KG-FAX) — GPL applies to isobar's own source regardless of how isobar itself was produced. Its *documented method* (see below) is the reusable asset, not its code. |
| `hampi-dashboard` | MIT (per its README badge) | Permissive; short attribution notice needed if code is actually copied, but copying is still not the intended use here (its dashboard-architecture pattern is what `signal-decoding-analysis` references, not its Python source). |
| `psk31` | **No LICENSE file present** | Default copyright rules apply: all rights reserved by the author, no license grant exists at all. This is the *most* restrictive case — do not copy this code into this project without contacting the author for permission, regardless of how small the file is. |

Treat this table as a starting point, not a permanent record — if any of these repos are
updated locally or a different repo is added as a reference, re-check its license file
before assuming the same terms still apply.

## The rule: architecture knowledge vs. code copying

- **Always fine:** describing *how* a mode/algorithm works in your own words, reproducing
  publicly documented facts (bandwidths, baud rates, protocol structure — these are facts,
  not anyone's copyrightable expression), and writing your own implementation from that
  understanding.
- **Requires attention:** copying a function body, a specific numeric filter design, a
  particular code structure, or closely paraphrasing (variable-for-variable, line-for-line)
  a GPL or unlicensed source's implementation. If it would be recognizable as "the same code"
  to the original author, it's a copy, not an independent implementation.
- **isobar's own model is the template to follow** when you want the *behavior* of a
  GPL/reference tool without its licensing obligations: it explicitly states it was
  "written from a functional specification produced by reverse engineering," with no code,
  text, or graphical assets copied from the original — and it explicitly invokes the merger
  doctrine (a public standard has only a small number of natural implementations, so
  convergence on the same approach isn't evidence of copying). If a new decoder module needs
  to replicate a GPL reference's *behavior* precisely, write a functional spec first
  (describe inputs/outputs/algorithm steps in your own words) and implement from that spec
  without the reference source open side-by-side, exactly like isobar's documented process.

## Practical guidance for this codebase

1. Before starting a new decoder/DSP module "based on" one of the referenced repos, decide
   up front: is this an independent implementation from a spec (no code-copying concern), or
   is it intended to closely follow/port the reference's actual code (real concern)? Don't
   let this decision happen implicitly mid-implementation.
2. If closely porting is genuinely necessary (e.g. a complex FEC/vocoder algorithm too
   error-prone to reimplement from scratch), stop and raise it explicitly rather than
   silently including GPL-derived code in a codebase whose own license terms may not be
   GPL-compatible — this is a licensing decision for the project owner, not something to
   resolve unilaterally while implementing a feature.
3. When wrapping an external tool/process instead of reimplementing (the hampi-dashboard
   pattern from `signal-decoding-analysis` — SDRTrunk, `dsd-fme`, SatDump, `direwolf`),
   check that specific tool's own license/distribution terms too; wrapping a GPL binary as a
   separate process you invoke has very different obligations than statically linking or
   copying its source, but redistributing that binary alongside this app is still a
   distribution decision that needs the same license check.
4. Never copy code from a repo with no visible `LICENSE`/`COPYING` file (like `psk31` here)
   on the assumption that "it's small" or "it's obviously how everyone does it" makes it
   safe — no license file means no grant exists at all.
5. When in doubt about whether a piece of code in this project was influenced too closely by
   a specific reference implementation, err toward: rewrite from the functional description
   in `signal-decoding-analysis`'s mode catalog rather than the original source, or ask
   before merging.

## Quick checklist

- identified whether a new module is "written from a spec" or "closely porting a reference"
  *before* implementation, not after
- checked the actual `LICENSE`/`COPYING` file of any repo being used as a reference, not
  assumed from reputation or repo popularity
- treated protocol/standard facts (bandwidth, baud rate, framing) as free to reuse, and
  actual code/algorithm expression as the thing requiring a license check
- flagged any case where close porting of GPL or unlicensed code seems genuinely necessary,
  rather than resolving it silently
- applied the same check to any external tool/process being wrapped, not just to code being
  copied directly into this repo
