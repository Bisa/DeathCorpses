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

- Vintage Story `>= 1.22.0`

---

## Usage

### How it works

When you die with items in your inventory, a grave is spawned at your feet containing all of your items. A waypoint is added to your map marking the grave location. 

Right-click and hold on the grave to collect your items once you have returned to the location of your death.

Once collected (or destroyed), the waypoint is optionally (on by default) removed.

A **Grave Compass** item can be crafted to help locate nearby graves when you're unsure of their exact position.

### Commands

All commands require the privilege configured in `NeedPrivilegeForReturnThings` (default: `gamemode`).

| Command | Description |
|---|---|
| `/returnthings list <player>` | Lists all saved death inventories for a player, numbered by date |
| `/returnthings get <player> <give to player> [id]` | Restores a saved death inventory to a player. `id` is the index from `list` (default: `0`, most recent) |

### Configuration

The config file is created at `ModConfig/deathcorpses.json` on first run. All settings are server-side.

| Setting | Default | Description |
|---|---|---|
| `CanFired` | `false` | Whether the grave can burn in lava/fire (destroyed after ~15 seconds) |
| `HasHealth` | `false` | Whether the grave has 100 HP and can be broken by other players |
| `CreateGrave` | `true` | Whether a grave entity is spawned at all. If false, items are dropped on the ground |
| `SaveInventoryTypes` | hotbar, backpack, crafting, cursor, character | Which inventory slots are saved into the grave |
| `NeedPrivilegeForReturnThings` | `gamemode` | Privilege required to use `/returnthings` |
| `MaxDeathContentSavedPerPlayer` | `10` | How many death inventories to keep on disk per player (for `/returnthings`) |
| `CreateWaypoint` | `Auto` | Whether to create a death waypoint. `Auto` disables it if another mod already handles death waypoints, `Always` forces it, `None` disables it |
| `WaypointIcon` | `bee` | Icon for the death waypoint. Options: `circle`, `bee`, `cave`, `home`, `ladder`, `pick`, `rocks`, `ruins`, `spiral`, `star1`, `star2`, `trader`, `vessel`, etc. |
| `WaypointColor` | `crimson` | Color for the death waypoint. Accepts .NET color names (see [99colors.net](https://www.99colors.net/dot-net-colors)) |
| `PinWaypoint` | `true` | Whether the death waypoint is pinned on the map |
| `DisableVanillaDeathWaypoint` | `true` | Suppresses the vanilla "You died here" tombstone waypoint |
| `RemoveWaypointOnCollect` | `true` | Removes the death waypoint when the grave is collected |
| `FreeGraveAfterTime` | `240` | In-game hours after which anyone can collect the grave. `0` = always free, negative = never free |
| `GraveCollectionTime` | `1` | Seconds of right-click hold required to collect the grave |
| `GraveCompassEnabled` | `true` | Enables the Grave Compass item. Setting to `false` turns existing compasses into unknown items |
| `DropArmorOnDeath` | `Vanilla` | Controls armor drop behaviour. `Vanilla` = respect the game's own setting, `Armor` = always save armor into the grave, `ArmorAndCloth` = save armor and clothing |
| `DebugMode` | `false` | Broadcasts internal grave creation/collection messages to all players |

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

