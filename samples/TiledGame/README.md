# TiledGame

A complete Capsule game that authors its room in Tiled, and this module's consumer proof. It is Capsule's [MinimalGame](https://github.com/just-awesome-games/capsule-engine/tree/main/samples/MinimalGame) with one change: `src/asset-sources/scenes/room.tmj` and `tiles.tsj` replace the hand-authored room document, and `JAG.Capsule.Tiled` is referenced beside `JAG.Capsule.Build`. Nothing else is configured — the map ships as `assets/scenes/room.scene.json` because the package is present.

| Path | What it shows |
| --- | --- |
| `Directory.Build.props` | The two build-only package references a Tiled-authoring game carries. |
| `src/asset-sources/scenes/room.tmj` + `tiles.tsj` | The Tiled room. `tiles.tsj` is an image tileset over `textures/tiles.png`; its tile properties carry the collision layer each tile type is on and which faces collide — the ledges declare `top` alone, which makes them one-way platforms. |
| `src/asset-sources/scenes/hall.scene.json` | A hand-authored document beside the map: both origins ship through the same hook. |
| `src/TiledGame.Game/Scenes/Room.cs` | The class half of the room, claiming the document the map became. |
| `src/TiledGame.Shell/TiledGame.Shell.csproj` | The shell, plus the assertion CI relies on: the shipped document exists and names the map as its provenance. |

Inside this repository the sample restores `JAG.Capsule.Tiled` from the tree's own pack. Run it after packing:

```text
dotnet pack Capsule.Tiled.slnx --configuration Release --output artifacts/packages -p:CapsuleUsePackages=true
dotnet restore samples/TiledGame/TiledGame.slnx --configfile samples/TiledGame/NuGet.config
dotnet run --project samples/TiledGame/src/TiledGame.Shell
```

A copy outside the repository sets `CapsuleTiledVersion` to a release and drops the local feed from `NuGet.config`.
