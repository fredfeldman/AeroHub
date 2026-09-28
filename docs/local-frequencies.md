# AeroHub Local Frequencies

This file records local monitoring frequencies to seed editable source profiles and fixtures. Frequencies listed here are operational defaults for this workspace, not protocol constants.

## ACARS VHF

| Frequency MHz | Mode | Initial Use | Status |
|---:|---|---|---|
| 136.800 | AM/VHF ACARS | Local ACARS monitoring profile | User-provided |
| 136.975 | AM/VHF ACARS | Local ACARS monitoring profile | User-provided |
| 136.650 | AM/VHF ACARS | Local ACARS monitoring profile | User-provided |

## Implementation Notes

1. Treat these as editable receiver/source profile defaults.
2. Store tuned frequency, receiver mode, sample rate, bandwidth, gain, and source adapter with every capture or decoded message.
3. Do not hard-code these frequencies inside ACARS parsing or decoder logic.
4. Allow users to add, disable, reorder, and annotate frequencies from the frontend once settings persistence exists.