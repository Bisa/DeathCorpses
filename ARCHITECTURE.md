# Architecture

## Maintaining This Document

> **IMPORTANT — READ BEFORE MAKING ANY CHANGES TO THIS PROJECT.**
>
> This file is the authoritative architectural reference for the entire mod. Every future agent session starts here to understand the codebase. If this document falls out of sync with the code, future agents **will** make incorrect assumptions, introduce bugs, and duplicate work. Keeping this file accurate is not optional — it is as important as the code changes themselves.

**When you add, remove, or change any of the following, you must suggest updates to this file:**

- New or removed source files or directories (update Source Layout)
- Changes to the build pipeline or output structure (update Build Pipeline)
- New or modified ModSystem subclasses or lifecycle behavior (update Startup Sequence)
- Changes to death handling, corpse recovery, or obituary logic (update Data Flows)
- New or changed persistence formats, file paths, or migration registrations (update Persistence)
- New, removed, or renamed config properties (update Configuration System)
- New, removed, or renamed commands or changed signatures (update Command Reference)
- New entity or item classes (update Source Layout and relevant data flow sections)

Do not silently skip this step. If you are unsure whether a change warrants an update here, err on the side of suggesting one — the user can always decline. Propose the ARCHITECTURE.md edits alongside your code changes, not as a separate follow-up.

**If you are a Claude Code agent**, read `CLAUDE.md` before making any changes — it contains Claude-specific instructions, constraints, and workflow rules for this project.

## Overview

DeathCorpses is a Vintage Story mod that spawns lootable corpse entities when players die. It preserves the player's inventory in the corpse and creates a map waypoint for navigation. The mod ships as a single zip supporting both VS 1.21 (.NET 8) and VS 1.22+ (.NET 10) through a dual-target compilation strategy with a thin runtime loader.

## Source Layout

```
src/
├── Core.cs                      # ModSystem entry point: loads config, registers entities/items
├── Config.cs                    # All server-side settings with attribute-driven validation
├── ConfigMigrations.cs          # Config migration registrations
├── CorpseMigrations.cs          # Data migration registrations
├── Constants.cs                 # Shared constants (mod ID)
├── ModSystemRegistry.cs         # Static registry for inter-system lookup
├── modinfo.json                 # Mod metadata and version
├── modicon.png                  # Mod icon
├── deathcorpses.csproj          # Impl project (compiled for net8 and net10)
│
├── Loader/
│   ├── Loader.cs                # Bootstrap ModSystem: detects runtime, loads correct impl
│   ├── Loader.csproj            # Thin loader project (net8 only, embeds impl DLLs)
│   └── deps.json                # NuGet dependency lockfile for loader
│
├── Entities/
│   └── EntityPlayerCorpse.cs    # EntityAgent holding dead player's inventory
│
├── Items/
│   ├── ItemCorpseCompass.cs     # Scans for nearby corpses, emits particles toward them
│   ├── ItemObituaries.cs        # Base obituary item (unbound, stackable, tradeable)
│   ├── ItemObituariesBound.cs   # Bound to a specific corpse, shows directional hints
│   └── ItemObituariesFaded.cs   # Inert item after corpse was collected
│
├── Systems/
│   ├── DeathContentManager.cs   # Server-side: death events, corpse spawning, persistence
│   └── Commands.cs              # /dc command tree (corpse + config management)
│
├── Lib/
│   ├── Config/                  # Generic attribute-driven config framework
│   │   ├── ConfigManager.cs     #   ModSystem: discovers, loads, validates, syncs configs
│   │   ├── ConfigUtil.cs        #   Serialization, validation, type conversion
│   │   ├── ConfigMigrator.cs    #   Version-to-version migration runner
│   │   ├── ConfigMigrationRegistry.cs  # Global migration registry
│   │   └── *Attribute.cs        #   Config, Description, Range, Privileges, ClientOnly, etc.
│   ├── Data/                    # TreeAttribute migration framework
│   │   ├── DataMigrator.cs      #   Migration runner for binary entity data
│   │   └── DataMigrationRegistry.cs    # Global registry (string-keyed)
│   ├── Extensions/              # ApiExtensions, ModLoaderExtensions, ParsersExtensions, Vec3iExtensions
│   ├── UI/
│   │   └── HudCircleRenderer.cs # Progress circle overlay for corpse collection
│   └── Utils/                   # ChatUtil, WorldUtil, DarkColor
│
├── assets/deathcorpses/         # VS asset layout
│   ├── entities/                #   Corpse entity definition
│   ├── itemtypes/               #   Compass + obituary item definitions
│   ├── recipes/grid/            #   Compass crafting recipe
│   ├── shapes/item/             #   3D models
│   ├── textures/item/           #   Sprites
│   ├── lang/                    #   Translations (16 locales + universal/)
│   ├── patches/                 #   Config + trading patches
│   └── config/                  #   Conflicting mods list
│
└── deps/                        # NuGet dependency lockfiles (net8.0.json, net10.0.json)
```

## Build Pipeline

The build uses Nix Flakes. No local .NET SDK is required.

**Inputs:** `targets.json` defines per-framework game versions and hashes. `modinfo.json` provides the mod version.

```
targets.json + modinfo.json
        |
   flake.nix
        |
   +----+----+---------+
   |         |          |
implNet8  implNet10  loaderDll
(net8.0)  (net10.0)  (net8.0, embeds both impls)
   |         |          |
   +----+----+---------+
        |
   zip assembly
```

Three assemblies are compiled in parallel:

| Assembly | Project | Target | Output |
|---|---|---|---|
| Impl (net8) | `deathcorpses.csproj` | net8.0 / VS 1.21.6 | `deathcorpses-net8.dll` |
| Impl (net10) | `deathcorpses.csproj` | net10.0 / VS 1.22.0 | `deathcorpses-net10.dll` |
| Loader | `Loader/Loader.csproj` | net8.0 | `deathcorpses.dll` |

The loader embeds both impl DLLs as manifest resources. The final zip also includes `.bin` fallback copies (VS 1.22 may strip manifest resources when loading DLLs from bytes).

**Version patching:** The zip assembly step patches `modinfo.json` with the build version. For local builds (`nix build`), this is a `-dev.{revCount}` pre-release suffix compatible with the mod portal's version format (e.g. `2.6.0-dev.142`). The zip filename additionally includes `+g{shortHash}` build metadata for identification. Dirty trees use `dev.0` since `revCount` is unavailable. CI uses `nix build .#release-zip` which uses the base version from `modinfo.json` as-is.

**Mod portal version constraints:** The mod portal ([mods.vintagestory.at](https://mods.vintagestory.at)) validates the `version` field in `modinfo.json` against `/^(\d+)\.(\d+)\.(\d+)(?:-(dev|pre|rc)\.(\d+))?$/` ([source](https://github.com/anegostudios/vsmoddb/blob/master/lib/version.php)). Only three pre-release keywords are allowed (`dev`, `pre`, `rc`), each followed by a single integer (max 4095). Build metadata (`+...`), additional dot-separated identifiers, and arbitrary pre-release labels are all rejected. Any version written into `modinfo.json` must conform to this format or the upload will fail.

**Output:**
```
deathcorpses-{version}.zip
├── deathcorpses.dll           # Loader (the only DLL VS sees)
├── deathcorpses-net8.bin      # Fallback impl for .NET 8
├── deathcorpses-net10.bin     # Fallback impl for .NET 10
├── modinfo.json               # Version patched to match build version
├── assets/
└── README.md
```

## Loader Mechanism

The Loader exists because VS 1.22 moved to .NET 10 while VS 1.21 uses .NET 8. A single mod zip must work on both. The Loader is compiled as net8.0 (forward-compatible with .NET 10) and dynamically loads the correct impl at runtime.

**Loading strategies** (tried in order):
1. Embedded manifest resources inside the loader DLL
2. `.bin` files from the mod's source zip via `Mod.SourcePath`
3. `.bin` files from extracted folder via `Mod.SourcePath`

**System discovery:** `Loader.CreateSystems()` uses `assembly.GetTypes()` (not `GetExportedTypes()`) to find `internal` ModSystem subclasses, instantiates them via `Activator.CreateInstance()`, sets the `Mod` property via reflection, sorts by `ExecuteOrder()`, and registers each in `ModSystemRegistry`.

**Lifecycle delegation:** The Loader forwards all VS lifecycle calls to impl systems: `StartPre`, `Start`, `StartServerSide`, `StartClientSide`, `AssetsLoaded`, `AssetsFinalize`, `Dispose`.

### Critical Constraints

> **Violating any of these causes hard-to-diagnose failures:**
>
> 1. `deathcorpses.csproj` **must** have `<Compile Remove="Loader\**" />` — otherwise Loader ends up in the impl assembly, causing infinite recursion at startup.
>
> 2. Impl `ModSystem` subclasses **must** be `internal` — if public, VS discovers and double-instantiates them.
>
> 3. Impl systems **must** use `ModSystemRegistry.Get<T>()`, never `api.ModLoader.GetModSystem<T>()` — VS does not track dynamically loaded systems.
>
> 4. `Loader.csproj` **must** target net8.0 only — net8 assemblies run on both .NET 8 and .NET 10 runtimes.

## Startup Sequence

```
VS discovers deathcorpses.dll
  → instantiates Loader (the only public ModSystem)

Loader.StartPre()
  → LoadImpl() — detects Environment.Version.Major, loads correct assembly
  → CreateSystems() — finds internal ModSystem subclasses, registers in ModSystemRegistry
  → forwards StartPre() to each impl system

  ConfigManager.StartPre() [ExecuteOrder 0.001]
    → discovers [Config]-annotated types, loads/validates/saves configs

Loader.Start()
  Core.Start()
    → retrieves Config from ConfigManager
    → registers EntityPlayerCorpse and all item classes with VS
    → sets world config flags (CorpseCompassEnabled, ObituariesEnabled)

Loader.AssetsLoaded()
  Core.AssetsLoaded()
    → resolves CreateWaypoint Auto mode (checks for conflicting mods)

Loader.StartServerSide()
  DeathContentManager.StartServerSide()
    → hooks OnEntityDeath and PlayerJoin events
    → builds _knownCorpseIds cache from disk
  Commands.StartServerSide()
    → registers /dc command tree

Loader.StartClientSide()
  ConfigManager.StartClientSide()
    → registers network channel for receiving server config sync
```

## Data Flows

### Death to Corpse Creation

```
Player dies
  → OnEntityDeath(entity, damageSource) → OnPlayerDeath(player, damageSource)
  → capture DeathRecapData (if DeathRecapDetail != None)
  → schedule recap delivery callbacks (3s optimistic, 30s persist-to-disk fallback)
  → check keepContents flag (skip if true)
  → CreateCorpseEntity()
      copy player UID, name, timestamp
      create InventoryGeneric
      TakeContentFromPlayer() — extracts slots per SaveInventoryTypes config
      DropArmorOnDeath handling (Vanilla/Armor/ArmorAndCloth modes)
      find floor below death position
  → if inventory empty: skip
  → if RandomCorpse:
      RandomizeCorpsePosition() — async chunk load, safety checks, 10 retries
      SaveDeathContent() early (before entity spawn)
  → FinalizeCorpse()
      create waypoint (if CreateWaypoint != None)
      SaveDeathContent() (or update position if already saved)
      spawn EntityPlayerCorpse (if CreateCorpse enabled)
      OR drop items on ground (if CreateCorpse disabled)
```

**Persistence path:** `ModData/{WorldId}/deathcorpses/{PlayerUID}/inventory-{timestamp}.dat`

### Corpse Recovery

Three recovery paths:

**Direct collection** (player right-clicks corpse):
- Hold right-click for `CorpseCollectionTime` seconds (default 1s)
- Ownership check: owner, creative mode, or `IsFree` (after `FreeCorpseAfterTime` hours)
- `Collect()` → transfers items to player (overflow drops on ground)
- Deletes disk save, removes waypoint (if configured), despawns entity

**Admin command** (`/dc corpse get`):
- `LoadLastDeathContent()` reconstructs inventory from `.dat` file
- `TryGiveItemstack()` to target player
- Optionally despawns entity (loads chunk if needed) and deletes save file

**Compass navigation** (`ItemCorpseCompass`):
- Server-side scans 3-chunk radius for `EntityPlayerCorpse` entities
- Filters by owner UID (or all in creative mode)
- Stores nearest position in itemstack attributes
- Client emits particle trail toward corpse

### Obituary System

```
Unbound obituary (stackable, tradeable)
  → right-click: TryBind()
  → queries DeathContentManager.GetRandomCorpseFromDisk()
  → creates bound obituary with randomized hint position (within ObituariesHintRadius)

Bound obituary
  → emits particles toward hint position
  → on interact: refreshes if entity loaded, shows "distant" if unloaded, converts to faded if gone

Faded obituary
  → inert flavor item
```

### Death Recap

On death, `DeathContentManager` captures damage source metadata into a `DeathRecapData` record (stored in-memory keyed by player UID). On respawn, a chat message is sent with detail controlled by `DeathRecapDetail`:

- **Cause** — killer name + damage type (always shown)
- **Coordinate** — adds death coordinates (always the death point, never the corpse's randomized position)
- **Distance** — adds distance from respawn point to corpse (omitted when no corpse exists)

**Delivery flow:**
1. 3s callback after death: send if player is alive; no-op otherwise
2. 30s callback: if still undelivered, persist to `death-recap.dat` and free memory
3. On next `PlayerJoin`: load from disk and deliver

**Persistence path:** `ModData/{WorldId}/deathcorpses/{PlayerUID}/death-recap.dat` — same directory as corpse saves, deleted after delivery. Versioned with `DataMigrationRegistry` (key: `recap`, current version: 1).

## Persistence

### Entity Persistence (VS-managed)

`EntityPlayerCorpse` serializes via `ToBytes()`/`FromBytes()`. Inventory is packed into `WatchedAttributes`. VS saves and loads entities with chunks automatically.

Key attributes stored: `ownerUID`, `ownerName`, `creationTime`, `creationRealDatetime`, `waypointID`, `corpseId`.

### Disk Save Files (mod-managed)

Corpse inventories are independently persisted to disk as `.dat` files.

- **Path:** `ModData/{WorldId}/deathcorpses/{PlayerUID}/inventory-{timestamp}.dat`
- **Format:** VS `TreeAttribute` binary serialization
- **Fields:** `version`, `graveX/Y/Z`, `corpseId`, plus `InventoryGeneric.ToTreeAttributes()` output
- **Retention:** `MaxCorpsesSavedPerPlayer` (default 10) — oldest files deleted when exceeded
- **Cache:** `_knownCorpseIds` HashSet built at startup, updated on save/delete — minimizes disk I/O for existence checks

**Why dual persistence?** Disk saves survive entity despawn (chunk unload) and provide admin recovery via `/dc corpse get` even when the corpse entity is not loaded.

### Versioned Migration

Two parallel migration systems for schema evolution:

- **Config migrations** (`Lib/Config/ConfigMigrator.cs`): operate on `Dictionary<string, JToken>`, registered via `ConfigMigrationRegistry.Register<TConfig>(fromVersion, transform)`. Version tracked in the `[Config]` attribute.
- **Data migrations** (`Lib/Data/DataMigrator.cs`): operate on `TreeAttribute`, registered via `DataMigrationRegistry.Register(key, fromVersion, transform)`. Version stored as `version` int in the tree.

Both run migrations sequentially from loaded version to current version.

## Configuration System

`Config.cs` defines all settings as public properties with attribute-driven validation:

| Attribute | Purpose |
|---|---|
| `[Config("filename.json")]` | Marks class as a config, sets filename |
| `[Description("...")]` | Human-readable help text, saved in JSON |
| `[Range(min, max)]` | Numeric bounds with automatic clamping |
| `[Privileges]` | Validates VS privilege codes |
| `[ClientOnly]` | Excluded from server→client sync |
| `[ConfigIgnore]` | Excluded from serialization |

**Load/save cycle:** `ConfigManager.StartPre()` discovers all `[Config]` types → deserializes JSON → applies migrations → fills properties (missing get defaults) → validates → saves back with metadata (defaults, descriptions, constraints).

**Saved JSON format** (enriched, not just values):
```json
{
  "PropertyName": {
    "Value": 42,
    "Default": 50,
    "Description": "...",
    "Limits": "0 to 100"
  }
}
```

**Network sync:** On player join, server serializes config (excluding `[ClientOnly]` properties) and broadcasts via ProtoBuf channel. `MarkConfigDirty()` triggers validate + save + broadcast.

**Runtime changes:** `/dc config set <option> <value>` → reflection-based property update → `MarkConfigDirty()`.

## Command Reference

All commands require the privilege set in `Config.CommandPrivilege` (default: `gamemode`).

```
/dc corpse list <player>                         # List saved corpse files
/dc corpse get <player> <give-to> [id]           # Restore inventory to a player
/dc corpse remove <player> [id]                  # Delete corpse(s) — id=-1 removes all
/dc corpse tp <player> [id]                      # Teleport caller to corpse
/dc corpse tpother <target> <owner> [id]         # Teleport another player to corpse
/dc corpse fetch <player> [id]                   # Teleport corpse to caller
/dc corpse fetchto <player> <x> <y> <z> [id]    # Teleport corpse to coordinates
/dc recap clear                                  # Remove all pending death recaps (memory + disk)
/dc config list                                  # List all config options
/dc config get <option>                          # Show a config value
/dc config set <option> <value>                  # Change a config value at runtime
```

## Project Conventions

- **Conventional Commits** — all commit messages must follow `<type>[scope]: <description>`. Allowed types: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, `revert`. A local hook (`.githooks/commit-msg`) and CI workflow (`.github/workflows/commits.yml`) enforce this. Changelogs are generated from these commits using `git-cliff` (`cliff.toml`).
- **Semantic Versioning** — the mod version in `src/modinfo.json` follows semver (`MAJOR.MINOR.PATCH`). Bump it as part of any code change: **MINOR** for new features or backwards-compatible additions (`feat`), **PATCH** for bug fixes (`fix`). MAJOR bumps are reserved for breaking changes (config renames, removed commands, save format incompatibilities). Propose the version bump alongside your code changes, not as a follow-up.
- **Update README.md** when adding or removing user-facing functionality — especially admin commands and configuration settings.

## Common Modification Scenarios

**Adding a config property:**
1. Add property to `Config.cs` with default value and optional `[Description]`/`[Range]` attributes
2. If the change requires migrating existing configs, bump `Version` in `[Config]` and register a migration in `ConfigMigrationRegistry`
3. Update README.md

**Adding an admin command:**
1. Add subcommand in `Commands.cs` using the existing fluent `.BeginSubCommand()` pattern
2. Create handler method following existing conventions
3. Update README.md

**Adding a new item type:**
1. Create item class in `src/Items/`
2. Register in `Core.Start()` via `api.RegisterItemClass()`
3. Add VS asset files: `assets/deathcorpses/itemtypes/`, shapes, textures, recipes, lang entries

**Changing corpse spawn behavior:**
Modify `DeathContentManager.OnPlayerDeath()` or `RandomizeCorpsePosition()`. The `FinalizeCorpse()` method handles waypoint creation and entity spawning.

**Updating VS version targets:**
1. Edit `targets.json` with new game version and hash
2. Run `nix build .#fetch-deps-netX && ./result ./src/deps/netX.json` to update NuGet lockfile

## Releasing

A single workflow `.github/workflows/release.yml` handles both release types. CI uses `nix build .#release-zip` which takes the version from `modinfo.json` as-is (CI patches it for prereleases before building).

- **Stable release**: push a tag matching the version in `src/modinfo.json`. Bump the version in `src/modinfo.json` before tagging.
- **Pre-release**: trigger manually via `workflow_dispatch`. Version is auto-patched to `{base}-rc.{run_number}`.

Local builds (`nix build .#zip` or just `nix build`) always produce a dev-suffixed version to distinguish them from tagged releases.
