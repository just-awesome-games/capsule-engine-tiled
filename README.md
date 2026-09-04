# Capsule Tiled

Tiled scene authoring for [Capsule Engine](https://github.com/just-awesome-games/capsule-engine). Install the package, save maps under the game's scene sources, build: every map becomes a Capsule scene document the game loads by name.

Capsule's build wires one scene format, `*.scene.json`. This module is an authoring module for that seam — it translates Tiled's `.tmj` maps into that format under `obj/` at build time and hands them to Capsule, which validates, canonicalizes, and ships them exactly as it does a hand-authored document. Without the package, Tiled files under a Capsule game's sources are ignored.

## Quick start

1. Reference the package beside Capsule's build package, in the same place, as build-only tooling:

   ```xml
   <PackageReference Include="JAG.Capsule.Build" Version="[0.5.1]" PrivateAssets="all" />
   <PackageReference Include="JAG.Capsule.Tiled" Version="[0.1.1]" PrivateAssets="all" />
   ```

2. Save maps as `.tmj` and tilesets as `.tsj` anywhere under `asset-sources/scenes/`. Tileset images live under `asset-sources/textures/`, as every texture does.

3. Build. A map's key is its path under `asset-sources/scenes/` with forward slashes and no extension, so `scenes/highway/room-02.tmj` is keyed `highway/room-02` and ships at `assets/scenes/highway/room-02.scene.json` beside the executable. Two maps of one stem in different directories are two keys. A scene class claims a document the way it claims any other — see Capsule's [scene authoring](https://github.com/just-awesome-games/capsule-engine/blob/main/docs/scenes.md).

There is nothing to configure. The module activates where Capsule imports scenes (a shell does; any other project sets `CapsuleImportScenes`), looks where Capsule looks (`CapsuleAssetSourcesDir`), and enforces the tile size a game declares with `CapsuleTileSize`. A map at the wrong grid fails the build with the map named. Import problems are reported at the failing file as build errors.

## Tiled subset

The importer reads `.tmj` maps that are orthogonal, finite, square-tiled, CSV-encoded and unflipped. A tileset tile's Class is the tile type, its local tile id is the type's `cell`, and its optional `layer` and `collidableFaces` properties map to the scene document's palette fields.

An object's Class is its entry `type` and its position is its `x` and `y`. A tile object — one dragged out of a tileset, so it carries a gid — also imports a `scale`: its width and height over the tile size of the tileset its gid resolves to, written only when that is not identity. A flipped or rotated tile object is refused. Points and rectangles carry no gid and import as position alone; their size means nothing to Capsule.

Tilesets are image tilesets only — a collection of separate images is refused. A tileset's image is resolved against the `.tsj` and must sit under the game's `asset-sources/textures/`; the layer's `texture` is the image's path under that root, forward slashes and extension included, so `textures/terrain/cave.png` is named `terrain/cave.png`. Its `columns` are copied, and its tile size must be square and equal to the map's. One tile layer paints from one tileset, because a grid cuts its cells from one texture; a layer spanning two is refused naming both, and a layer painting nothing imports as an entry with no texture and the `empty` type alone. Every other constraint is reported by the importer at the failing file.

Tiled's Windows GUI executable writes no console output even on success. Use `tmxrasterizer` when a headless PNG preview is needed.

## How it hooks in

`build/JAG.Capsule.Tiled.targets` ships in the package's `buildTransitive/` and does three things: globs `asset-sources/scenes/**/*.tmj` and every `.tsj` beneath the sources, runs the packed `Capsule.Tiled` process once per build over the stale maps, and adds each derived document to Capsule's `CapsuleSceneDocument` item — carrying its key as `%(CapsuleDocumentKey)` — from a target hooked before the engine's `CapsuleCollectSceneDocuments`. Every Capsule property is read inside a target, because NuGet imports this package's targets before Capsule's. The shipped document keeps the map as its provenance — `source.path` names the `.tmj`, and its hash covers the map and the tilesets it references. Nothing derived is committed.

## Developing

Install the .NET SDK selected by [`global.json`](global.json), then once per clone:

```text
git config core.hooksPath .githooks
```

The module pins the Capsule release it is built against (`CapsuleVersion` in `Directory.Build.props`). To develop against a sibling `capsule-engine` clone, create the ignored `Directory.Build.local.props`:

```xml
<Project>
  <PropertyGroup>
    <CapsuleSourcePath>../capsule-engine</CapsuleSourcePath>
  </PropertyGroup>
</Project>
```

`-p:CapsuleUsePackages=true` on any command forces the pinned package graph for a run that must verify it. The gates are the four commands in `.githooks/pre-commit`; CI runs them in both modes, packs the module, and builds [`samples/TiledGame`](samples/TiledGame/) against that pack as the packaged-consumer proof. A release is a `v*` tag, which `.github/workflows/packages.yml` publishes to NuGet.org. [RELEASING.md](RELEASING.md) is the release by hand, engine bump included.

Capsule Tiled is licensed under the [MIT License](LICENSE).
