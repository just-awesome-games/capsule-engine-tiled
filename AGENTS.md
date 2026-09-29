# Agent rules

These rules cover judgments the build cannot enforce.

## Scope

This repository is one authoring module for Capsule. It turns Tiled maps into Capsule scene documents at build time and does nothing else. Engine behavior (the document format, validation, what a scene is) lives in [capsule-engine](https://github.com/just-awesome-games/capsule-engine) and is consumed here, never redefined. What lands is complete and never knowingly suboptimal. JAG's own games are the only considered consumers. Break the module's surface when a better design needs it, and migrate the consuming game in the same wave. A removed field or property is deleted outright. No code stays behind to point at its replacement. Add no option a game has not asked for. The module reads Capsule's properties and defines none.

## Where code goes

`TiledImporter` is what a game's build project adds. `MapImporter` turns one map into one scene document.

- A map property is a scene setting in `MapImporter`.
- A tile property belongs to the tile definition in `TilesetImporter`.
- A layer or object property goes in `LayerImporter`. A new Tiled layer type is a new case in its dispatch and a new method beside the others.
- A new property value type is a new read on `TiledProperties`.

Each addition lands with its README table row and one test in the matching test class: `MapImportTests`, `TilesetImportTests`, `LayerImportTests` or `ObjectImportTests` for object placement and properties. A test class that outgrows its file splits by concern.

## Documentation

`README.md` is the whole user documentation: what the module is, the quick start, the Tiled subset and how to develop. It is declarative and current, with no changelog, migration note or forward reference. `PACKAGE.md` is the NuGet readme and carries absolute links only.

A comment states an invariant or a why. Delete walkthroughs, section labels and commentary addressed to reviewers.

## Boundaries

`JAG.Capsule.Tiled` is a library a game's build project references. It references `JAG.Capsule.Build` and nothing else of Capsule. `TiledImporter` is its only public type, and it implements Capsule's `IAssetImporter`. `build/JAG.Capsule.Tiled.targets` serves only a game that builds against this clone, and it references the library from the project that references `JAG.Capsule.Build`, the game's build project.

Fix a warning, or suppress it with the reason at the suppression site. Every commit stays publishable without studio-only context.

## Tests

Test the importer's contracts and failure modes over fixture maps. Test the build seam end to end through this repository's own test project and its build project. Do not test obvious implementation steps or chase coverage.
