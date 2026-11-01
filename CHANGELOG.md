# Changelog

## [1.1.5] - 2026-11-01

- Fixed the duplicated *Move building* rows and the empty craft list that showed up after a move: leaving the move
  mode cleared the build zone inventory of the game while the zone id stayed set, so the reopened build window threw
  inside `CanCraft()` for every row it tried to draw and stayed empty. The inventory is now restored together with the
  zone, which is what the vanilla build/remove flow always leaves in place.
- Every row this mod creates is drawn inside a guard: a row that fails half way through is destroyed again instead of
  staying in the table with the prefab icon and the prefab costs (that was the "old fist icon" that appeared next to
  proper rows).
- The move row is now deduplicated against the whole craft table, not only against the item list, so rows left behind
  by a previous window are dropped and exactly one row remains per window.
- No row is added to a window that has no vanilla rows yet; such a half opened window is no longer filled with rows
  that can never be positioned.
- Removed the temporary diagnostic logging from 1.1.4 (the single "Added the move row to '<desk>'" line stays).


## [1.1.4] - 2026-11-01

- The move row is now added from three places: the vanilla "add remove row" hook, the window-open hook and a slow
  safety net in the main loop that re-adds (or re-shows) it every half second while a builder window is open. Other
  mods that rebuild or re-filter the craft list can no longer make the row disappear.
- The row is also logged once per window ("Added the move row to '<desk>'"), so a missing row can be told apart from
  a mod that never opens the vanilla build window.


## [1.1.3] - 2026-11-01

- Graves can be moved now. They have no build craft of their own (the body is stored in the object), so they are
  matched by their grave interaction type instead of a `Put` craft. A buried body stays in the object while it is
  carried - verified in game: the same `body` item before and after the move.


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


