# CI without a game install

The plugin links against `Assembly-CSharp.dll`, `Assembly-CSharp-firstpass.dll` and the Unity modules that ship
with Graveyard Keeper, so building it normally requires the game. CI has no game, therefore `ci/refs` holds
declaration-only stand-ins for the two game assemblies.

They are generated, not written by hand, and they contain **no game code** - only the type and member signatures the
plugin actually references (roughly 50 types / 120 members). Every generated member keeps the accessibility it has in
the real assembly, and nothing is compiled into the plugin from these files.

Keeping the real accessibility is what makes the stub build a guard: a direct read of a `protected`/`private` game
member compiles against a loosened reference but throws `FieldAccessException` / `MethodAccessException` on a runtime
that enforces visibility. Because the stub (and the local build, which no longer publicizes the game assemblies) uses
the real modifiers, such an access fails to compile instead. Non-public members go through `GameAccess.cs`.

## Layout

| Path | Purpose |
|---|---|
| `refs/Assembly-CSharp/*.Stub.cs`, `refs/Assembly-CSharp-firstpass/*.Stub.cs` | Generated stub sources (committed) |
| `refs/*/AssemblyInfo.cs` | Generated `AssemblyVersion`, matching the shipped assemblies so the plugin's `AssemblyRef` is unchanged |
| `refs/*/*.csproj` | Stub projects, referenced by `MoveBuildings.csproj` when `UseGameRefs=false` |
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

Optional parameters are a caveat: the game assemblies do not store every default value in metadata, so a stub may
declare `= default` where the real default is something else (`CraftGUI.OnCraft`'s `amount` is the known case, its
real default is `1`). Pass such arguments explicitly instead of relying on the default.

## Building like CI does

```powershell
dotnet build -c Release -p:UseGameRefs=false -p:DeployToGame=false -p:BepInExCore="<extracted BepInEx>\core"
```

`UseGameRefs` is detected automatically: it is `true` when `<game>\Graveyard Keeper_Data\Managed\Assembly-CSharp.dll`
exists. `DeployToGame` is `true` when `<game>\BepInEx\plugins` exists, and then the built DLL is copied into the game.

## Stub builds are equivalent

Both build modes were compiled and their IL compared member by member (types, fields, properties, methods, method
bodies, references): identical. Nothing in the plugin is conditional on the build flavour any more, so the assembly
that CI releases is the one that is compiled and tested locally.
