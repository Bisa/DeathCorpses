# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Start Here

Read [ARCHITECTURE.md](ARCHITECTURE.md) first. It is the authoritative reference for how this project is structured — source layout, build pipeline, loader mechanism, data flows, persistence, configuration, commands, conventions, and releasing.

## Build Commands

This project uses **Nix Flakes** — no local .NET SDK is required.

```sh
# Build the mod zip (output in ./result)
nix build .#zip

# Update NuGet dependency lockfiles
nix build .#fetch-deps-net8  && ./result ./src/deps/net8.0.json
nix build .#fetch-deps-net10 && ./result ./src/deps/net10.0.json
```

There is no test suite.

## Rules

- **Always update ARCHITECTURE.md** when making code changes that affect the mod's structure, data flows, commands, config properties, persistence formats, or build pipeline. Read the "Maintaining This Document" section at the top of ARCHITECTURE.md for the full list of triggers. Propose these edits alongside your code changes, not as a follow-up.

## Self-Maintenance

When the user gives feedback or instructions that sound like they should apply "going forward" (e.g. preferences, conventions, recurring patterns), suggest updating this file to capture that guidance. Ask before editing.
