# Changelog

## [1.1.2] - 2026-11-01

- Fixed the *Move building* row landing in the wrong slot: it is now anchored on the vanilla *Remove* row for both the
  list order and the hierarchy order, so it always sits directly above *Remove* and stays visible.
- Leftover rows from a window that was closed on the same frame are cleaned up instead of piling up.
- The build window is reopened one frame after leaving the move mode, so the table never lays out a half-destroyed list.
- The craft list is laid out once more right after the window opens when every visible row ended up stacked at the
  origin, which is what made all buildings show up in a single line (seen at the garden desk).
- A failure while adding the row can no longer escape into the vanilla open routine and leave the whole list unlaid.
- Added defensive guards around the language lookup used for the row label.


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


