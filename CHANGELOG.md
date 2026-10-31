# Changelog

## [1.1.1] - 2026-11-01

- The *Move building* row is now inserted in front of the vanilla *Remove* row instead of being appended after it,
  where short craft lists scrolled it out of view.
- Moving a building onto (or partly onto) its own previous footprint is allowed, so buildings that share ground with
  decorations can be nudged at all.
- Placement keeps working when the object has no zone of its own: the zone is resolved from its footprint, so nothing
  can be dropped outside the buildable area any more.
- The object now lands exactly where the preview was shown; the vanilla full-cell rounding that could shift a move by
  up to half a cell is bypassed.
- After a successful placement the pick mode is ready for the next building straight away, while a rejected placement
  keeps the current building in hand.
- New *Move building* icon in the vanilla build-tile style (blue tile, white four-direction arrow), embedded in the DLL.
- The build grid is re-aligned and recoloured after every move instead of being moved without a refresh.
- Building without a game install no longer picks up the CI stubs when it should use the real assemblies.

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

