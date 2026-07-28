# Android validation

The Android project needs compile-only assemblies extracted from the user's
legitimate game installation. Point JunimoGate builds at that private folder:

```bash
export JUNIMOGATE_GAME_REFERENCE_DIR=/absolute/path/to/analysis/assemblies
```

From the JunimoGate superproject, validate a submodule update with:

```bash
dotnet test JunimoGate.sln
./build/build-android.sh Debug app
./build/build-android.sh Release app
./build/verify-android-artifacts.sh
```

Also verify:

- Debug and Release build with zero warnings and zero errors;
- no commercial game files enter either Git repository or the APK as owned
  payload;
- the generated `StardewModdingAPI.dll` matches the reviewed source commit;
- a fresh clone with `git submodule update --init --recursive` builds;
- a physical ARM64 device can Fast Launch a zero-Mod session through SMAPI;
- title screen, input, audio, Home/Back, resume, and clean process exit work;
- at least one representative real mod loads and produces a visible effect
  before publishing a compatibility claim.

Byte-identical APKs are not required across paths or build invocations because
packaging metadata may differ. Compare the managed/native payload inventory and
explain any remaining archive-level difference.
