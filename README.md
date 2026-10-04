# Capsule Tiled

Capsule Tiled imports [Tiled](https://www.mapeditor.org/) maps into [Capsule Engine](https://github.com/just-awesome-games/capsule-engine) scenes at build time. Each map under a game's authoring tree becomes a scene document the game loads by name.

## Quick start

1. Reference the package from the game's build project, the console app that references `JAG.Capsule.Build` and whose `Program.cs` runs `CapsuleBuild`:

   ```xml
   <PackageReference Include="JAG.Capsule.Tiled" Version="[0.8.0]" />
   ```

   To build against a `capsule-engine-tiled` clone, name it in the game's ignored `Directory.Build.local.props` beside the engine clone, as Capsule's [source mode](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/build-and-publish.md#consuming-capsule) shows. The clone then supplies the build project's reference to the importer:

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

3. Save maps as `.tmj`, tilesets as `.tsj` and tileset images anywhere under the logic project's `Assets/`, then build. A map's key is its path under `Assets/`, normalized by Capsule's [asset rules](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/assets.md#named-assets). `Scenes/Highway/Room02.tmj` is keyed `scenes/highway/room-02`.

`TiledImporter` has no settings of its own. A map whose tile size differs from the one `WithTileSize` sets fails the build, and a game that calls no `WithTileSize` takes each map's own. Maps under a directory holding a `.capsuleignore` build but never publish. An edited tileset imports again only the maps that name it. An import error names the file that failed.

## Tiled subset

Maps and tilesets are saved with Tiled 1.10 or later. Maps are orthogonal, finite, square-tiled and CSV-encoded. Their objects are points, rectangles, ellipses, polylines, polygons and unflipped tile objects, none of them a template instance. A rectangle or ellipse has both a width and a height or neither. A polyline or polygon has no Rotation. A tileset is a single image whose tile size equals the map's. Anything else fails the build.

| Tiled | Scene document |
| --- | --- |
| Map Background Color | scene `clearColor` |
| Map `baseScene` string property | `baseScene`, the abstract `Scene` subclass key |
| Map `camera` string property | `camera` object `type`, the `Camera` subclass key |
| Map Parallax Origin | `camera` object `scrollCenter`, negated, when the origin is not 0, 0 or any layer has a Parallax Factor other than 1, 1 |
| Map custom property other than `baseScene` and `camera` | the scene member of its name, such as `ambient` or `sampling`, in the value forms below |
| Tile layer | a `tile-map` entry drawn from one tileset, with its `tileSize`, `width`, `height`, `tileTypes` and `tiles` |
| Tile layer tile flipped horizontally, vertically or diagonally | tile map `transforms`, which turns its drawing and collision shape alike |
| Tileset image path under `Assets/` | tile map `texture`, for example `Textures/Terrain/Cave.png` |
| Tileset columns | tile map `columns` |
| Tile Class and local tile id | tile type `name` and `cell` |
| Tile `layer` string property | tile type `layer`, trimmed of surrounding whitespace |
| Tile custom property other than `layer` | the tile type member of its name, in the value forms below; `type` names the `TileType` subclass, and `oneWay` and `solidSides` set those members |
| Tile Collision Editor holding one unrotated polygon or rectangle, on a tile with a `layer` | tile type `shape`, `[[x, y], ...]` in pixels from the tile's top-left corner; an empty editor, or a rectangle covering the tile, is the whole tile |
| Object ID, Class and position | entry `id`, `type`, `x` and `y` |
| Object Rotation | entry `rotation` |
| Tile object size over its tile size | entry `scale` |
| Rectangle or ellipse object Width and Height, when not 0 | entry `size`, `[w, h]` in pixels |
| Polyline or polygon object points | entry `path`, `[[x, y], ...]` in pixels from the object's position; a polygon repeats its first point at the end |
| Object custom property other than `zIndex` | the entry member of its name, in the value forms below |
| `zIndex` int property on a tile layer, object layer or object | entry `zIndex` |
| `collider` bool property on a tile layer | tile map `collider`; true gives the map a collider, and a layer without it only draws |
| Tile layer or object layer Parallax Factor other than 1, 1 | entry `scrollFactor` on the tile map or on each of the layer's objects |

An object's `zIndex` overrides its layer's. A placement with no `zIndex` keeps its class's band.

| Map, object or tile property | Scene, entry or tile type member |
| --- | --- |
| string, int, float or bool | the same value |
| file | the asset's key, its path under `Assets/` (`"Textures/Hazard.png"`); a `.tmj`, `.tmx` or `.scene.json` file is the scene's key, its path with no extension; an unset file is left out |
| color | `"#rrggbb"`, or `"#rrggbbaa"` when not opaque; an unset colour is left out |
| object | the referenced object's id, a number; an unset reference is left out |
| enum stored as a string | the value as written, the member name camel-cased (`"iceCave"`); a multi-value enum's members joined by commas (`"spikes,fire"`) |
| class whose members are `x` and `y`, both set | `[x, y]` |
| class whose members are `left`, `top`, `right` and `bottom`, all set | `[left, top, right, bottom]` |

Capsule checks each member against the `[Authorable]` members of the scene's, the entity's or the tile type's class when the scene loads, and so does a game test that runs `CapsuleScenes.Registry.ComposeAll()`. That check also holds a tile type's `shape` to a convex polygon of 3 or 4 points inside its tile. A custom property named after a key the importer writes itself fails the import. Those keys are the entry keys Capsule's `SceneDocumentKeys.Entry` reserves on an object, a rectangle's or ellipse's `size` and a polyline's or polygon's `path`, a tile's `name`, `cell` and `shape`, and the map's `clearColor` and the document keys `SceneDocumentKeys.Document` reserves. A file outside `Assets/`, an enum stored as a number and any other class value fail the import too. Tiled has no list property, so an array member other than `path` is authored by hand in a scene document.

A tile layer collides only when it sets `collider` to true. Its tileset must then hold a tile with a `layer`, and the layer keeps a Parallax Factor of 1, 1. One tileset paints the solid layer and its decorative or parallax copies, which set no `collider`. An object layer refuses `collider` set to true, because an object collides as its class does. A scene previews in Tiled exactly as it plays when the Tiled view is centred where the game camera is. Tiled's renderer adds the Parallax Origin to the view centre. Set it to minus half the game's viewport, for example -128, -112 for 256x224, to line the layers up at the first screen.

## Property types

A new Tiled project starts as a copy of `capsule.tiled-project`, from the root of the package, placed at the root of `Assets/`. The project then holds every map wherever the game files it. Opening the file in Tiled 1.10 or later adds three classes. `CapsuleLayer` gives a layer's Class dropdown a `zIndex` and a `collider` that defaults to false. `Vector2` is a custom property type with `x` and `y` members, for a `Vector2` member. `Rect` is a custom property type with `left`, `top`, `right` and `bottom` members, for a `Rect` member. An existing project imports the same types through Project > Import Types, from `capsule-property-types.json` at the root of the package. Tiled's command-line export (`tiled --export-map`) keeps a class property's type name only when it is also given the project with `--project <file>.tiled-project`.

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
