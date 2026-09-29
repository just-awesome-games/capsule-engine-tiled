# Capsule Tiled

Capsule Tiled imports Tiled maps into [Capsule Engine](https://github.com/just-awesome-games/capsule-engine) scenes at build time. Reference `JAG.Capsule.Tiled` from the game's build project, beside its `JAG.Capsule.Build` reference, and add the importer in its `Program.cs` with `.AddImporter(new TiledImporter())`. Each `.tmj` map under the logic project's `Assets/` then becomes a scene document the game loads by name.

The [quick start](https://github.com/just-awesome-games/capsule-engine-tiled#quick-start) and the [Tiled subset](https://github.com/just-awesome-games/capsule-engine-tiled#tiled-subset) are in the repository README.

Capsule Tiled is licensed under the [MIT License](https://github.com/just-awesome-games/capsule-engine-tiled/blob/main/LICENSE).
