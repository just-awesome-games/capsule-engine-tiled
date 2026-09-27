# Capsule Tiled

Capsule Tiled imports [Tiled](https://www.mapeditor.org/) maps into [Capsule Engine](https://github.com/just-awesome-games/capsule-engine) scenes at build time. Each map under a game's authoring tree becomes a scene document the game loads by name.

## Quick start

1. Reference the package beside Capsule's build package:

   ```xml
   <PackageReference Include="JAG.Capsule.Build" Version="[0.8.0]" PrivateAssets="all" />
   <PackageReference Include="JAG.Capsule.Tiled" Version="[0.6.0]" PrivateAssets="all" />
   ```

2. Save maps as `.tmj`, tilesets as `.tsj` and tileset images anywhere under the logic project's `Assets/`.

3. Build. A map's key is its path under `Assets/`, normalized by Capsule's [asset rules](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/assets.md#named-assets). `Scenes/Highway/Room02.tmj` is keyed `scenes/highway/room-02`.

The package has no settings of its own. It runs where Capsule imports scenes (`CapsuleImportScenes`) and reads maps from `CapsuleAssetSourcesDir`. A map whose tile size differs from `CapsuleTileSize` fails the build. Maps under a directory holding a `.capsuleignore` build but never publish. An import error names the file that failed.

## Tiled subset

Maps and tilesets are saved with Tiled 1.10 or later. Maps are orthogonal, finite, square-tiled and CSV-encoded. Their objects are points, rectangles, ellipses, polylines, polygons and unflipped tile objects, none of them a template instance. A rectangle or ellipse has both a width and a height or neither. A polyline or polygon has no Rotation. A tileset is a single image whose tile size equals the map's. Anything else fails the build.

| Tiled | Scene document |
| --- | --- |
| Map Background Color | `clearColor`, opaque |
| Map `ambient` color property | `ambient`, opaque |
| Map `sampling` string property, `linear` or `point` | `sampling` |
| Map `baseScene` string property | `baseScene`, the abstract `Scene` subclass key |
| Map `camera` string property | `camera`, the `Camera` subclass key |
| Map Parallax Origin | `scrollCenter`, negated, when the origin is not 0, 0 or any layer has a Parallax Factor other than 1, 1 |
| Tile layer | a tile map entry drawn from one tileset |
| Tile layer tile flipped horizontally, vertically or diagonally | tile map `transforms`, which turns its drawing and collision shape alike |
| Tileset image path under `Assets/` | tile map `texture`, for example `Textures/Terrain/Cave.png` |
| Tileset columns | tile map `columns` |
| Tile Class and local tile id | tile type `type` and `cell` |
| Tile `layer` string property | tile type `layer` |
| Tile `oneWay` and `solidSides` bool properties | tile type `oneWay` and `solidSides` |
| Tile Collision Editor holding one unrotated convex polygon of 3 or 4 points, or one rectangle, on a tile with a `layer` | tile type `shape`; an empty editor, or a rectangle covering the tile, is the whole tile |
| Object Class and position | entry `type`, `x` and `y` |
| Object Rotation | entry `rotation` |
| Tile object size over its tile size | entry `scale` |
| Rectangle or ellipse object Width and Height, when not 0 | `size` property, `[w, h]` in pixels |
| Polyline or polygon object points | `path` property, `[[x, y], ...]` in pixels from the object's position; a polygon repeats its first point at the end |
| Object custom property other than `zIndex` | entry `properties`, by name, in the value forms below |
| `zIndex` int property on a tile layer, object layer or object | entry `zIndex` |
| Tile layer or object layer Parallax Factor other than 1, 1 | entry `scrollFactor` on the tile map or on each of the layer's objects |

An object's `zIndex` overrides its layer's. A placement with no `zIndex` keeps its class's band.

| Object property | Entry property |
| --- | --- |
| string, int, float or bool | the same value |
| file | the asset's key, its path under `Assets/` (`"Textures/Hazard.png"`); a `.tmj`, `.tmx` or `.scene.json` file is the scene's key, its path with no extension; an unset file is left out |
| color | `"#rrggbb"`, or `"#rrggbbaa"` when not opaque; an unset colour is left out |
| object | the referenced object's id, a number; an unset reference is left out |
| enum stored as a string | the value as written, the member name camel-cased (`"iceCave"`); a multi-value enum's members joined by commas (`"spikes,fire"`) |
| class whose members are `x` and `y`, both set | `[x, y]` |

Capsule's build checks every entry property against the entity class's `[Authorable]` members. A rectangle or ellipse with a width and height already writes `size`, so a custom property of its own named `size` fails the import. A polyline or polygon already writes `path`, so a custom property named `path` on one fails it the same way. A file outside `Assets/`, an enum stored as a number and any other class value fail it too. Tiled has no list property, so an array member other than `path` is authored by hand in a scene document.

A tile layer painted from a tileset with any `layer` tile keeps a Parallax Factor of 1, 1. A scene previews in Tiled exactly as it plays when the Tiled view is centred where the game camera is. Tiled's renderer adds the Parallax Origin to the view centre. Set it to minus half the game's viewport, for example -128, -112 for 256x224, to line the layers up at the first screen.

## Property types

The build seeds `<project>.tiled-project` at the root of `Assets/` while no `.tiled-project` exists under it. Opening it in Tiled 1.10 or later adds the `CapsuleLayer` class, which gives a layer's Class dropdown a `zIndex`. The build never overwrites the file. An existing project imports the same type through Project > Import Types, from `capsule-property-types.json` in the package's `buildTransitive/`.

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

Capsule Tiled is licensed under the [Mozilla Public License 2.0](LICENSE).
