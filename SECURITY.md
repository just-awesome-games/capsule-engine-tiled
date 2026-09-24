# Security policy

Report a vulnerability privately through GitHub's security advisories for this repository, not in a public issue. Expect an acknowledgement within a week.

## Scope

Capsule Tiled is a build-time tool with no network stack, persistence layer or privileged installer. The scope is what it reads:

- **Map and tileset parsing.** `Capsule.Tiled` reads the `.tmj` and `.tsj` files of the game being built. Malformed input fails with an import error, never with memory corruption, an unbounded allocation or code execution.
- **Paths read at build time.** A map's tileset and image references resolve relative to the map and stay inside the asset source root the build passes. The tool refuses a reference that escapes that root.

The scene document format and everything after import are out of scope. They belong to [capsule-engine](https://github.com/just-awesome-games/capsule-engine/blob/main/SECURITY.md).
