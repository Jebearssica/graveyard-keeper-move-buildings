# CI without a game install

The plugin links against `Assembly-CSharp.dll`, `Assembly-CSharp-firstpass.dll` and the Unity modules that ship
with Graveyard Keeper, so building it normally requires the game. CI has no game, therefore `ci/refs` holds
declaration-only stand-ins for the two game assemblies.

They are generated, not written by hand, and they contain **no game code** - only the type and member signatures the
plugin actually references (roughly 50 types / 120 members). Every generated member is `public`, which is what the
local build gets from the publicizer; nothing is compiled into the plugin from these files.

## Layout

| Path | Purpose |
|---|---|
| `refs/Assembly-CSharp/*.Stub.cs`, `refs/Assembly-CSharp-firstpass/*.Stub.cs` | Generated stub sources (committed) |
| `refs/*/AssemblyInfo.cs` | Generated `AssemblyVersion`, matching the shipped assemblies so the plugin's `AssemblyRef` is unchanged |
| `refs/*/*.csproj` | Stub projects, referenced by `MoveBuildings.csproj` when `UseGameRefs=false` |
| `IgnoresAccessChecksTo.cs` | Access exemption for the non-public game members, compiled only into stub builds |
| `stubgen/` | The generator (Mono.Cecil, net8.0); not part of the plugin |

## Regenerating

Run this from the repository root on a machine that has the game, then commit the result:

```powershell
# build the plugin against the real assemblies first - stubgen reads its IL references
dotnet build -c Release

dotnet run --project ci/stubgen -c Release -- `
    bin/Release/MoveBuildings.dll `
    ci/refs `
    "<game>\Graveyard Keeper_Data\Managed"
```

Regenerate whenever the plugin starts using a game API it did not use before; the stub build fails to compile
otherwise, so CI catches a stale stub immediately.

Members that are only referenced through `nameof(...)` never appear in the IL and have to be listed in
`StubModel.ExtraMembers` (currently `MainGame.Update`, used by a Harmony attribute).

## Building like CI does

```powershell
dotnet build -c Release -p:UseGameRefs=false -p:DeployToGame=false -p:BepInExCore="<extracted BepInEx>\core"
```

`UseGameRefs` is detected automatically: it is `true` when `<game>\Graveyard Keeper_Data\Managed\Assembly-CSharp.dll`
exists. `DeployToGame` is `true` when `<game>\BepInEx\plugins` exists, and then the built DLL is copied into the game.

## Stub builds are equivalent

Both build modes were compiled and their IL compared member by member (types, fields, properties, methods, method
bodies, references): identical except for the `IgnoresAccessChecksToAttribute` type itself, which in the real-game
build is injected by the publicizer instead of being compiled from `IgnoresAccessChecksTo.cs`.
