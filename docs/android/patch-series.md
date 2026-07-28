# Android patch series

Git commits on `android/junimogate` are the authoritative patch series. There
is intentionally no second set of generated `.patch` files.

Apply and review the layers in branch order:

| Layer | Source | Responsibility |
| --- | --- | --- |
| `feat(android): import runtime patch baseline` | Android fork `6a34bbeb`, JunimoGate `c22449a` | Android build boundary, runner, lifecycle, loaders, audio, input, rewrite and mod adapters |
| `feat(host): add JunimoGate session boundary` | JunimoGate `99f99a3` | Embedded host/session contract and input integration |
| `build(android): update SkiaSharp to 4.150.1` | JunimoGate `e50a858` | Android ARM64 native dependency alignment |
| `feat(android): separate user data from runtime` | JunimoGate `840c859` | Writable saves/config/logs outside the prepared runtime |
| `fix(android): harden runtime adapters` | JunimoGate `dd253f1` | Lifecycle, thread, content, save, patching, reflection and audio fixes |
| `chore(android): eliminate build warnings` | JunimoGate `fd01474` | Nullability and Android build cleanup |
| `perf(android): eliminate console polling` | JunimoGate `e011ab1` | Remove the idle developer-console polling thread |
| `perf(android): skip unused frame tracking` | JunimoGate `258d8c1` | Subscriber-aware watcher/render short circuits and idle cleanup |
| `feat(android): migrate runtime patches to SMAPI 4.5.2` | JunimoGate `e582d17` | Upstream 4.5.2 input, content, metadata, compatibility and security changes |

The first import is a selected product snapshot, not a claim that JunimoGate
authored the Android fork. Preserve the `Ported-from`, `Source-snapshot`, and
`Upstream-base` trailers when rebasing or splitting these commits.

JunimoGate pins an exact commit through its `smapi/` submodule. Do not set a
submodule branch that silently advances during a launcher build.

