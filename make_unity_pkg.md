# make_unity_pkg

A PowerShell script that turns any mm_tools subdirectory into a `.unitypackage`
ready to import into Unity.

---

## Usage

Open a PowerShell terminal in `C:\Users\emmst\Documents\OPCD\mm_tools` and run:

```powershell
.\make_unity_pkg.ps1 <DirectoryName>
```

The `.unitypackage` is written into the same subdirectory and named after it:

```
<DirectoryName>\<directoryname>.unitypackage
```

### Examples

```powershell
.\make_unity_pkg.ps1 bird_flock
# → bird_flock\bird_flock.unitypackage

.\make_unity_pkg.ps1 SkyDome
# → SkyDome\skydome.unitypackage

.\make_unity_pkg.ps1 Fountain
# → Fountain\fountain.unitypackage

.\make_unity_pkg.ps1 moving_vehicles
# → moving_vehicles\moving_vehicles.unitypackage
```

---

## How package paths are determined

### Tools that have an `Assets/` subfolder

If `<Dir>/Assets/` exists the script uses it as the root and preserves the paths
inside it exactly. This covers tools already structured as Unity packages:

```
bird_flock/Assets/BirdFlock/Scripts/BirdPath.cs
                 └─────────────────────────────→  Assets/BirdFlock/Scripts/BirdPath.cs
```

### Tools that have no `Assets/` subfolder

All files are placed under `Assets/<Dir>/` in the package, preserving any
`Editor/`, `Scripts/`, or other subfolders that exist in source:

```
Fountain/Editor/FountainCreator.cs
         └────────────────────────→  Assets/Fountain/Editor/FountainCreator.cs

SkyDome/SkyDome.shader
        └──────────────→  Assets/SkyDome/SkyDome.shader
```

---

## What gets included

| Extension | Unity importer assigned |
|---|---|
| `.cs` | MonoImporter |
| `.shader` | ShaderImporter |
| `.json` | TextScriptImporter |
| `.png` / `.jpg` / `.jpeg` | TextureImporter |
| `.mp3` / `.wav` / `.ogg` | AudioImporter |
| `.fbx` | ModelImporter |
| Everything else (`.py`, `.md`, `.txt`, `.anim`, `.prefab`, `.mat` …) | DefaultImporter |

---

## What gets excluded automatically

| Rule | Examples skipped |
|---|---|
| Blender source files | `.blend`, `.blend1` |
| FBX embedded texture dirs | any folder ending in `.fbm` |
| Video source files | `.mp4`, `.mov`, `.avi` |
| Archives | `.zip`, `.unitypackage` |
| Unity meta files | `.meta` (Unity regenerates these on import) |
| Decomissioned files | any filename starting with `decom_` |
| Claude Code settings | `.claude/` directories, `settings.local.json` |
| Memory index | `MEMORY.md` |
| `package.json` | Causes Unity to treat the folder as a UPM embedded package, creating a duplicate `Assets/` folder alongside the real one |

---

## Updating an existing package

Just run the script again — it overwrites the `.unitypackage` in place.

```powershell
.\make_unity_pkg.ps1 bird_flock   # rebuilds bird_flock\bird_flock.unitypackage
```

---

## Notes

- **GUIDs are regenerated each run.** This is fine for fresh imports. If you
  re-import into a project that already has the package, Unity will treat the
  assets as new and may create duplicates. Either delete the old assets first,
  or export from inside Unity to preserve stable GUIDs.

- **Prefabs and materials** (`.prefab`, `.mat`, `.anim`) are included if present
  in the source folder, but their internal GUID cross-references will only resolve
  correctly if the referenced assets are also present in the target project.

- The script must be run from PowerShell, not Command Prompt. If execution policy
  blocks it, run once with:
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\make_unity_pkg.ps1 <Dir>
  ```
