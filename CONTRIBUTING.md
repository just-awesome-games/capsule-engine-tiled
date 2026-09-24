# Contributing to Capsule Tiled

Capsule Tiled is developed in public for JAG Studios' games. Changes that improve the Tiled import or its documentation are welcome. Open an issue before building a wider Tiled subset, and name the game that needs it.

Setup and the commit gates are under [Developing](README.md#developing) in the README.

## Expectations

- A behavior change includes the test that would have caught its absence. A fix includes the test that would have caught the bug.
- Game-specific policy does not belong in the module. Engine behavior belongs in [capsule-engine](https://github.com/just-awesome-games/capsule-engine).
- Warnings are errors. A necessary suppression states its reason at the suppression site.
- Pull requests stay focused and state breaking changes plainly. The module is pre-1.0 and its surface may change.

Additional repository rules are in [`AGENTS.md`](AGENTS.md).

By contributing, you agree that your contribution is licensed under the [MIT License](LICENSE).
