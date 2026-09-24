# Agent rules

These rules cover judgments the build cannot enforce.

## Scope

This repository is one authoring module for Capsule. It turns Tiled maps into Capsule scene documents at build time and does nothing else. Engine behavior (the document format, validation, what a scene is) lives in [capsule-engine](https://github.com/just-awesome-games/capsule-engine) and is consumed here, never redefined. What lands is complete and never knowingly suboptimal. JAG's own games are the only considered consumers. Break the module's surface when a better design needs it, and migrate the consuming game in the same wave. Add no option a game has not asked for. The module reads Capsule's properties and defines none.

## Documentation

`README.md` is the whole user documentation: what the module is, the quick start, the Tiled subset and how to develop. It is declarative and current, with no changelog, migration note or forward reference. `PACKAGE.md` is the NuGet readme and carries absolute links only.

A comment states an invariant or a why. Delete walkthroughs, section labels and commentary addressed to reviewers.

## Boundaries

`Capsule.Tiled` is a process the build runs, packed unlisted under `tools/`. No game references the assembly, and its types are internal. The header of `build/JAG.Capsule.Tiled.targets` names every Capsule contract the module relies on.

Fix a warning, or suppress it with the reason at the suppression site. Every commit stays publishable without studio-only context.

## Tests

Test the importer's contracts and failure modes over fixture maps. Test the build seam end to end through this repository's own test project. Do not test obvious implementation steps or chase coverage.
