# Capsule Tiled

Capsule Tiled imports [Tiled](https://www.mapeditor.org/) maps into [Capsule Engine](https://github.com/just-awesome-games/capsule-engine) scenes at build time. Each map under a game's scene sources becomes a scene document the game loads by name.

## Quick start

1. Reference the package beside Capsule's build package:

   ```xml
   <PackageReference Include="JAG.Capsule.Build" Version="[0.8.0]" PrivateAssets="all" />
   <PackageReference Include="JAG.Capsule.Tiled" Version="[0.6.0]" PrivateAssets="all" />
   ```

2. Save maps as `.tmj` and tilesets as `.tsj` under the logic project's `Assets/Scenes/`. Tileset images go under `Assets/Textures/`.

3. Build. A map's key is its path under `Scenes/`, normalized by Capsule's [scene rules](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/scenes.md). `Scenes/Highway/Room02.tmj` is keyed `highway/room-02`.

The package has no settings of its own. It runs where Capsule imports scenes (`CapsuleImportScenes`) and reads maps from `CapsuleAssetSourcesDir`. A map whose tile size differs from `CapsuleTileSize` fails the build. Maps under a directory holding a `.capsuleignore` build but never publish. An import error names the file that failed.

## Tiled subset

Maps are orthogonal, finite, square-tiled, CSV-encoded and unflipped. A tileset is a single image whose tile size equals the map's. Anything else fails the build.

| Tiled | Scene document |
| --- | --- |
| Map Background Color | `clearColor`, opaque |
| Map `ambient` color property | `ambient`, opaque |
| Map `sampling` string property, `linear` or `point` | `sampling` |
| Map `baseScene` string property | `baseScene`, the abstract `Scene` subclass key |
| Map `camera` string property | `camera`, the `Camera` subclass key |
| Tile layer | a tile map entry drawn from one tileset |
| Tileset image path under `Assets/Textures/` | tile map `texture`, for example `Terrain/Cave.png` |
| Tileset columns | tile map `columns` |
| Tile Class and local tile id | tile type `type` and `cell` |
| Tile `layer` string property | tile type `layer` |
| Tile `oneWay` and `solidSides` bool properties | tile type `oneWay` and `solidSides` |
| Object Class and position | entry `type`, `x` and `y` |
| Tile object size over its tile size | entry `scale` |
| `zIndex` int property on a tile layer, object layer or object | entry `zIndex` |

An object's `zIndex` overrides its layer's. A placement with no `zIndex` keeps its class's band.

## Property types

The build seeds `<project>.tiled-project` beside the maps when the scenes root holds no `.tiled-project`. Opening it in Tiled 1.9 or later adds the `CapsuleLayer` class, which gives a layer's Class dropdown a `zIndex`. The build never overwrites the file. An existing project imports the same type through Project > Import Types, from `capsule-property-types.json` in the package's `buildTransitive/`.

## Developing

Install the .NET SDK selected by [`global.json`](global.json), then enable the hooks once per clone:

```text
git config core.hooksPath .githooks
```

`Directory.Build.props` pins the Capsule release (`CapsuleVersion`). To build against a sibling `capsule-engine` clone, create the ignored `Directory.Build.local.props`:

```xml
<Project>
  <PropertyGroup>
    <CapsuleSourcePath>../capsule-engine</CapsuleSourcePath>
  </PropertyGroup>
</Project>
```

`-p:CapsuleUsePackages=true` forces the pinned packages. The gates are the four commands in `.githooks/pre-commit`. [RELEASING.md](RELEASING.md) is the release procedure.

Capsule Tiled is licensed under the [MIT License](LICENSE).
