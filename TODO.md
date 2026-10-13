# TODO

## Known issues

- **`character_import_pipeline`** — confirmed to break unrelated FBX
  imports project-wide (forces Humanoid + External materials on anything
  under any `characters/<name>/` folder, not just its intended targets).
  See `character_import_pipeline/README.md` for the full writeup. Needs
  its folder-matching logic scoped more narrowly before it's safe to use.

## Process

- Set up `gh auth login` to enable GitHub Releases (built `.unitypackage`
  files attached as downloadable release assets) and turn items in this
  file into real GitHub Issues once there's something to reference.
