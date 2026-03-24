# Death Corpses

A [Vintage Story](https://www.vintagestory.at/) mod 

- **Mod page:** https://mods.vintagestory.at/deathcorpses
- **Authors:** DArkHekRoMaNT (original mod), Marzz2006 (vs 1.21 adaption), Bisa
- **Maintainers:** Bisa

---

# Credits

Forked from the adapted [PlayerGrave](https://mods.vintagestory.at/show/mod/39449) mod, which itself was based on the original mod [Player Corpse](https://mods.vintagestory.at/playercorpse).

---

## Requirements

- Vintage Story `>= 1.21.6` (.NET 8) or `>= 1.22.0` (.NET 10)

A single zip works on both versions — the mod auto-detects the runtime at startup.

---

## Usage

### How it works

When you die with items in your inventory, a corpse is spawned at your feet containing all of your items. A waypoint is added to your map marking the corpse location.

Right-click and hold on the corpse to collect your items once you have returned to the location of your death.

Once collected (or destroyed), the waypoint is optionally (on by default) removed.

A **Corpse Compass** item can be crafted to help locate nearby corpses when you're unsure of their exact position.

### Commands

All commands require the privilege configured in `CommandPrivilege` (default: `gamemode`).

| Command | Description |
|---|---|
| `/dc corpse list <player>` | Lists all saved corpses for a player, numbered by date |
| `/dc corpse get <player> <give to player> [id]` | Restores a corpse's inventory to a player. `id` is the index from `list` (default: `0`, most recent) |
| `/dc corpse remove <player> [id]` | Removes corpses for a player. If `id` is given, only that saved corpse is removed. Without `id`, deletes all saved corpses and despawns any corpse entities in the world |
| `/dc corpse tp <player> [id]` | Teleports you to a player's corpse. `id` is the index from `list` (default: `0`, most recent) |
| `/dc corpse tpother <target player> <corpse owner> [id]` | Teleports another player to a corpse. `id` is the index from `list` (default: `0`, most recent) |
| `/dc corpse fetch <player> [id]` | Moves a player's corpse entity to your current position. `id` is the index from `list` (default: `0`, most recent) |
| `/dc corpse fetchto <player> <x> <y> <z> [id]` | Moves a player's corpse entity to the specified coordinates. `id` is the index from `list` (default: `0`, most recent) |
| `/dc config list` | Shows all config options and their current values |
| `/dc config get <option>` | Shows the current value of a specific config option |
| `/dc config set <option> <value>` | Changes a config option at runtime and saves it to disk |

### Configuration

The config file is created at `ModConfig/deathcorpses.json` on first run. All settings are server-side.

| Setting | Default | Description |
|---|---|---|
| `CanFired` | `false` | Whether the corpse can burn in lava/fire (destroyed after ~15 seconds) |
| `HasHealth` | `false` | Whether the corpse has 100 HP and can be broken by other players |
| `CreateCorpse` | `true` | Whether a corpse entity is spawned at all. If false, items are dropped on the ground |
| `SaveInventoryTypes` | hotbar, backpack, crafting, cursor, character | Which inventory slots are saved into the corpse |
| `CommandPrivilege` | `gamemode` | Privilege required to use `/dc` commands |
| `MaxCorpsesSavedPerPlayer` | `10` | How many corpses to keep on disk per player (for `/dc corpse`) |
| `CreateWaypoint` | `Auto` | Whether to create a death waypoint. `Auto` disables it if another mod already handles death waypoints, `Always` forces it, `None` disables it |
| `WaypointIcon` | `bee` | Icon for the death waypoint. Options: `circle`, `bee`, `cave`, `home`, `ladder`, `pick`, `rocks`, `ruins`, `spiral`, `star1`, `star2`, `trader`, `vessel`, etc. |
| `WaypointColor` | `crimson` | Color for the death waypoint. Accepts .NET color names (see [99colors.net](https://www.99colors.net/dot-net-colors)) |
| `PinWaypoint` | `true` | Whether the death waypoint is pinned on the map |
| `DisableVanillaDeathWaypoint` | `true` | Suppresses the vanilla "You died here" tombstone waypoint |
| `RemoveWaypointOnCollect` | `true` | Removes the death waypoint when the corpse is collected |
| `FreeCorpseAfterTime` | `240` | In-game hours after which anyone can collect the corpse. `0` = always free, negative = never free |
| `CorpseCollectionTime` | `1` | Seconds of right-click hold required to collect the corpse |
| `CorpseCompassEnabled` | `true` | Enables the Corpse Compass item. Setting to `false` turns existing compasses into unknown items |
| `DropArmorOnDeath` | `Vanilla` | Controls armor drop behaviour. `Vanilla` = respect the game's own setting, `Armor` = always save armor into the corpse, `ArmorAndCloth` = save armor and clothing |
| `RemoveCorpseOnGet` | `true` | If true, `/dc corpse get` removes the corpse entity and save file after restoring the inventory |
| `DebugMode` | `false` | Broadcasts internal corpse creation/collection messages to all players |

---

## Building

This repo uses [Nix flakes](https://nixos.wiki/wiki/Flakes) for fully reproducible builds. No local Vintage Story or .NET installation is required — Nix fetches everything.

### Prerequisites

- Nix with flakes enabled. If you don't have it: [install Nix](https://nixos.org/download) then add `experimental-features = nix-command flakes` to `~/.config/nix/nix.conf`.

### Build the mod zip

```sh
nix build .#zip
```

The zip contains a single `deathcorpses.dll` that embeds two compiled variants:

- `deathcorpses-net8.bin` — impl compiled against VS 1.21.6 (.NET 8)
- `deathcorpses-net10.bin` — impl compiled against VS 1.22.0 (.NET 10)

At startup the loader checks `Environment.Version.Major` and loads the matching variant from its own manifest resources. This avoids the `MissingFieldException` caused by `Entity.Pos` changing from a field in VS 1.21 to a property in VS 1.22, while keeping a single zip for all supported versions.

The resulting zip is at `./result` and is ready to drop into your `Mods/` folder or upload to the mod portal.

### Updating the (NuGet) Nix package lockfiles

`deps/net8.0.json` and `deps/net10.0.json` pin the NuGet packages fetched during each impl build. If you add, remove, or version-bump a `<PackageReference>` in the `.csproj`, regenerate both:

```sh
nix build .#fetch-deps-net8  && ./result ./deps/net8.0.json
nix build .#fetch-deps-net10 && ./result ./deps/net10.0.json
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

### Verifying prereleases

Prereleases (`-rc.N` versions) patch `modinfo.json` before building, so a plain `nix build` won't reproduce them. Use the included script to replicate the CI steps:

```sh
# Verify prerelease 2.1.0-rc.15 built from the main branch
./scripts/verify-prerelease.sh 15 main

# Then compare with the release artifact
gh release download 2.1.0-rc.15 --pattern '*.zip' --dir /tmp
sha256sum /tmp/deathcorpses-2.1.0-rc.15.zip
```

---

## Contributing

This project uses [Conventional Commits](https://www.conventionalcommits.org/). All commit messages must follow the format:

```
<type>[optional scope]: <description>
```

Allowed types: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, `revert`.

A local git hook validates messages before commit. It is configured automatically when entering the dev shell:

```sh
nix develop
```

If you don't use `nix develop`, enable it manually: `git config --local core.hooksPath .githooks`

PR commits are also validated in CI.

---

## Versioning

This project follows [Semantic Versioning](https://semver.org/) (`MAJOR.MINOR.PATCH`):

| Change | Version bump |
|---|---|
| Breaking save/config/API changes | `MAJOR` |
| New features, backwards-compatible | `MINOR` |
| Bug fixes | `PATCH` |

The version in `modinfo.json` is the single source of truth. It must be committed before tagging — the release CI enforces this by failing if the tag does not exactly match the version in `modinfo.json`.

Local dirty builds automatically append a short commit hash (e.g. `1.0.0+abc1234-dirty`) to distinguish them from clean releases.

---

## Releasing

1. Bump `"version"` in `modinfo.json`
2. Commit: `git commit -am "chore: release 1.0.1"`
3. Tag: `git tag 1.0.1`
4. Push: `git push origin main 1.0.1`

CI will build the zip, generate a changelog from conventional commits, and publish it as the GitHub release body. The build will fail if the tag does not match the version in `modinfo.json`.

To preview the changelog locally:

```sh
nix develop
./scripts/generate-changelog.sh              # stable
./scripts/generate-changelog.sh --prerelease # prerelease
```

### Release candidates

To publish a prerelease from any branch or commit without a tag, trigger the **Release** workflow manually:

```sh
gh workflow run release.yml -f ref=main
```

Or use the GitHub Actions UI. This produces a prerelease tagged `1.0.0-rc.42` (base version from `modinfo.json` + CI run number).

