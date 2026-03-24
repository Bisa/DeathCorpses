#!/usr/bin/env nix-shell
#!nix-shell -i bash -p jq
set -euo pipefail

usage() {
  echo "Usage: $0 <run-number> [ref]"
  echo ""
  echo "Reproduces a CI prerelease build locally for hash verification."
  echo ""
  echo "  run-number  The GitHub Actions run number (shown in the release version as -rc.N)"
  echo "  ref         Branch or commit to build from (default: HEAD)"
  echo ""
  echo "Example:"
  echo "  $0 15 main"
  echo "  sha256sum /tmp/dc-verify/result"
  exit 1
}

[ $# -lt 1 ] && usage

RUN_NUMBER="$1"
REF="${2:-HEAD}"
WORK_DIR="/tmp/dc-verify"
REPO_ROOT="$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"

BASE_VERSION="$(jq -r .version "$REPO_ROOT/modinfo.json")"
VERSION="${BASE_VERSION}-rc.${RUN_NUMBER}"

echo "Reproducing prerelease build: ${VERSION} from ref ${REF}"

# Clean up any previous run
rm -rf "$WORK_DIR"
git -C "$REPO_ROOT" worktree add "$WORK_DIR" "$REF" --detach 2>/dev/null

# Replicate CI: patch version and commit so nix sees a clean tree
jq --arg v "$VERSION" '.version = $v' "$WORK_DIR/modinfo.json" > "$WORK_DIR/modinfo.tmp"
mv "$WORK_DIR/modinfo.tmp" "$WORK_DIR/modinfo.json"
git -C "$WORK_DIR" add modinfo.json
git -C "$WORK_DIR" -c user.name="CI" -c user.email="ci@noreply" -c commit.gpgsign=false commit -m "prerelease $VERSION" --quiet

echo "Building..."
nix build "$WORK_DIR#zip" --out-link "$WORK_DIR/result"

RESULT_PATH="$(readlink -f "$WORK_DIR/result")"

# Clean up worktree
git -C "$REPO_ROOT" worktree remove "$WORK_DIR" --force 2>/dev/null || true

echo ""
echo "Local build hash:"
sha256sum "$RESULT_PATH"
echo ""
echo "Compare with the release artifact:"
echo "  gh release download ${VERSION} --repo Bisa/DeathCorpses --pattern '*.zip' --dir /tmp"
echo "  sha256sum /tmp/deathcorpses-${VERSION}.zip"
