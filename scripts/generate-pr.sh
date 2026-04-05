#!/usr/bin/env bash
# Generate a PR from the current branch using git-cliff for the description.
#
# Usage:
#   ./scripts/generate-pr.sh            # preview the PR title and body
#   ./scripts/generate-pr.sh --submit   # submit the PR
#
# Requires: git-cliff, gh
set -euo pipefail

SUBMIT=false
for arg in "$@"; do
  case "$arg" in
    --submit) SUBMIT=true ;;
  esac
done

UPSTREAM_REPO="Bisa/DeathCorpses"

branch=$(git symbolic-ref --short HEAD 2>/dev/null)
base="main"

if [ "$branch" = "$base" ] || [ "$branch" = "master" ]; then
  echo "Error: you are on '$branch'. Switch to a feature branch first." >&2
  exit 1
fi

# Determine the upstream remote (may be "origin" or "upstream" in a fork)
upstream_remote=""
for remote in $(git remote); do
  url=$(git remote get-url "$remote" 2>/dev/null || true)
  if echo "$url" | grep -qi "$UPSTREAM_REPO"; then
    upstream_remote="$remote"
    break
  fi
done

if [ -z "$upstream_remote" ]; then
  echo "Error: no remote pointing to $UPSTREAM_REPO found." >&2
  echo "  Add it with: git remote add upstream https://github.com/$UPSTREAM_REPO.git" >&2
  exit 1
fi

# Ensure we have the latest base branch ref
if ! git fetch "$upstream_remote" "$base" --quiet 2>&1; then
  echo "Error: failed to fetch '$base' from '$upstream_remote'." >&2
  exit 1
fi

merge_base=$(git merge-base "$upstream_remote/$base" HEAD)
commit_count=$(git rev-list --count "$merge_base"..HEAD)

if [ "$commit_count" -eq 0 ]; then
  echo "Error: no commits found since diverging from $base." >&2
  exit 1
fi

# Use first commit subject as PR title, or branch name if multiple commits
if [ "$commit_count" -eq 1 ]; then
  title=$(git log -1 --format='%s' HEAD)
else
  # Use branch name, replacing separators with spaces
  title=$(echo "$branch" | sed 's/[-_/]/ /g')
fi

# Generate changelog body for commits since diverging from base
if ! body=$(git-cliff "$merge_base"..HEAD --strip all 2>&1); then
  echo "Error: git-cliff failed:" >&2
  echo "$body" >&2
  exit 1
fi

echo "=== PR Preview ==="
echo ""
echo "Target: $UPSTREAM_REPO ($base)"
echo "Branch: $branch"
echo "Commits: $commit_count"
echo ""
echo "Title: $title"
echo ""
echo "--- Body ---"
echo "$body"
echo "---"

if [ "$SUBMIT" = "true" ]; then
  echo ""
  gh pr create --repo "$UPSTREAM_REPO" --base "$base" --title "$title" --body "$body"
else
  echo ""
  echo "Run with --submit to create the PR."
fi
