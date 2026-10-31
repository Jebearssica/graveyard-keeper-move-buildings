# Changelog

## [1.1.0] - 2026-10-31

- Move buildings from the build desk window: the craft list gets an extra *Move building* row next to the vanilla
  *Remove* row instead of using a hotkey.
- The pick mode stays active after placing an object, so several buildings can be moved in a row; right click or
  *Back* leaves it and reopens the build desk.
- Contents of a moved object are preserved because the same `WorldGameObject` is relocated instead of rebuilt
  (verified on a chest: 8 items before and after, save serializer sees the new position).
- Position-dependent caches are refreshed on placement: build grid shape, chunks, A* path cells, zone membership and
  quality totals, sprite sorting, interaction bubble.
- `AllowNonBuildableObjects` (default `false`), `ShowBuildGrid` (default `true`) and a `SelfTest` switch for
  verification runs.
- Release builds carry no debug symbols and no local paths.
