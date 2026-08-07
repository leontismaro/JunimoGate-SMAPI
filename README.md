**English** | [简体中文](README.zh-CN.md)

# JunimoGate SMAPI

JunimoGate SMAPI is the Android-focused SMAPI fork used by the
[JunimoGate launcher](https://github.com/leontismaro/JunimoGate). It is built
from source as the launcher's `smapi/` Git submodule and hosted through an
explicit Android session contract.

## Android integration

This branch keeps upstream SMAPI behavior while adding the Android and host
boundaries required by JunimoGate:

- a `net9.0-android35.0` ARM64 build target;
- host-owned Activity, storage, content, save, log, and Mod paths;
- Android main-thread dispatch, lifecycle, view, input, and audio integration;
- a unified managed assembly-loading boundary for the game, SMAPI, and Mods;
- Android save serializer registration and writable user-data separation;
- structured session startup and outcome reporting to the launcher;
- Android Mod dependency binding and reusable rewrite-cache support.

The maintained host contract and process model are documented in the launcher
repository's
[SMAPI architecture](https://github.com/leontismaro/JunimoGate/blob/main/docs/smapi-architecture.md).

## Source and acknowledgements

This branch is derived directly from:

- [Pathoschild/SMAPI](https://github.com/Pathoschild/SMAPI), the official SMAPI
  project maintained by Pathoschild and contributors;
- [NRTnarathip/SMAPI-Android-1.6](https://github.com/NRTnarathip/SMAPI-Android-1.6),
  the direct Android runtime lineage used by this branch.

We thank both projects and the related community work that preceded them.
Exact commits, import points, and the maintained patch layers are recorded in
[provenance](docs/android/provenance.md) and the
[Android patch series](docs/android/patch-series.md).

## Build and validation

The recommended build path is through a recursive checkout of the JunimoGate
launcher repository, which supplies the pinned Android toolchain and locally
built Harmony and MonoGame packages.

Android compilation also requires a directory containing the game assemblies
used as compile references:

```bash
export JUNIMOGATE_GAME_REFERENCE_DIR="/absolute/path/to/game/assemblies"
```

See [Android validation](docs/android/validation.md) for the build entry points,
required inputs, and checks. Maintenance procedures are in
[upgrading the Android branch](docs/android/upgrading.md).

## Upstream documentation

The preserved SMAPI documentation starts at [docs/README.md](docs/README.md).
For general SMAPI player and Mod author documentation, see
[smapi.io](https://smapi.io/).

## License

JunimoGate SMAPI is licensed under the
[GNU Lesser General Public License version 3](LICENSE.txt), identified as
LGPL-3.0-only. Modified SMAPI source remains under the same license.

Third-party dependencies retain their respective licenses. JunimoGate launcher
code is maintained in a separate repository under its own project license.
