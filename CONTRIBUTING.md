# Contributing

This repo holds the OPCD community tooling (`chickenandribs_opcd_tools`) —
Unity packages and Blender addons for building GSPro courses with OPCD.

## Branches

- **`main`** — the stable, released version of each tool. This is what
  you should clone/pull from day to day.
- **`dev`** — active development. Finished work lands here first and gets
  tested before merging to `main`.

## Releases

Each release is a git tag on `main` (`v1.2.0`). Pre-release/beta versions
are tagged with a suffix (`v1.2.0-beta.1`) and marked as a GitHub
pre-release — visible, but clearly labeled not-yet-stable. The built
`.unitypackage` for a release is attached to its GitHub Release as a
downloadable file; `.unitypackage` files are not tracked in git itself
(see `.gitignore`) — only source is.

## Commit messages

Format: `type(scope): summary`, where `scope` is the tool's folder name.

```
fix(trigger_formations): Done state no longer restarts the move clip
feat(moving_vehicles): add WaypointStop pause duration
docs(meshgapdetector): add troubleshooting section
```

Common types: `feat`, `fix`, `docs`, `refactor`, `chore`.

Keep commits small and frequent rather than large and batched — a commit
should represent one coherent change, with a message short enough to read
in one line.

## README format

Every tool's `README.md` follows the same shape:

1. **Description** — what it does, what's included.
2. **Installation**
3. **Step-by-Step Instructions**
4. **Troubleshooting**
5. **Feature Requests / Bug Reports** — point to this repo's Issues.
6. **Video** (optional) — links to any walkthrough videos.

## What doesn't go in this repo

- Built `.unitypackage` files (build output — see Releases above).
- Per-tool `MEMORY.md` dev notes (internal working notes, not for public
  consumption).
- Anything not yet ready for public use — keep that local, or in a
  separate private repo, until it's ready.

## Bug reports / feature requests

Please open an issue in this repository.
