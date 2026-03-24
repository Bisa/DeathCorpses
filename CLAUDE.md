# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

DeathCorpses is a **Vintage Story** game mod (C#) that creates corpse entities when players die, preserving their inventory and adding map waypoints. It is a fork of the PlayerGrave mod.

The mod ships as a **single zip** supporting both VS 1.21 (net8.0) and VS 1.22+ (net10.0). The mod version is sourced from `modinfo.json` and injected at build time.

## Build Commands

This project uses **Nix Flakes** — no local .NET SDK is required.

```sh
# Build the mod zip (output in ./result)
nix build .#zip

# Update NuGet dependency lockfiles
nix build .#fetch-deps-net8  && ./result ./deps/net8.0.json
nix build .#fetch-deps-net10 && ./result ./deps/net10.0.json
```

There is no test suite. To run the mod, copy `./result` to the Vintage Story `Mods/` folder and launch a server/client (v1.21.6+ or v1.22.0+).

## Architecture

### Loader (`Loader/`)

`Loader/Loader.cs` is the **only** `ModSystem` that VS sees. It is compiled as a net8.0 DLL (`deathcorpses.dll`) and acts as a bootstrap: at `StartPre` it detects the runtime version and loads the correct impl assembly (`deathcorpses-net8.bin` or `deathcorpses-net10.bin`). It then discovers all `ModSystem` subclasses in the impl and forwards every lifecycle call (`Start`, `StartServerSide`, etc.) to them.

**Loading strategies** (tried in order):
1. Embedded manifest resources inside the loader DLL
2. Fallback: read `.bin` files from the mod's source zip/folder via `Mod.SourcePath` (VS 1.22 strips manifest resources when loading mod DLLs from bytes)

**Important constraints:**
- `deathcorpses.csproj` must have `<Compile Remove="Loader\**" />` — otherwise the Loader class ends up in the impl assembly and causes infinite recursion.
- Impl `ModSystem` subclasses must be `internal` to prevent VS from double-instantiating them.
- Impl systems must use `ModSystemRegistry.Get<T>()` (not `api.ModLoader.GetModSystem<T>()`) to find each other, since VS doesn't know about them.

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

A single workflow `.github/workflows/release.yml` handles both release types. It always produces one zip via `nix build .#zip`.

- **Stable release**: push a tag matching the version in `modinfo.json`. Bump the version in `modinfo.json` before tagging.
- **Pre-release**: trigger manually via `workflow_dispatch`. Version is auto-patched to `{base}-rc.{run_number}`.
