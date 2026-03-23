# Death Corpses

A [Vintage Story](https://www.vintagestory.at/) mod that spawns a persistent corpse entity at your death location containing all your items. The corpse is marked with a waypoint on your map (optionally removed when you collect it), and the vanilla "You died here" tombstone waypoint can be suppressed.

**Mod page:** https://mods.vintagestory.at/deathcorpses

**Authors:** DArkHekRoMaNT (original mod), Marzz2006 (vs 1.21 adaption), Bisa

---

## Requirements

- Vintage Story `>= 1.22.0`

---

## Building

This repo uses [Nix flakes](https://nixos.wiki/wiki/Flakes) for fully reproducible builds. No local Vintage Story or .NET installation is required — Nix fetches everything.

### Prerequisites

- Nix with flakes enabled. If you don't have it: [install Nix](https://nixos.org/download) then add `experimental-features = nix-command flakes` to `~/.config/nix/nix.conf`.

### Build the mod zip

```sh
nix build .#zip
```

The resulting zip is at `./result` and is ready to drop into your `Mods/` folder or upload to the mod portal.

### Updating the (NuGet) Nix package lockfile

`deps.json` pins the NuGet packages fetched during the build. If you add, remove, or version-bump a `<PackageReference>` in the `.csproj`, you need to regenerate deps.json as such:

```sh
nix build .#fetch-deps && ./result ./deps.json
```

---

## Verifying release artifacts

Every release zip is built by CI from a tagged commit using the same Nix flake as this repo. Because Nix locks every dependency by hash, the build is reproducible: building the same tag locally will produce a bit-for-bit identical zip.

To verify that a release artifact matches the source:

**1. Download the release zip from GitHub**

Find the zip attached to a release on the [releases page](https://github.com/Bisa/DeathCorpses/releases) and note its SHA-256 hash:

```sh
sha256sum deathcorpses-<version>.zip
```

**2. Checkout the corresponding tag and build it yourself**

```sh
git clone https://github.com/Bisa/DeathCorpses
cd DeathCorpses
git checkout <version>
nix build .#zip
sha256sum ./result
```

If both hashes match, the artifact on GitHub is exactly what was built from that commit — nothing was added, removed, or tampered with.

---

## Versioning

This project follows [Semantic Versioning](https://semver.org/) (`MAJOR.MINOR.PATCH`):

| Change | Version bump |
|---|---|
| Breaking save/config/API changes | `MAJOR` |
| New features, backwards-compatible | `MINOR` |
| Bug fixes | `PATCH` |

The version in `modinfo.json` is the single source of truth. It must be committed before tagging — the release CI enforces this by failing if the tag does not exactly match the version in `modinfo.json`.

Local dirty builds automatically append a short commit hash (e.g. `1.0.0+abc1234`) to distinguish them from clean releases.

---

## Releasing

1. Bump `"version"` in `modinfo.json`
2. Commit: `git commit -am "release 1.0.1"`
3. Tag: `git tag 1.0.1`
4. Push: `git push origin main 1.0.1`

CI will build the zip and publish a GitHub release automatically. The build will fail if the tag does not match the version in `modinfo.json`.

### Release candidates

To publish a prerelease from any branch or commit without a tag, trigger the **Prerelease** workflow manually:

```sh
gh workflow run prerelease.yml -f ref=main
```

Or use the GitHub Actions UI. This produces a prerelease tagged `1.0.0-rc.abc1234` (base version from `modinfo.json` + short commit hash).

