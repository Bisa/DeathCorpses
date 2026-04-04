#!/usr/bin/env nix-shell
#!nix-shell -i bash -p jq
set -euo pipefail

usage() {
  echo "Usage: $0 <mode> <args...>"
  echo ""
  echo "Reproduces a CI or local build in a clean worktree for hash verification."
  echo ""
  echo "Modes:"
  echo "  prerelease <run-number> [ref]   Reproduce a CI prerelease (-rc.N) build"
  echo "  dev <ref>                       Reproduce a local dev build"
  echo ""
  echo "Examples:"
  echo "  $0 prerelease 15 main"
  echo "  $0 dev abc1234"
  echo "  $0 dev main"
  exit 1
}

[ $# -lt 2 ] && usage

MODE="$1"
shift

WORK_DIR="/tmp/dc-verify"
REPO_ROOT="$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
BASE_VERSION="$(jq -r .version "$REPO_ROOT/src/modinfo.json")"
MODID="$(jq -r .modid "$REPO_ROOT/src/modinfo.json")"

# Clean up any previous run
rm -rf "$WORK_DIR"

case "$MODE" in
  prerelease)
    [ $# -lt 1 ] && usage
    RUN_NUMBER="$1"
    REF="${2:-HEAD}"
    VERSION="${BASE_VERSION}-rc.${RUN_NUMBER}"

    echo "Reproducing prerelease build: ${VERSION} from ref ${REF}"

    git -C "$REPO_ROOT" worktree add "$WORK_DIR" "$REF" --detach 2>/dev/null

    # Replicate CI: patch version and commit so nix sees a clean tree
    jq --arg v "$VERSION" '.version = $v' "$WORK_DIR/src/modinfo.json" > "$WORK_DIR/modinfo.tmp"
    mv "$WORK_DIR/modinfo.tmp" "$WORK_DIR/src/modinfo.json"
    git -C "$WORK_DIR" add src/modinfo.json
    git -C "$WORK_DIR" -c user.name="CI" -c user.email="ci@noreply" -c commit.gpgsign=false commit -m "prerelease $VERSION" --quiet

    echo "Building..."
    nix build "$WORK_DIR#release-zip" --out-link "$WORK_DIR/result"

    RESULT_PATH="$(readlink -f "$WORK_DIR/result")"
    git -C "$REPO_ROOT" worktree remove "$WORK_DIR" --force 2>/dev/null || true

    echo ""
    echo "Local build hash:"
    sha256sum "$RESULT_PATH"
    echo ""
    echo "Compare with the release artifact:"
    echo "  gh release download ${VERSION} --repo Bisa/DeathCorpses --pattern '*.zip' --dir /tmp"
    echo "  sha256sum /tmp/${MODID}-${VERSION}.zip"
    ;;

  dev)
    REF="$1"

    # Resolve the ref to a full hash to detect dev.0 (dirty/unreproducible) builds
    FULL_REV="$(git -C "$REPO_ROOT" rev-parse "$REF")"
    SHORT_REV="${FULL_REV:0:7}"
    REV_COUNT="$(git -C "$REPO_ROOT" rev-list --count "$FULL_REV")"
    VERSION="${BASE_VERSION}-dev.${REV_COUNT}"

    if [ "$REV_COUNT" -eq 0 ]; then
      echo "ERROR: revCount is 0 — this is likely an orphan commit or initial commit."
      echo "Cannot meaningfully verify this build."
      exit 1
    fi

    echo "Reproducing dev build: ${VERSION}+g${SHORT_REV} from ref ${REF}"

    git -C "$REPO_ROOT" worktree add "$WORK_DIR" "$FULL_REV" --detach 2>/dev/null

    echo "Building..."
    nix build "$WORK_DIR#zip" --out-link "$WORK_DIR/result"

    RESULT_PATH="$(readlink -f "$WORK_DIR/result")"
    git -C "$REPO_ROOT" worktree remove "$WORK_DIR" --force 2>/dev/null || true

    echo ""
    echo "Local build hash:"
    sha256sum "$RESULT_PATH"
    echo ""
    echo "Compare against your local dev build zip:"
    echo "  sha256sum /path/to/${MODID}-${VERSION}+g${SHORT_REV}.zip"
    echo ""
    echo "Note: dev.0 (dirty tree) builds are unreproducible — only clean builds can be verified."
    ;;

  *)
    echo "ERROR: Unknown mode '${MODE}'"
    usage
    ;;
esac
