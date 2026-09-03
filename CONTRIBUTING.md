# Contributing to Capsule Tiled

Capsule Tiled is developed for JAG Studios' games in public. Changes are accepted when they improve the Tiled import or its documentation. A wider Tiled subset needs a consuming game use case — open an issue before investing in a speculative feature — but what ships must stand on its own: complete and never knowingly suboptimal.

## Setup

Install the .NET SDK selected by [`global.json`](global.json). Then, once per clone:

```text
git config core.hooksPath .githooks
```

This is not optional: Git ignores `.githooks/` until it is configured, and an unconfigured clone commits straight past the hook without reporting anything.

[`.githooks/pre-commit`](.githooks/pre-commit) gates every commit on a locked restore, a build, the format check, and the tests.

## Build

The gates are the four commands in `.githooks/pre-commit`; CI in `.github/workflows/ci.yml` runs them against the pinned Capsule packages and against a source clone of the engine, and packs the module.

## Expectations

- A behavior change includes the test that would have caught its absence; a fix includes the test that would have caught the bug.
- Game-specific policy does not belong in the module, and neither does engine behavior — that goes to [capsule-engine](https://github.com/just-awesome-games/capsule-engine).
- Warnings are errors. A necessary suppression includes its reason at the suppression site.
- Pull requests stay focused and state breaking changes plainly. The module is pre-1.0, so its surface may change.

Additional repository rules are in [`AGENTS.md`](AGENTS.md).

By contributing, you agree that your contribution is licensed under the [MIT License](LICENSE).
