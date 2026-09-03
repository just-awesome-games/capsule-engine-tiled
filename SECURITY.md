# Security policy

Report a vulnerability privately through GitHub's security advisories for this repository rather than in a public issue. Expect an acknowledgement within a week.

## Scope

Capsule Tiled is a build-time tool. It has no network stack, no persistence layer and no privileged installer. What is in scope is what it does touch:

- **Map and tileset parsing.** `Capsule.Tiled` reads Tiled `.tmj` and `.tsj` files supplied by the game being built. Malformed input must fail with a `TiledImportException`, never with memory corruption, an unbounded allocation, or code execution.
- **Paths read at build time.** A map's tileset and image references are resolved relative to the map and confined to the asset-source root the build hands the tool. A reference that escapes that root must be refused, never followed.

Out of scope: the scene document format and everything after import, which belong to [capsule-engine](https://github.com/just-awesome-games/capsule-engine/blob/main/SECURITY.md).
