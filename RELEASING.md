# Releasing Capsule Tiled

A release promotes a batch worth shipping or serves a consumer that must bump. Day to day the
module and its consumers build from source at the engine's `main` head. CI's `packages` lane runs
on demand (`workflow_dispatch`) ahead of a release, never on every push.

A release is an annotated `v<major>.<minor>.<patch>` tag on `main`. Pushing the tag runs
`.github/workflows/packages.yml`, which builds and tests the module in package mode against the
pinned Capsule release, packs `JAG.Capsule.Tiled` at that version, and pushes it to NuGet.org.
Nothing else publishes.

## Publishing ownership

The nuget.org organization `JAG-Studios` owns every `JAG.Capsule*` package. It also owns the
trusted-publishing policy for this repository's `packages.yml`. The Actions variable `NUGET_USER`
names a member's nuget.org username, which is a user profile name and never the organization. A
member who takes over releases adds their own organization-owned policy.

## 1. Pull in a new Capsule release (skip if the pin is unchanged)

The `JAG.Capsule` references in `src/JAG.Capsule.Tiled/JAG.Capsule.Tiled.csproj` and
`tests/JAG.Capsule.Tiled.Tests/JAG.Capsule.Tiled.Tests.csproj` pin one exact engine version.

Wait until NuGet.org serves that version (HTTP 200). A 404 means it is still indexing:

```bash
ENGINE=0.x.y   # the JAG.Capsule version the csproj files pin
curl -s -o /dev/null -w "jag.capsule %{http_code}\n" "https://api.nuget.org/v3-flatcontainer/jag.capsule/$ENGINE/jag.capsule.$ENGINE.nupkg"
```

Regenerate the committed lock files against the new pin:

```bash
dotnet restore -p:CapsuleSourcePath= --force-evaluate
git diff --stat -- '*packages.lock.json'    # exactly src/JAG.Capsule.Tiled and tests/JAG.Capsule.Tiled.Tests
```

Fix whatever the new engine broke (a moved namespace, a changed document seam) in `src/` and
`tests/` before continuing.

## 2. Run the package-mode gates

CI's `packages` lane runs these. A source-mode restore, which the pre-commit hook runs, rewrites
`obj/project.assets.json`. Restore in package mode immediately before building in it.

```bash
dotnet restore -p:CapsuleSourcePath=
dotnet build --configuration Release --no-restore -p:CapsuleSourcePath=
dotnet format --verify-no-changes --no-restore
dotnet test --configuration Release --no-build -p:CapsuleSourcePath=
dotnet pack JAG.Capsule.Tiled.slnx --configuration Release --no-build --output artifacts/packages -p:CapsuleSourcePath=
```

Clear `artifacts/packages` first if a stale pack from an earlier run is there.

## 3. Commit and push

The pre-commit hook runs the source-mode gates (locked restore, build, format, test) against the
sibling engine clone named in `Directory.Build.local.props`.

```bash
git add -A
git commit -m "Pin Capsule $ENGINE"
git push origin main
gh run list --branch main --workflow ci.yml --limit 1     # wait for "success" on both lanes
gh run watch <run-id> --exit-status
```

## 4. Choose the version

```bash
git tag --sort=-v:refname | head -1      # the current release
```

Patch for a fix, minor for new authoring surface or a new engine pin, major for a change that
alters what an existing map derives to. A version pushed to NuGet.org can never be reused.

## 5. Tag and push

```bash
VERSION=0.x.y
git tag -a "v$VERSION" -m "Capsule Tiled $VERSION"
git push origin "v$VERSION"
```

If the push fails, delete the local tag (`git tag -d "v$VERSION"`) before retrying.

## 6. Validate the publish

```bash
gh run list --workflow packages.yml --limit 1
gh run watch <run-id> --exit-status
gh run view <run-id> --log | grep "Your package was pushed"
curl -s -o /dev/null -w "%{http_code}\n" "https://api.nuget.org/v3-flatcontainer/jag.capsule.tiled/$VERSION/jag.capsule.tiled.$VERSION.nupkg"
```

## 7. Move the consumers

A game consuming the package pins it in its `JAG.Capsule.Tiled` reference. Bump it beside the
game's `JAG.Capsule` reference and run `dotnet restore --force-evaluate` there.

## Undoing a mistake

- **Tag pushed, workflow failed:** fix `main`, then release the next patch. Delete the tag only
  if nothing was pushed (`gh run view <run-id> --log` shows no "Your package was pushed"):
  `git push origin ":refs/tags/v$VERSION" && git tag -d "v$VERSION"`.
- **Package published but broken:** unlist it on NuGet.org and release the next patch.
