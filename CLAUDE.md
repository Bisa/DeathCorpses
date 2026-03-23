# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

DeathCorpses is a **Vintage Story** game mod (C# / .NET 10.0) that creates corpse entities when players die, preserving their inventory and adding map waypoints. It is a fork of the PlayerGrave mod.

The mod version is sourced from `modinfo.json` and injected at build time.

## Build Commands

This project uses **Nix Flakes** — no local .NET SDK is required.

```sh
# Build the mod zip (output in ./result)
nix build .#zip

# Update NuGet dependency lockfile
nix build .#fetch-deps && ./result ./deps.json
```

There is no test suite. To run the mod, copy `./result` to the Vintage Story `Mods/` folder and launch a server/client (v1.22.0+).

## Architecture

### Entry Point

`Core.cs` — `ModSystem` subclass that loads config, registers the `EntityPlayerCorpse` entity and `ItemCorpseCompass` item.

### Key Systems

- **`Systems/DeathContentManager.cs`** — Server-side `ModSystem` that intercepts `OnEntityDeath()` to spawn a corpse entity, create a map waypoint, and save the death inventory for later recovery. Also suppresses vanilla death waypoints on player join if configured.
- **`Systems/Commands.cs`** — Registers the `/dc` root command. Subcommand `corpse` (`list|get|remove`) manages saved corpses.

### Key Entities & Items

- **`Entities/EntityPlayerCorpse.cs`** — Custom `EntityAgent` that holds the dead player's inventory. Tracks owner UID, creation time, and associated waypoint ID. Handles timed-interaction collection and "free corpse" logic (available to anyone after a configurable duration).
- **`Items/ItemCorpseCompass.cs`** — Held item that scans a 3-block radius for nearby `EntityPlayerCorpse` entities and renders visual HUD indicators via `Lib/UI/HudCircleRenderer.cs`.

### Configuration

- **`Config.cs`** — Defines all server-side settings (corpse fire/health, waypoint icon/color/pinning, armor drop behavior, debug mode). Serialized to `ModConfig/deathcorpses.json` in the world save.
- **`Lib/Config/`** — Generic attribute-driven config manager used to load/save/validate the config.

### Lib Utilities

`Lib/` contains reusable helpers: API extensions (`Lib/Extensions/`), HUD rendering (`Lib/UI/`), and world/color/chat utilities (`Lib/Utils/`). These have no mod-specific logic and can be used across systems.

### Assets

`assets/` follows Vintage Story's asset layout: entity types, item types, recipes, shapes (JSON), textures, and language files (18 locales under `assets/deathcorpses/lang/`).

## Self-Maintenance

When the user gives feedback or instructions that sound like they should apply "going forward" (e.g. preferences, conventions, recurring patterns), suggest updating this file to capture that guidance. Ask before editing.

## Conventions

- **Always update README.md** when adding or removing functionality/features — especially user-facing admin commands and configuration settings.

## Releasing

Releases are tag-triggered via `.github/workflows/release.yml`. Pre-releases use the manual `prerelease.yml` workflow. Bump the version in `modinfo.json` before tagging.
