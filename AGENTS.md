# Agent rules

These rules cover judgments the build cannot enforce.

## Scope

This repository is one authoring module for Capsule. It turns Tiled maps into Capsule scene documents at build time and does nothing else. Engine behavior (the document format, validation, what a scene is) lives in [capsule-engine](https://github.com/just-awesome-games/capsule-engine) and is consumed here, never redefined. What lands is complete and never knowingly suboptimal. JAG's own games are the only considered consumers. Break the module's surface when a better design needs it, and migrate the consuming game in the same wave. A removed field or property is deleted outright. No code stays behind to point at its replacement. Add no option a game has not asked for. The module reads Capsule's properties and defines none.

## Where code goes

`Program` owns the command line and the batch. `TiledImporter` turns one map into one scene document.

- A map property is a scene setting in `TiledImporter`.
- A tile property belongs to the tile definition in `TilesetImporter`.
- A layer or object property goes in `LayerImporter`. A new Tiled layer type is a new case in its dispatch and a new method beside the others.
- A new property value type is a new read on `TiledProperties`.

Each addition lands with its README table row and one test in the matching test class: `MapImportTests`, `TilesetImportTests` or `LayerImportTests`.

## Documentation

`README.md` is the whole user documentation: what the module is, the quick start, the Tiled subset and how to develop. It is declarative and current, with no changelog, migration note or forward reference. `PACKAGE.md` is the NuGet readme and carries absolute links only.

A comment states an invariant or a why. Delete walkthroughs, section labels and commentary addressed to reviewers.

## Boundaries

`Capsule.Tiled` is a process the build runs, packed unlisted under `tools/`. No game references the assembly, and its types are internal. The header of `build/JAG.Capsule.Tiled.targets` names every Capsule contract the module relies on.

Fix a warning, or suppress it with the reason at the suppression site. Every commit stays publishable without studio-only context.

## Tests

Test the importer's contracts and failure modes over fixture maps. Test the build seam end to end through this repository's own test project. Do not test obvious implementation steps or chase coverage.
