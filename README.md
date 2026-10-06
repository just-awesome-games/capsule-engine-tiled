# Capsule Tiled

Capsule Tiled imports [Tiled](https://www.mapeditor.org/) maps into [Capsule Engine](https://github.com/just-awesome-games/capsule-engine) scenes at build time. Each map under a game's authoring tree becomes a scene document the game loads by name.

## Quick start

1. Reference the package from the game's build project, the console app that references `JAG.Capsule.Build` and whose `Program.cs` runs `CapsuleBuild`:

   ```xml
   <PackageReference Include="JAG.Capsule.Tiled" Version="[0.8.0]" />
   ```

   To build against a `capsule-engine-tiled` clone, name it in the game's ignored `Directory.Build.local.props` beside the engine clone, as Capsule's [source mode](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/build-and-publish.md#consuming-capsule) shows:

   ```xml
   <CapsuleSourceOverrides>JAG.Capsule.Tiled=../capsule-engine-tiled</CapsuleSourceOverrides>
   ```

2. Add the importer in the build project's `Program.cs`:

   ```csharp
   using Capsule.Build;
   using JAG.Capsule.Tiled;

   return CapsuleBuild.Configure(args)
       .AddImporter(new TiledImporter())
       .WithTileSize(16)
       .Run();
   ```

3. Save maps as `.tmj` under the logic project's `Assets/` and tilesets as `.tsj`, then build. A map's key is its path under `Assets/`, normalized by Capsule's [asset rules](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/assets.md#named-assets). `Scenes/Highway/Room02.tmj` is keyed `scenes/highway/room-02`.

`TiledImporter` has no settings of its own. A map whose tile size differs from the one `WithTileSize` sets fails the build, and a game that calls no `WithTileSize` takes each map's own. A tileset may live anywhere, and the map names it relative to itself. A missing tileset fails the build as a missing file.

## Tiled subset

Maps and tilesets are saved with Tiled 1.10 or later. Maps are orthogonal, finite, square-tiled and CSV-encoded. Their objects are points, rectangles, ellipses, polylines, polygons and unflipped tile objects, none of them a template instance. A rectangle or ellipse has both a width and a height or neither. A polyline or polygon has no Rotation. A tileset is a single image under `Assets/` whose tile size equals the map's, and a layer paints from one tileset. Anything else fails the build.

| Tiled | Scene document |
| --- | --- |
| Map Background Color | scene `clearColor` |
| Map `baseScene` string property | `baseScene`, the abstract `Scene` subclass key |
| Map `camera` string property | `camera` object `type`, the `Camera` subclass key |
| Map Parallax Origin | `camera` object `scrollCenter`, negated, when the origin is not 0, 0 or any layer has a Parallax Factor other than 1, 1 |
| Map custom property | the scene member of its name, such as `ambient` or `sampling` |
| Tile layer | a `tile-map` entry |
| Tile layer tile flipped horizontally, vertically or diagonally | tile map `transforms`, which turns its drawing and collision shape alike |
| Tile Class | a tile type named after it; unpainted Classes included |
| Painted tile with no Class | a tile type named `cell-<ID>` after its tile ID, following the classed ones in the tile map's `tileTypes` |
| Tile `layer` string property | tile type `layer`, trimmed of surrounding whitespace |
| Tile custom property | the tile type member of its name; `type` names the `TileType` subclass |
| Tile Collision Editor holding one unrotated polygon or rectangle, on a tile with a `layer` | tile type `shape`, `[[x, y], ...]` in pixels from the tile's top-left corner; an empty editor, or a rectangle covering the tile, is the whole tile |
| Object ID, Class, position and Rotation | entry `id`, `type`, `x`, `y` and `rotation` |
| Tile object size over its tile size | entry `scale` |
| Rectangle or ellipse object Width and Height, when not 0 | entry `size`, `[w, h]` in pixels |
| Polyline or polygon object points | entry `path`, `[[x, y], ...]` in pixels from the object's position; a polygon repeats its first point at the end |
| Object custom property | the entry member of its name |
| `zIndex` int property on a tile layer, object layer or object | entry `zIndex`; an object's overrides its layer's |
| `collider` bool property on a tile layer | tile map `collider`; a layer without it only draws |
| Tile layer or object layer Parallax Factor other than 1, 1 | entry `scrollFactor` on the tile map or on each of the layer's objects |

Custom properties take these value forms. A property named after a key the importer writes itself fails the import.

| Map, object or tile property | Scene, entry or tile type member |
| --- | --- |
| string, int, float or bool | the same value |
| file | its path under `Assets/`, as `"Textures/Hazard.png"`; a `.tmj` or `.scene.json` file is the scene's key, its path with no extension; an unset file is left out |
| color | `"#rrggbb"`, or `"#rrggbbaa"` when not opaque; an unset color is left out |
| object | the referenced object's id; an unset reference is left out |
| enum stored as a string | the value as written, as `"iceCave"` or `"spikes,fire"` |
| class value setting exactly `x` and `y` | `[x, y]` |
| class value setting exactly `left`, `top`, `right` and `bottom` | `[left, top, right, bottom]` |
| list, saved by Tiled 1.12 or later | an array of its items in order, each in the form above for its type, for a `T[]` member such as `[10, 12]` |

Tiled saves only the members a class value sets, so set each one even where it is 0. An enum stored as a number, any other class value and an unset list item fail the import. A `[Flags]` enum member takes one enum value naming its flags, as `"spikes,fire"`, and an array of enums takes a list.

A scene previews in Tiled as it plays when the Tiled view is centred where the game camera is. Tiled's renderer adds the Parallax Origin to the view centre. Set it to minus half the game's viewport, for example -128, -112 for 256x224, to line the layers up at the first screen.

## Property types

A new Tiled project starts as a copy of `capsule.tiled-project`, from the root of the package, placed at the root of `Assets/`. It adds three classes. `CapsuleLayer` gives a layer a `zIndex` and a `collider`. `Vector2` and `Rect` are property types for members of those types. An existing project imports the same types from `capsule-property-types.json` through Project > Import Types. Tiled's command-line export (`tiled --export-map`) keeps a class property's type name only when it is given the project with `--project <file>.tiled-project`.

## Developing

Install the .NET SDK selected by [`global.json`](global.json), then enable the hooks once per clone:

```text
git config core.hooksPath .githooks
```

The library and the test build project reference `JAG.Capsule.Build`, and the test project references `JAG.Capsule`. The three references pin one Capsule release. To build against a sibling `capsule-engine` clone, create the ignored `Directory.Build.local.props`:

```xml
<Project>
  <PropertyGroup>
    <CapsuleSourcePath>../capsule-engine</CapsuleSourcePath>
  </PropertyGroup>
  <Import Project="$(CapsuleSourcePath)/build/Capsule.Build.props" Condition="'$(CapsuleSourcePath)' != ''" />
</Project>
```

`-p:CapsuleSourcePath=` forces the pinned packages. The gates are the four commands in `.githooks/pre-commit`. [RELEASING.md](RELEASING.md) is the release procedure.

Capsule Tiled is licensed under the [MIT License](LICENSE).
