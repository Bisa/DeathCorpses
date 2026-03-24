#!/usr/bin/env bash
# Generate RELEASE_NOTES.md and CHANGELOG.md using git-cliff.
#
# Usage:
#   ./scripts/generate-changelog.sh                  # stable: version from modinfo.json
#   ./scripts/generate-changelog.sh --prerelease     # prerelease: uses latest stable tag as base
#
# Requires: git-cliff, jq
set -euo pipefail

PRERELEASE=false
if [ "${1:-}" = "--prerelease" ]; then
  PRERELEASE=true
fi

VERSION=$(jq -r .version src/modinfo.json)

# Find the previous stable tag (exclude rc tags)
prev_stable_for_release() {
  git tag --sort=-v:refname | grep -v '.*-rc\.' | grep -v "^${VERSION}$" | head -1 || true
}

prev_stable_any() {
  git tag --sort=-v:refname | grep -v '.*-rc\.' | head -1 || true
}

if [ "$PRERELEASE" = "true" ]; then
  PREV_TAG=$(prev_stable_any)
else
  PREV_TAG=$(prev_stable_for_release)
fi

# Release notes (just this release's commits)
if [ -n "$PREV_TAG" ]; then
  echo "Generating release notes for commits since $PREV_TAG..."
  git-cliff "$PREV_TAG"..HEAD -o RELEASE_NOTES.md
else
  echo "No previous tag found, generating release notes from all commits..."
  git-cliff -o RELEASE_NOTES.md
fi

# Full cumulative changelog
echo "Generating full CHANGELOG.md..."
git-cliff -o CHANGELOG.md

echo ""
echo "=== RELEASE_NOTES.md ==="
cat RELEASE_NOTES.md
echo ""
echo "=== CHANGELOG.md updated ==="
echo "$(wc -l < CHANGELOG.md) lines"
