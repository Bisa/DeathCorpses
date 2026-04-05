# Contributing to DeathCorpses

Contributions are welcome! Whether it's code, bug reports, or art assets to replace AI generated textures, I appreciate the help.

## Licensing and the Developer Certificate of Origin (DCO)

This project is licensed under the [MIT License](LICENSE). By contributing, you agree that your contributions will be licensed under the same terms.

All contributions must be signed off under the [Developer Certificate of Origin (DCO)](DCO). This certifies that you have the right to submit the work and that you agree to license it under this project's MIT license.

### How to sign off

Add a `Signed-off-by` line to each of your commit messages:

```
Signed-off-by: Your Name <your.email@example.com>
```

Git can do this automatically with the `-s` flag:

```sh
git commit -s -m "your commit message"
```

The name and email must match your Git identity. Commits without a valid sign-off will not be accepted. The DCO check in CI is skipped for PRs opened by the repository owner.

### Finding commits that are not signed

This repository includes a `pre-push` hook in `.githooks/` that warns you about any commits missing a `Signed-off-by` line before they are pushed. If you use [direnv](https://direnv.net/), the hook is enabled automatically. Otherwise, enable it manually:

```sh
git config --local core.hooksPath .githooks
```

### Fixing commits that are missing sign-off

If you forgot `-s` on some commits, you can add sign-off retroactively:

**Latest commit only:**

```sh
git commit -s --amend
```

**Multiple commits (all commits on your branch):**

First, make sure you have this repo as a remote called `upstream`:

```sh
git remote add upstream https://github.com/bisa/DeathCorpses.git
git fetch upstream
```

Then rebase your branch onto it with sign-off:

```sh
git rebase --signoff upstream/master
```

This replays every commit on your branch and adds `Signed-off-by` to each one.

### What this means in practice

- **Code**: You wrote it (or it's from a compatibly licensed source) and you're granting it under MIT.
- **Art/assets**: You created the work yourself (or have explicit permission from the creator) and you're granting it under MIT. Do not submit assets you found online unless they are explicitly licensed in a way that permits redistribution under MIT.

## Submitting changes

1. Fork the repository and create a branch for your changes.
2. Make your changes with signed off commits.
3. Preview your PR locally:
   ```sh
   ./scripts/generate-pr.sh
   ```
4. Submit when ready:
   ```sh
   ./scripts/generate-pr.sh --submit
   ```
   The script generates the PR description from your commit history using [git-cliff](https://git-cliff.org/), and targets the upstream repository even when run from a fork.

   If you prefer to create the PR manually, open a pull request describing what you changed and why.

## Questions

If you're unsure whether a contribution is appropriate or have licensing questions, open an issue first.
