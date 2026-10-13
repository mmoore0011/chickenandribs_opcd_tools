# Character Import Pipeline

## ⚠️ Known broken — not safe to use as-is

**This tool is confirmed to break unrelated FBX imports project-wide.** It's
included in this repo so the community can see exactly what's wrong and
help fix it, not because it's ready to use. See **Troubleshooting** below
before installing it anywhere.

## Description

A Unity `AssetPostprocessor` intended to automate Humanoid rig setup and
material extraction for every FBX imported under a `characters/<name>/`
folder, as part of the Blender → Unity → Timeline character pipeline
documented in `PIPELINE.md`.

## Installation

Not recommended until the known issue below is fixed. If you want to
inspect or help fix it: import `character_import_pipeline.unitypackage`
into a **test/throwaway** Unity project, not a production one.

## Troubleshooting

**Confirmed bug:** the postprocessor forces `animationType = Human` and
`materialLocation = External` on **any** FBX imported under **any**
folder named `characters/<name>/`, anywhere in the project — not just the
intended character files. Confirmed via a clean A/B test: a known-working
FBX imported into a `characters/` folder broke with a Humanoid rig-mapping
error ("Required human bone 'Hips' not found"), while the exact same file
imported elsewhere worked correctly. This makes it unsafe for any project
where `characters/` might contain non-Humanoid or already-correctly-rigged
content.

**Likely fix direction:** scope the postprocessor's logic more narrowly —
e.g. only act on files matching the specific export convention from
`recipes/character_fbx_exporter.py`, rather than matching on folder name
alone.

## Feature Requests / Bug Reports

Please open an issue in this repository — this tool especially could use
help from anyone willing to dig into the AssetPostprocessor scoping logic.
