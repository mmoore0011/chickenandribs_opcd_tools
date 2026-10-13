// Character Import Pipeline
//
// Fully automatic — there's no menu command to run. Whenever an FBX lands
// anywhere under Assets/.../characters/<name>/..., this AssetPostprocessor:
//
//   1. Forces External material location, so the extracted material becomes a
//      real, persistent .mat asset instead of being regenerated/discarded on
//      every future reimport (which is what "Use Embedded Materials" does —
//      and why the same texture-wiring kept getting lost every time a file
//      was replaced).
//   2. Forces Humanoid animation type. Every export from the character FBX
//      exporter add-on (Blender-side) includes both mesh and armature, even
//      for animation-only clips, specifically so "Create From This Model"
//      bone auto-detection has skin-weight data to work with every time —
//      without a mesh, that detection is unreliable and fails with "Required
//      human bone 'Hips' not found" even on a skeleton identical to a
//      working file's. (An older version of this tool tried sharing one
//      Avatar across files via ModelImporter.avatarSetup/sourceAvatar —
//      that API doesn't exist until Unity 2019.3, so it doesn't compile on
//      this project's 2018.2. Keeping mesh in every export sidesteps the
//      problem entirely instead of needing that API.)
//   3. After import, auto-wires whatever sibling texture files it finds
//      (checking the FBX's own folder, then walking up toward the character's
//      root folder — e.g. an animation clip sitting in characters/bram/animations/
//      will still find bram.png sitting up in characters/bram/) into the
//      extracted material, matched by filename keyword. Never overwrites a
//      material that's already been wired up by hand.
//
// This exists because a single character can have 30+ separate animation-only
// FBX exports, each of which needed this exact same fix applied by hand.

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class CharacterImportProcessor : AssetPostprocessor
{
    private void OnPreprocessModel()
    {
        string characterFolder = FindCharacterFolder(assetPath);
        if (characterFolder == null) return;

        var importer = (ModelImporter)assetImporter;
        importer.materialLocation = ModelImporterMaterialLocation.External;
        importer.animationType = ModelImporterAnimationType.Human;
    }

    private void OnPostprocessModel(GameObject g)
    {
        string characterFolder = FindCharacterFolder(assetPath);
        if (characterFolder == null) return;

        string fbxDirectory = Path.GetDirectoryName(assetPath).Replace("\\", "/");
        WireUpExtractedMaterials(fbxDirectory, characterFolder);
    }

    // Matches ".../characters/<name>/..." and returns ".../characters/<name>".
    // Returns null for anything outside a characters/<name>/ folder, so this
    // processor never touches unrelated FBX imports elsewhere in the project.
    private static string FindCharacterFolder(string path)
    {
        path = path.Replace("\\", "/");
        const string marker = "/characters/";
        int idx = path.IndexOf(marker);
        if (idx < 0) return null;

        int nameStart = idx + marker.Length;
        int nameEnd = path.IndexOf('/', nameStart);
        if (nameEnd < 0) return null;

        return path.Substring(0, nameEnd);
    }

    private static void WireUpExtractedMaterials(string fbxDirectory, string characterRoot)
    {
        string materialsDir = fbxDirectory.TrimEnd('/') + "/Materials";
        if (!AssetDatabase.IsValidFolder(materialsDir))
            return; // extraction hasn't produced a Materials folder here (yet, or at all)

        string[] textureFiles = FindTexturesWalkingUp(fbxDirectory, characterRoot);
        if (textureFiles == null || textureFiles.Length == 0)
            return;

        string[] suffixKeywords = { "metallic", "roughness", "normal", "emission", "occlusion" };

        Texture2D metallicTex  = FindTextureByKeyword(textureFiles, "metallic");
        Texture2D normalTex    = FindTextureByKeyword(textureFiles, "normal");
        Texture2D emissionTex  = FindTextureByKeyword(textureFiles, "emission");
        Texture2D occlusionTex = FindTextureByKeyword(textureFiles, "occlusion");

        string albedoPath = textureFiles.FirstOrDefault(f =>
        {
            string n = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
            return !suffixKeywords.Any(n.Contains);
        });
        Texture2D albedoTex = albedoPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath) : null;

        if (albedoTex == null && metallicTex == null && normalTex == null && emissionTex == null && occlusionTex == null)
            return;

        Material[] materials = AssetDatabase.FindAssets("t:Material", new[] { materialsDir })
            .Select(guid => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(m => m != null)
            .ToArray();

        bool anyWired = false;

        foreach (Material mat in materials)
        {
            // Don't clobber a material someone (or a previous run) already wired up.
            if (mat.GetTexture("_MainTex") != null) continue;

            if (albedoTex != null)
            {
                mat.SetTexture("_MainTex", albedoTex);
                mat.color = Color.white;
            }
            if (metallicTex != null)
            {
                mat.SetTexture("_MetallicGlossMap", metallicTex);
                mat.EnableKeyword("_METALLICGLOSSMAP");
                mat.SetFloat("_Metallic", 1f);
            }
            if (normalTex != null)
            {
                mat.SetTexture("_BumpMap", normalTex);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (occlusionTex != null)
            {
                mat.SetTexture("_OcclusionMap", occlusionTex);
            }
            if (emissionTex != null)
            {
                mat.SetTexture("_EmissionMap", emissionTex);
                mat.SetColor("_EmissionColor", Color.white);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            EditorUtility.SetDirty(mat);
            anyWired = true;
        }

        if (anyWired)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"[Character Import Pipeline] Wired textures into material(s) for {fbxDirectory}");
        }
    }

    // Checks the FBX's own folder first, then walks upward one folder at a
    // time toward (and including) the character's root folder, stopping at
    // the first level that actually contains image files.
    private static string[] FindTexturesWalkingUp(string startDir, string characterRoot)
    {
        string dir = startDir;
        while (true)
        {
            if (AssetDatabase.IsValidFolder(dir))
            {
                string[] found = Directory.GetFiles(dir)
                    .Where(f => f.ToLower().EndsWith(".png") || f.ToLower().EndsWith(".jpg") || f.ToLower().EndsWith(".jpeg"))
                    .Select(f => f.Replace("\\", "/"))
                    .ToArray();
                if (found.Length > 0) return found;
            }

            if (dir.Equals(characterRoot, System.StringComparison.OrdinalIgnoreCase))
                break;

            int slash = dir.LastIndexOf('/');
            if (slash < 0) break;
            dir = dir.Substring(0, slash);
        }
        return null;
    }

    private static Texture2D FindTextureByKeyword(string[] textureFiles, string keyword)
    {
        string match = textureFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant().Contains(keyword));
        return match != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(match) : null;
    }
}
