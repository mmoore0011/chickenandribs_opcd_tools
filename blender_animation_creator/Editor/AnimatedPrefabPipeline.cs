using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Editor-only tool: converts a freshly-exported Blender FBX (with an armature
/// animation) into a fully wired, looping, animated Prefab — without any
/// manual Animator setup each time.
///
/// This is a build-time / editor convenience tool only. It does not run in
/// the built game and adds no runtime scripts to the animated object itself
/// — the result is just a GameObject + Animator + AnimatorController asset,
/// the same as if you'd done it by hand in the Editor.
///
/// Usage: Tools > chickenandribs > Blender Animation Converter
/// Select the source FBX in the Project window first.
/// </summary>
public static class AnimatedPrefabPipeline
{
    // Adjust this if your "real" clip naming convention changes.
    // This matches Blender's default action name. Other sources (e.g. Meshy AI)
    // name the clip after the animation itself instead (e.g.
    // "Armature|Armature|Armature|Armature|running|baselayer"), so we fall back
    // to picking the longest non-trivial clip when nothing matches this.
    private const string RealClipNameContains = "ArmatureAction";
    private const int MinRealClipFrameCount = 2;

    [MenuItem("Tools/chickenandribs/Blender Animation Converter")]
    public static void ConvertSelectedFbx()
    {
        Object selected = Selection.activeObject;
        if (selected == null)
        {
            EditorUtility.DisplayDialog("Blender Animation Converter", "Select an FBX file in the Project window first.", "OK");
            return;
        }

        string fbxPath = AssetDatabase.GetAssetPath(selected);
        if (string.IsNullOrEmpty(fbxPath) || !fbxPath.ToLower().EndsWith(".fbx"))
        {
            EditorUtility.DisplayDialog("Blender Animation Converter", "Selected object is not an FBX file.", "OK");
            return;
        }

        try
        {
            ConvertFbxToAnimatedPrefab(fbxPath);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Blender Animation Converter] Conversion failed: {e.Message}\n{e.StackTrace}");
            EditorUtility.DisplayDialog("Blender Animation Converter", $"Conversion failed:\n{e.Message}", "OK");
        }
    }

    private static void ConvertFbxToAnimatedPrefab(string fbxPath)
    {
        EditorUtility.DisplayProgressBar("Blender Animation Converter", "Fixing import settings...", 0.1f);

        string fbxDirectory = Path.GetDirectoryName(fbxPath);
        string baseName = Path.GetFileNameWithoutExtension(fbxPath);

        // --- Step 1: Force correct import settings on the FBX ---
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
            throw new System.Exception($"Could not get ModelImporter for {fbxPath}");

        bool importerDirty = false;

        if (importer.animationType != ModelImporterAnimationType.Generic)
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            importerDirty = true;
        }

        // External materials land as editable .mat assets in a "Materials"
        // folder next to the FBX, so texture wiring below (and any manual
        // tweaks later) actually stick instead of being regenerated/discarded
        // on every reimport like embedded materials are.
        if (importer.materialLocation != ModelImporterMaterialLocation.External)
        {
            importer.materialLocation = ModelImporterMaterialLocation.External;
            importerDirty = true;
        }

        // Find the real clip and force its Loop Time on, while leaving the
        // junk empty clips alone.
        ModelImporterClipAnimation[] clipAnimations = importer.clipAnimations;
        bool hasExplicitClips = clipAnimations != null && clipAnimations.Length > 0;

        // If there are no explicit clipAnimations yet (common on first import),
        // we need to seed them from defaultClipAnimations so we can edit Loop Time.
        if (!hasExplicitClips)
        {
            clipAnimations = importer.defaultClipAnimations;
        }

        ModelImporterClipAnimation realClip = clipAnimations?
            .Where(c => c.name.Contains(RealClipNameContains))
            .OrderByDescending(c => c.lastFrame - c.firstFrame)
            .FirstOrDefault();

        // No Blender-style "ArmatureAction" clip found — fall back to the longest
        // clip that isn't a trivial (e.g. 1-frame bind pose) clip.
        // (Not using ??= here: this project's Unity/Mono compiler is on C# 7.3,
        // which predates the ??= operator added in C# 8.)
        if (realClip == null)
        {
            realClip = clipAnimations?
                .Where(c => c.lastFrame - c.firstFrame >= MinRealClipFrameCount)
                .OrderByDescending(c => c.lastFrame - c.firstFrame)
                .FirstOrDefault();
        }

        if (realClip == null)
        {
            throw new System.Exception(
                $"No animation clip containing '{RealClipNameContains}', and no clip with at least " +
                $"{MinRealClipFrameCount} frames, was found in {fbxPath}. " +
                "Check the clip naming in Blender, or update RealClipNameContains in this script.");
        }

        string realClipName = realClip.name;

        for (int i = 0; i < clipAnimations.Length; i++)
        {
            if (clipAnimations[i].name == realClipName)
            {
                clipAnimations[i].loopTime = true;
            }
        }

        importer.clipAnimations = clipAnimations;
        importerDirty = true;

        if (importerDirty)
        {
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }

        EditorUtility.DisplayProgressBar("Blender Animation Converter", "Wiring up materials...", 0.3f);

        // --- Step 1b: Wire sibling Meshy-style textures into the extracted material(s) ---
        WireUpExtractedMaterials(fbxDirectory);

        EditorUtility.DisplayProgressBar("Blender Animation Converter", "Building Animator Controller...", 0.4f);

        // --- Step 2: Build (or overwrite) the Animator Controller ---
        string controllerPath = CombineAssetPath(fbxDirectory, baseName + "Controller.controller");

        // Re-fetch the imported clip object now that import is finalized.
        // Both ModelImporterClipAnimation.name and the sub-asset name use the same format
        // ("Armature|ArmatureAction.001"), so direct equality works.
        AnimationClip clipAsset = AssetDatabase.LoadAllAssetsAtPath(fbxPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__") && c.name == realClipName);

        if (clipAsset == null)
        {
            throw new System.Exception(
                $"Import succeeded but could not locate the AnimationClip asset '{realClipName}' " +
                $"inside {fbxPath} after reimport.");
        }

        Debug.Log($"[BAC] Selected clip: '{clipAsset.name}'");

        // Always create fresh, per your preference — delete any existing
        // controller at this path first so CreateAnimatorControllerAtPath
        // doesn't fail or merge with stale data.
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
        {
            AssetDatabase.DeleteAsset(controllerPath);
        }

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        AnimatorStateMachine rootStateMachine = controller.layers[0].stateMachine;

        AnimatorState flapState = rootStateMachine.AddState(realClipName);
        flapState.motion = clipAsset;
        rootStateMachine.defaultState = flapState;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayProgressBar("Blender Animation Converter", "Instantiating and wiring prefab...", 0.7f);

        // --- Step 3: Instantiate the FBX, wire the Animator, save as Prefab ---
        Object fbxRoot = AssetDatabase.LoadAssetAtPath<Object>(fbxPath);
        GameObject instance = (GameObject)Object.Instantiate(fbxRoot);
        instance.name = baseName;

        Animator animator = instance.GetComponent<Animator>();
        if (animator == null)
            animator = instance.AddComponent<Animator>();

        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        Debug.Log($"[BAC] Animator avatar: {(animator.avatar != null ? animator.avatar.name : "NULL")}  controller: {controller.name}");

        // --- Step 4: Save as a new prefab, overwriting same path/name each time ---
        string prefabPath = CombineAssetPath(fbxDirectory, baseName + ".prefab");

        // "Always overwrite the same prefab path/name" per your preference.
        // NOTE: Unity 2018.2 predates PrefabUtility.SaveAsPrefabAsset (added in 2018.3),
        // so we use the older CreatePrefab API here instead.
        GameObject resultingPrefab = PrefabUtility.CreatePrefab(prefabPath, instance, ReplacePrefabOptions.ReplaceNameBased);
        bool prefabSuccess = resultingPrefab != null;

        Object.DestroyImmediate(instance);

        EditorUtility.ClearProgressBar();

        if (!prefabSuccess)
        {
            throw new System.Exception($"PrefabUtility failed to save prefab at {prefabPath}");
        }

        AssetDatabase.Refresh();
        Debug.Log($"[Blender Animation Converter] Done. Prefab: {prefabPath}  |  Controller: {controllerPath}  |  Clip used: {realClipName}");

        EditorUtility.DisplayDialog(
            "Blender Animation Converter",
            $"Created/updated:\n{prefabPath}\n\nUsing clip: {realClipName}\nController: {controllerPath}",
            "Nice");

        // Ping the resulting prefab in the Project window for convenience.
        Object resultPrefab = AssetDatabase.LoadAssetAtPath<Object>(prefabPath);
        EditorGUIUtility.PingObject(resultPrefab);
        Selection.activeObject = resultPrefab;
    }

    // Meshy AI (and similar auto-generated exports) drop sibling PNGs next to
    // the FBX named after a shared base with a map-identifying suffix, e.g.
    // "<name>_texture_0.png" (base color), "..._texture_0_metallic.png",
    // "..._texture_0_roughness.png". This wires them into whatever material(s)
    // materialLocation = External just extracted, by filename keyword rather
    // than exact suffix, so it isn't tied to Meshy's exact numbering scheme.
    private static void WireUpExtractedMaterials(string fbxDirectory)
    {
        string materialsDir = CombineAssetPath(fbxDirectory, "Materials");
        if (!AssetDatabase.IsValidFolder(materialsDir))
            return; // FBX had no material to extract, or extraction produced none

        string[] textureFiles = Directory.GetFiles(fbxDirectory)
            .Where(f => f.ToLower().EndsWith(".png") || f.ToLower().EndsWith(".jpg") || f.ToLower().EndsWith(".jpeg"))
            .Select(f => f.Replace("\\", "/"))
            .ToArray();

        if (textureFiles.Length == 0)
            return; // no sibling textures next to the FBX to wire up

        string[] suffixKeywords = { "metallic", "roughness", "normal", "emission", "occlusion" };

        Texture2D metallicTex  = FindTextureByKeyword(textureFiles, "metallic");
        Texture2D roughnessTex = FindTextureByKeyword(textureFiles, "roughness");
        Texture2D normalTex    = FindTextureByKeyword(textureFiles, "normal");
        Texture2D emissionTex  = FindTextureByKeyword(textureFiles, "emission");
        Texture2D occlusionTex = FindTextureByKeyword(textureFiles, "occlusion");

        string albedoPath = textureFiles.FirstOrDefault(f =>
        {
            string n = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
            return n.Contains("texture") && !suffixKeywords.Any(n.Contains);
        });
        Texture2D albedoTex = albedoPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath) : null;

        if (albedoTex == null && metallicTex == null && normalTex == null && emissionTex == null && occlusionTex == null)
            return; // none of the sibling files matched the naming convention

        Material[] materials = AssetDatabase.FindAssets("t:Material", new[] { materialsDir })
            .Select(guid => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(m => m != null)
            .ToArray();

        foreach (Material mat in materials)
        {
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
        }

        if (roughnessTex != null)
        {
            // Standard shader has no standalone roughness slot — smoothness
            // comes from the alpha channel of _MetallicGlossMap instead, and
            // Meshy exports roughness as its own separate texture. Rather than
            // silently guess at a combined map, leave it unwired and say so.
            Debug.Log($"[Blender Animation Converter] Found a roughness map ('{roughnessTex.name}') " +
                      "but the Standard shader has no separate roughness slot — left unwired. " +
                      "The metallic map's alpha channel (if any) drives smoothness instead.");
        }

        AssetDatabase.SaveAssets();
    }

    private static Texture2D FindTextureByKeyword(string[] textureFiles, string keyword)
    {
        string match = textureFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant().Contains(keyword));
        return match != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(match) : null;
    }

    private static string CombineAssetPath(string directory, string fileName)
    {
        // Asset paths always use forward slashes, regardless of OS.
        return (directory.Replace("\\", "/").TrimEnd('/') + "/" + fileName);
    }
}
