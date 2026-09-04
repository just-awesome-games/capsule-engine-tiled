# Releasing Capsule Tiled

A release is an annotated `v<major>.<minor>.<patch>` tag on `main`. Pushing the tag runs
`.github/workflows/packages.yml`, which builds and tests the module in package mode against the
pinned Capsule release, packs `JAG.Capsule.Tiled` at that version, and pushes it to NuGet.org.
Nothing else publishes.

## 1. Pull in a new Capsule release (skip if the pin is unchanged)

The module pins one exact engine version in `Directory.Build.props` — `<CapsuleVersion>`.

Wait until NuGet.org serves that version (HTTP 200; 404 means still indexing):

```bash
for p in jag.capsule jag.capsule.build; do
  curl -s -o /dev/null -w "$p %{http_code}\n" "https://api.nuget.org/v3-flatcontainer/$p/0.6.0/$p.0.6.0.nupkg"
done
```

Regenerate the committed lock files against the new pin:

```bash
dotnet restore -p:CapsuleUsePackages=true --force-evaluate
git diff --stat -- '*packages.lock.json'    # exactly src/Capsule.Tiled and tests/Capsule.Tiled.Tests
```

Fix whatever the new engine broke (a moved namespace, a changed document seam) in `src/` and
`tests/` before continuing.

## 2. Run the package-mode gates

These are what CI's `packages` lane runs. A source-mode restore (which the pre-commit hook runs)
rewrites `obj/project.assets.json`, so always restore in package mode immediately before building
in it.

```bash
dotnet restore -p:CapsuleUsePackages=true
dotnet build --configuration Release --no-restore -p:CapsuleUsePackages=true
dotnet format --verify-no-changes --no-restore
dotnet test --configuration Release --no-build -p:CapsuleUsePackages=true
dotnet pack Capsule.Tiled.slnx --configuration Release --no-build --output artifacts/packages -p:CapsuleUsePackages=true
```

Clear `artifacts/packages` first if a stale pack from an earlier run is there.

## 3. Commit and push

The pre-commit hook runs the source-mode gates (locked restore, build, format, test) against the
sibling engine clone named in `Directory.Build.local.props`.

```bash
git add -A
git commit -m "Pin Capsule 0.6.0"
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
git tag -a v0.2.0 -m "Capsule Tiled 0.2.0"
git push origin v0.2.0
```

If the push fails, delete the local tag (`git tag -d v0.2.0`) before retrying.

## 6. Validate the publish

```bash
gh run list --workflow packages.yml --limit 1
gh run watch <run-id> --exit-status
gh run view <run-id> --log | grep "Your package was pushed"
curl -s -o /dev/null -w "%{http_code}\n" https://api.nuget.org/v3-flatcontainer/jag.capsule.tiled/0.2.0/jag.capsule.tiled.0.2.0.nupkg
```

## 7. Move the consumers

A game consuming the package pins `CapsuleTiledVersion` in its `Directory.Build.props`; bump it
beside `CapsuleVersion` and `dotnet restore --force-evaluate` there.

## Undoing a mistake

- **Tag pushed, workflow failed:** fix `main`, then release the next patch. Delete the tag only
  if nothing was pushed (`gh run view <run-id> --log` shows no "Your package was pushed"):
  `git push origin :refs/tags/v0.2.0 && git tag -d v0.2.0`.
- **Package published but broken:** unlist it on NuGet.org and release the next patch.
