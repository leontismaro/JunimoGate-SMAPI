# Android branch provenance

The `android/junimogate` branch carries the Android runtime used by
JunimoGate. It preserves the complete official SMAPI history and keeps the
Android product changes as ordinary Git commits on top of an official tag.

## Sources

| Source | Commit | Purpose |
| --- | --- | --- |
| [Pathoschild/SMAPI](https://github.com/Pathoschild/SMAPI) | `821167e5c511bf3a2d98f604e5e838561c469219` | Official SMAPI 4.5.2 base |
| [NRTnarathip/SMAPI-Android-1.6](https://github.com/NRTnarathip/SMAPI-Android-1.6) | `6a34bbeb6e891536cdd948594094482ba0d8d264` | Android runtime lineage |
| Android fork common ancestor | `604b2acf3ad2121d4c16fbb895883c5e5538db64` | Historical merge point with official SMAPI |
| JunimoGate vendor import | `c22449a` | Selected Android runtime snapshot |
| JunimoGate 4.5.2 migration | `e582d17` | Current product-equivalent source snapshot |

The Android fork's development history contains experimental and work-in-
progress commits. Those commits are not merged wholesale into this branch.
Instead, the selected runtime was imported as a provenance baseline and the
JunimoGate product changes were replayed as documented commits. Commit
trailers record the source snapshot for each layer.

## Licensing and payload policy

SMAPI and the Android fork are licensed under LGPL-3.0. Keep `LICENSE.txt`,
source availability, and modification records with every distribution of the
modified runtime.

This repository must not contain Stardew Valley APKs, assemblies, Content,
native libraries, AOT output, decompiled source, saves, or other commercial
payloads. Game assemblies may be supplied from a user's legitimate local
installation as non-copying compile references. They must never be committed
or packaged as SMAPI-owned files.

