# Move Buildings

A [BepInEx 5](https://github.com/BepInEx/BepInEx) plugin for **Graveyard Keeper 1** that adds a *Move building* action
to the build desk window, so existing buildings can be relocated without demolishing and rebuilding them.

**Contents are preserved.** The mod never destroys or re-creates the object: it moves the very same
`WorldGameObject` instance and only refreshes the caches that depend on its position. A chest keeps its items, a
workbench keeps its craft queue and quality, a grave keeps its corpse, a zombie station keeps its worker link.

给《墓地守护者 1》加的 BepInEx 模组：在建造台界面里多出一行「移动建筑」，把已有建筑直接搬到新位置，
不拆除、不重建，所以箱子等容器里的物品全部保留。

## Install

1. Install BepInEx 5 for Graveyard Keeper.
2. Download `MoveBuildings.dll` from the [latest release](https://github.com/Jebearssica/graveyard-keeper-move-buildings/releases/latest)
   (or build it from source) and drop it into `BepInEx/plugins/MoveBuildings/`.
3. Start the game.

## Usage

1. Use a build desk to open the building window (the same window where *Remove* lives).
2. Scroll to the bottom of the list and click **Move building** (「移动建筑」).
3. The window closes and the building grid appears. Click a building to pick it up.
   - Mouse: move the preview, **left click** to place, **right click** to cancel.
   - Rotation keys rotate the preview when the object supports variations.
4. After placing, the pick mode stays active so more buildings can be moved. **Right click / Back** leaves the mode
   and reopens the build desk window.

## How contents survive a move

All persistent state of a building lives on its `WorldGameObject.data` (`Item`), e.g. a chest's contents in
`data.inventory`. On placement the mod only:

- writes the new `transform.position` (through the vanilla `MoveWhenPlacingGlobalPos`)
- updates `variation` when the player actually rotated the preview
- refreshes position-dependent caches: `RefreshPositionCache`, `RecalculateGridShape`,
  `ChunkedGameObject.RecalculateChunk`, `UpdatePathCell` (A* navmesh), `RecalculateZoneBelonging` (zone membership and
  quality totals), sprite sorting and the interaction bubble

There is no `DestroyMe`, `SetObject` or other rebuild step, so containers, craft queues, quality, HP, parameters,
orientations and zombie-worker links stay untouched.

## What can be moved

By default: objects that can be produced by the build menu - i.e. an `ObjectCraftDefinition` with
`build_type == Put` whose `out_obj` matches the object id (ids are compared without the trailing `_place`, so both
finished objects and construction sites work). NPCs, mobs, drops and scenery props are ignored.

Set `AllowNonBuildableObjects` to `true` to also allow any world object that only has a removal craft (trees, rocks,
quest props).

## Configuration

`BepInEx/config/gk.movebuildings.cfg`

| Key | Default | Meaning |
|---|---|---|
| `AllowNonBuildableObjects` | `false` | Also allow objects that only have a removal craft |
| `ShowBuildGrid` | `true` | Show the build grid while moving |
| `SelfTest` | `false` | Verification only: after loading a save, walks the build-UI move flow on an existing object and logs `PASS`/`FAIL` |

## Building from source

Against a real install (the default):

```powershell
dotnet build -c Release
```

The game directory is detected when the project sits inside it (for example `<game>/mods-work/MoveBuildings`),
otherwise the default Steam location is used; override it with `-p:GameDir="D:\Games\Graveyard Keeper"`. The build
copies the DLL into `BepInEx/plugins/MoveBuildings/` when BepInEx is installed.

Without the game, using the declaration-only stubs in `ci/refs` (this is what CI does):

```powershell
dotnet build -c Release -p:UseGameRefs=false -p:DeployToGame=false -p:BepInExCore="<extracted BepInEx>\core"
```

See [ci/README.md](ci/README.md) for how the stubs are generated.

Release builds are compiled with `DebugType=none` and a `PathMap`, so the assembly contains no local file paths.

## Implementation notes

- A Harmony postfix on `CraftGUI.AddRemoveBuildingItem` appends the extra row next to the vanilla *Remove* row, and a
  postfix on `CraftGUI.SwitchTab` keeps it visible on every craft tab (the vanilla *Remove* row gets the same special
  treatment).
- A Harmony prefix on `CraftGUI.OnCraft` turns the extra row into the pick mode instead of a craft.
- The preview and the placement validation reuse the vanilla `FloatingWorldGameObject` API
  (`CreateFloatingWorldObjectById`, `MoveCurrentFloatingObject`, `RotateCurrentFloatingObject`), so the preview, grid
  snapping and red "cannot place" feedback match the normal build flow.
- Availability matches `FloatingWorldGameObject.RecalculateAvailability`: per-cell `BuildGrid.IsCellBusy` plus
  `FlowGridCell.IsInsideWorldZone`. Objects that do not belong to a `WorldZone` may move freely.
- Movement is restricted to the object's own `WorldZone` when it has one, which prevents states like a grave placed on
  the street.
- The original object's colliders are temporarily disabled while the preview is active (and restored afterwards) so it
  does not count as occupying its own target cells.
- Any error path runs the same teardown: stop the preview, restore colours/colliders, leave build mode.

## Limitations

- Objects can only be moved inside their original `WorldZone`.
- NPCs and mobs are never movable.
- Vanilla placement snaps to the build grid, so a move can shift an off-grid object by up to half a cell (48 px).
- A handful of quest props are hard-coded by position; the "buildable objects only" default keeps them out of reach.

## License

MIT - see [LICENSE](LICENSE).
