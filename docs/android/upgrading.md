# Updating the Android branch

Use an explicit upstream merge or rebase branch; never overwrite the product
tree with a downloaded snapshot.

1. Fetch `upstream` and `android-fork`.
2. Create `upgrade/smapi-X.Y.Z` from `android/junimogate`.
3. Merge the chosen official SMAPI tag while retaining the complete official
   repository tree.
4. Resolve conflicts by patch responsibility: build boundary, lifecycle,
   loaders, input/audio, rewrite adapters, mod compatibility, host boundary,
   then idle-path optimizations.
5. Keep Android changes guarded by `SMAPI_FOR_ANDROID` or the existing host
   abstractions where practical. Do not fork unrelated desktop behavior.
6. Update metadata, translations, blacklist data, package versions, and the
   provenance table deliberately.
7. Run the validation in `validation.md` before updating JunimoGate's submodule
   pointer.
8. Update the submodule pointer in a separate JunimoGate commit so runtime and
   launcher changes remain reviewable.

When upstream changes a game-facing API, prefer semantic compatibility checks
and local adapters. Do not add full-assembly hashes, fixed MVIDs, or global IL
counts as normal launch requirements.

