# Agent rules

These rules cover judgments the build cannot enforce.

## Scope

This repository is one authoring module for Capsule: it turns Tiled maps into Capsule scene documents at build time and nothing else. Engine behavior — the document format, validation, what a scene is — lives in [capsule-engine](https://github.com/just-awesome-games/capsule-engine) and is consumed here, never redefined. What lands must meet the bar of a high-class open-source tool: complete, never knowingly suboptimal, no half-built features. JAG's own games are the only considered consumers, so break the module's surface whenever the better design needs it and migrate the consuming game in the same wave. Do not add options no game has asked for; the module reads Capsule's properties and defines none of its own.

## Documentation

`README.md` is the whole user documentation: quick start, the Tiled subset, how the hook works, how to develop. Keep it declarative and current — no changelogs, migration notes, or forward references. `PACKAGE.md` is the NuGet readme and carries absolute links only.

Comments explain invariants and hazards the code cannot state. Delete walkthroughs, section labels, and commentary addressed to reviewers.

## Boundaries

`Capsule.Tiled` is a process the build runs, packed unlisted under `tools/`; no game references the assembly and nothing invokes it by hand. Every Capsule contract this module relies on — the seam, the properties, the document types — is named in `build/JAG.Capsule.Tiled.targets` and `README.md`, nowhere else.

Warnings are fixed or suppressed with the reason at the suppression site. Every commit must remain publishable without studio-only context.

## Tests

Test the importer's contracts and failure modes over fixture maps, and the build seam end to end through this repository's own test project. Do not test obvious implementation steps or target coverage mechanically.
