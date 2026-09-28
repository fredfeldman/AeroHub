---
name: settings-persistence-pattern
description: "Use when implementing application settings persistence in a WPF app — a custom SettingsProvider, roaming vs. machine-scoped settings, and corrupt-file recovery. Grounded in this repo's actual PortableSettingsProviderSSDR implementation."
---

# Settings Persistence Pattern

## Purpose

`smart-sdr-architecture` says "settings are persisted in a predictable way" and points at
`PortableSettingsProviderSSDR.cs` but doesn't unpack what that class actually does. This
skill covers the concrete pattern, grounded in that real implementation, for anyone building
a new app's settings layer rather than reaching for the .NET default (which stores settings
under a version-specific path that changes on every app version bump and doesn't survive
side-by-side installs well).

Use this skill when you need to:

- implement application settings persistence for a new WPF app in this family
- decide between roaming (user-wide) and machine-specific settings for a given value
- make settings storage resilient to a corrupted or partially-written settings file
- avoid the default .NET `ApplicationSettingsBase` version-path problem

## Reference implementation: `PortableSettingsProviderSSDR`

- **A custom `SettingsProvider`**, not the default one. `ApplicationName` derives from
  `Application.ProductName` (falling back to the exe's own filename without the extension),
  and `GetAppSettingsPath()`/`GetAppSettingsFilename()` are overridable virtual methods
  returning `%AppData%\FlexRadio Systems\` and `SSDR.settings` — a fixed, predictable path
  that doesn't change per assembly version, unlike the .NET default provider.
- **Plain XML on disk**, loaded lazily into an in-memory `XmlDocument` on first access and
  written back out on `SetPropertyValues`. Simple enough to hand-edit or diff in support
  scenarios, which matters for a ham-radio-style app where users/support routinely need to
  inspect or manually fix a settings file.
- **Automatic backup-and-restore around every load.** On successful load, the settings file
  is immediately copied to a `.backup` sibling. If loading fails (corrupt/partial write —
  e.g. app crashed mid-save), it falls back to copying the `.backup` file over the primary
  and retrying the load; if *that* also fails, it gives up and creates a fresh empty
  settings document (`CreateNewSettings`) rather than crashing the app on startup. This
  three-tier fallback (primary → backup → fresh) is the core resilience pattern worth
  copying verbatim into a new app.
- **Per-setting roaming vs. machine-scoped storage**, decided by `IsRoaming(setting)`
  (a per-property attribute check): roaming settings are stored directly under
  `Settings/<PropertyName>`; machine-specific settings are stored under
  `Settings/<Environment.MachineName>/<PropertyName>` in the same file. This lets one
  settings file (which itself may be synced/backed-up/roamed by the user) still hold
  values that legitimately differ per machine (e.g. a specific audio device name, a window
  position) without those values leaking onto a different machine.
- **Defaults and read failures degrade gracefully.** `GetValue` catches any exception during
  XML lookup and falls back to the setting's declared `DefaultValue`, rather than throwing
  and taking down whatever code path first touches settings.

## Design principles to carry into a new app

1. **Use a fixed, version-independent settings path.** Base it on the product/company name,
   not on assembly version or install location, so settings survive app updates and don't
   silently reset. Make the path and filename overridable (virtual methods, not hardcoded
   inline) so a test harness or a portable-mode variant can redirect them.
2. **Never let a corrupt settings file take down app startup.** Build in the same three-tier
   fallback: try the primary file, fall back to a maintained backup copy, and if both fail,
   start from a fresh empty settings document rather than crashing. Maintain the backup by
   copying the primary file immediately after every successful load (cheap insurance against
   the *next* load, not the current one).
3. **Decide roaming vs. machine-scoped per setting, not globally.** Values that make sense
   to follow the user (window layout preferences, feature toggles) are roaming; values that
   are inherently tied to physical hardware on a specific machine (audio device name, COM
   port, monitor position) should be stored under a machine-name-scoped section of the same
   file so they don't corrupt behavior when the settings file itself is copied/synced to
   another machine.
4. **Fail toward the declared default, never toward an exception, when reading a setting.**
   A missing or malformed entry should silently resolve to the setting's default value; a
   settings read should never be a plausible crash site in the app.
5. **Keep the storage format human-inspectable.** Plain XML (or JSON) that a user or support
   engineer can open in a text editor is worth the small size/parse cost over a binary format,
   for the same debuggability reason the text command protocol is preferred in
   `radio-protocol-conventions`.
6. **Persist only user preferences, never transient/live state**, per the existing rule in
   `smart-sdr-architecture`/`sdr-generated-app.instructions.md` — this skill covers *how* to
   persist correctly, not a license to persist things that shouldn't be (live signal data,
   decoder confidence values, etc.).

## Quick implementation checklist

- settings path/filename based on product name, not assembly version, and overridable
- primary-file load wrapped in a backup-copy-on-success / restore-from-backup-on-failure /
  fresh-document-as-last-resort three-tier fallback
- roaming vs. machine-scoped decided per setting, both stored in the same file under
  distinct XML sections (or equivalent)
- every setting read falls back to its declared default on any read/parse failure rather
  than throwing
- storage format is human-inspectable (XML/JSON), not an opaque binary blob
