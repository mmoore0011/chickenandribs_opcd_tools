// OPCD MAHS Setup Tool
// Open via:  OPCD > MAHS Setup
//
// Workflow (v2.0):
//   Run MAHS_Texture_Packer.py first to produce four PNGs from any PBR texture set:
//     <Name>_Albedo.png   <Name>_MAHS.png   <Name>_Normal.png   <Name>_Height.png
//   Import those four files into your Unity project folder, then use this tool.
//
// Steps automated:
//   1. Set _Normal texture type to Normal Map
//   2. Set _MAHS and _Height to Linear (sRGB off)
//   3. Add Albedo + MAHS to OPCDArray_Detail_Textures_MAHS_2048  (backs up first)
//   4. Add Normal to OPCDArray_Normal_Textures_1024               (backs up first)
//   5. Copy template material → OPCD MAHS - <foldername>.mat in source folder
//   6. Set _RedDetailIndex, uncheck _RedBaseBlend, assign _RedHeightTexture

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

public class MAHSSetupWindow : EditorWindow
{
    // ── OPCD project paths (adjust if your project layout differs) ────────────
    private const string DETAIL_ARRAY_PATH =
        "Assets/Resources/OPCD MAHS Resources/Arrays/OPCDArray_Detail_Textures_MAHS_2048.asset";
    private const string NORMAL_ARRAY_PATH =
        "Assets/Resources/OPCD MAHS Resources/Arrays/OPCDArray_Normal_Textures_1024.asset";

    private const string TEMPLATE_MAT_PATH =
        "Assets/Resources/Materials/Theme - Default/OPCD MAHS Tee Default.mat";

    private const string RED_DETAIL_INDEX_PROP    = "_RedDetailIndex";
    private const string RED_BASE_BLEND_PROP      = "_RedBaseBlend";
    private const string RED_BASE_BLEND_KEYWORD   = "_REDBASEBLEND_ON";
    private const string RED_HEIGHT_TEX_PROP      = "_RedHeightTexture";

    private const int DETAIL_SIZE = 2048;
    private const int NORMAL_SIZE = 1024;
    private const string TEMP_ASSET = "Assets/_mahs_setup_temp.asset";
    private const string VERSION = "2.3";

    // ── State ─────────────────────────────────────────────────────────────────
    private string _sourceFolder = "";

    [MenuItem("Tools/chickenandribs/MAHS Setup")]
    public static void Open()
    {
        GetWindow<MAHSSetupWindow>("MAHS Setup");
    }

    // One-time repair for materials created before the _RedBaseBlend keyword/float desync
    // fix: CopyAsset duplicated the template's keyword list verbatim, so SetFloat alone left
    // _REDBASEBLEND_ON compiled in even when the float read 0. Safe to re-run any time —
    // only touches materials where the keyword actually disagrees with the float.
    [MenuItem("Tools/chickenandribs/MAHS Setup - Fix Existing Materials' Base Blend Keyword")]
    public static void FixExistingBaseBlendKeywords()
    {
        string[] guids = AssetDatabase.FindAssets("t:Material");
        int fixedCount = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || !mat.HasProperty(RED_BASE_BLEND_PROP)) continue;

            bool shouldBeOn = mat.GetFloat(RED_BASE_BLEND_PROP) != 0f;
            bool isOn       = mat.IsKeywordEnabled(RED_BASE_BLEND_KEYWORD);
            if (shouldBeOn == isOn) continue;

            if (shouldBeOn) mat.EnableKeyword(RED_BASE_BLEND_KEYWORD);
            else            mat.DisableKeyword(RED_BASE_BLEND_KEYWORD);
            EditorUtility.SetDirty(mat);
            fixedCount++;
        }

        if (fixedCount > 0)
            AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("Fix Base Blend Keyword",
            fixedCount + " material(s) had a mismatched Red Base Blend keyword — now fixed.",
            "OK");
    }

    // ── GUI ───────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        GUILayout.Label("OPCD MAHS Material Setup  v" + VERSION, EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Select the folder containing your packed textures (from MAHS_Texture_Packer.py).\n" +
            "Required files: _Albedo, _MAHS, _Normal, _Height",
            MessageType.Info);
        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Source folder:", GUILayout.Width(90));
        EditorGUILayout.LabelField(
            string.IsNullOrEmpty(_sourceFolder) ? "(none selected)" : _sourceFolder,
            EditorStyles.wordWrappedMiniLabel);
        if (GUILayout.Button("Browse", GUILayout.Width(60)))
            Browse();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        bool ready = !string.IsNullOrEmpty(_sourceFolder);
        using (new EditorGUI.DisabledScope(!ready))
        {
            if (GUILayout.Button("Add to Arrays", GUILayout.Height(32)))
                Run();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.LabelField("Emergency Restore", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "If a run failed without rolling back, click below to restore both arrays from their most recent backup.",
            MessageType.Warning);
        if (GUILayout.Button("Restore Arrays from Backups"))
            RestoreArrayBackups();
    }

    // ── Folder picker ─────────────────────────────────────────────────────────
    private void Browse()
    {
        string defaultDir = Application.dataPath + "/Resources/OPCD MAHS Resources/ExtraMaterials";
        if (!Directory.Exists(defaultDir)) defaultDir = Application.dataPath;

        string picked = EditorUtility.OpenFolderPanel(
            "Select packed texture folder", defaultDir, "");
        if (string.IsNullOrEmpty(picked)) return;

        picked = picked.Replace('\\', '/');
        string dataPath = Application.dataPath.Replace('\\', '/');

        if (!picked.StartsWith(dataPath))
        {
            EditorUtility.DisplayDialog("Error",
                "Please select a folder inside this project's Assets folder.", "OK");
            return;
        }
        _sourceFolder = "Assets" + picked.Substring(dataPath.Length);
    }

    // ── Main run ──────────────────────────────────────────────────────────────
    private void Run()
    {
        int albedoSlot = -1;
        int normalSlot = -1;

        var undo = new List<Action>();

        try
        {
            // ── 0. Find source textures ───────────────────────────────────────
            SetProgress(0.05f, "Finding textures…");
            var albedoTex = Require(FindTex(_sourceFolder, "Albedo"), "Albedo");
            var mahsTex   = Require(FindTex(_sourceFolder, "MAHS"),   "MAHS");
            var normalTex = Require(FindTex(_sourceFolder, "Normal"), "Normal");
            var heightTex = FindTex(_sourceFolder, "Height"); // optional for array; reported to user

            // Store paths before any reimport invalidates references
            string albedoPath = AssetDatabase.GetAssetPath(albedoTex);
            string mahsPath   = AssetDatabase.GetAssetPath(mahsTex);
            string normalPath = AssetDatabase.GetAssetPath(normalTex);
            string heightPath = heightTex != null ? AssetDatabase.GetAssetPath(heightTex) : null;

            // ── 1. Set texture import settings ────────────────────────────────
            SetProgress(0.15f, "Setting texture import settings…");

            var normalImp     = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            var prevNormalType = normalImp != null ? normalImp.textureType : TextureImporterType.Default;
            ApplyNormalMapType(normalPath);
            undo.Add(() =>
            {
                var imp = AssetImporter.GetAtPath(normalPath) as TextureImporter;
                if (imp != null) { imp.textureType = prevNormalType; imp.SaveAndReimport(); }
            });

            ApplyLinearImport(mahsPath, DETAIL_SIZE);
            if (heightPath != null)
                ApplyLinearImport(heightPath, DETAIL_SIZE);

            AssetDatabase.Refresh();

            // Reload references after reimport
            albedoTex = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
            mahsTex   = AssetDatabase.LoadAssetAtPath<Texture2D>(mahsPath);
            normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);

            if (albedoTex == null) throw new Exception("Albedo texture not found after refresh:\n" + albedoPath);
            if (mahsTex == null)   throw new Exception("MAHS texture not found after refresh:\n" + mahsPath);
            if (normalTex == null) throw new Exception("Normal texture not found after refresh:\n" + normalPath);

            // ── 2. Expand Detail array (Albedo + MAHS) ────────────────────────
            SetProgress(0.35f, "Backing up Detail array…");
            var    detailArr    = LoadAsset<Texture2DArray>(DETAIL_ARRAY_PATH);
            albedoSlot          = detailArr.depth;
            string detailBackup = BackupAsset(DETAIL_ARRAY_PATH);
            undo.Add(() => RestoreFromBackup(detailBackup, DETAIL_ARRAY_PATH));

            SetProgress(0.50f, "Expanding Detail array…");
            var newDetail = ExpandArray(
                detailArr,
                new Texture2D[] { albedoTex, mahsTex },
                new bool[]      { true,      false   },
                DETAIL_SIZE);
            OverwriteArrayAsset(newDetail, DETAIL_ARRAY_PATH);

            // ── 3. Expand Normal array (Normal) ───────────────────────────────
            SetProgress(0.70f, "Backing up Normal array…");
            var    normalArr    = LoadAsset<Texture2DArray>(NORMAL_ARRAY_PATH);
            normalSlot          = normalArr.depth;
            string normalBackup = BackupAsset(NORMAL_ARRAY_PATH);
            undo.Add(() => RestoreFromBackup(normalBackup, NORMAL_ARRAY_PATH));

            SetProgress(0.85f, "Expanding Normal array…");
            var newNormal = ExpandArray(
                normalArr,
                new Texture2D[] { normalTex },
                new bool[]      { false     },
                NORMAL_SIZE);
            OverwriteArrayAsset(newNormal, NORMAL_ARRAY_PATH);

            AssetDatabase.Refresh();

            // ── 4. Copy and configure material ────────────────────────────────
            SetProgress(0.90f, "Creating material…");
            string folderName = Path.GetFileName(_sourceFolder.TrimEnd('/'));
            string matPath = _sourceFolder + "/OPCD MAHS - " + folderName + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(matPath) != null)
                AssetDatabase.DeleteAsset(matPath);
            if (!AssetDatabase.CopyAsset(TEMPLATE_MAT_PATH, matPath))
                throw new Exception("Could not copy template material.\nExpected at:\n" + TEMPLATE_MAT_PATH);
            AssetDatabase.Refresh();
            undo.Add(() =>
            {
                if (AssetDatabase.LoadAssetAtPath<Material>(matPath) != null)
                    AssetDatabase.DeleteAsset(matPath);
            });

            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) throw new Exception("Could not load new material: " + matPath);

            if (mat.HasProperty(RED_DETAIL_INDEX_PROP))
                mat.SetFloat(RED_DETAIL_INDEX_PROP, (float)albedoSlot);
            else
                Debug.LogWarning("[MAHS Setup] Property not found: " + RED_DETAIL_INDEX_PROP +
                    " — set Red Detail Index to " + albedoSlot + " manually.");

            if (mat.HasProperty(RED_BASE_BLEND_PROP))
            {
                mat.SetFloat(RED_BASE_BLEND_PROP, 0f);
                // [Toggle] properties compile through a shader keyword, not the raw float —
                // CopyAsset duplicated the template's keyword list verbatim, so the keyword
                // must be disabled explicitly or the base-blend code path stays compiled in
                // regardless of what the float says.
                mat.DisableKeyword(RED_BASE_BLEND_KEYWORD);
            }
            else
                Debug.LogWarning("[MAHS Setup] Property not found: " + RED_BASE_BLEND_PROP +
                    " — uncheck Red Base Blend manually.");

            if (heightPath != null)
            {
                var heightTex2 = AssetDatabase.LoadAssetAtPath<Texture2D>(heightPath);
                if (mat.HasProperty(RED_HEIGHT_TEX_PROP) && heightTex2 != null)
                    mat.SetTexture(RED_HEIGHT_TEX_PROP, heightTex2);
                else if (!mat.HasProperty(RED_HEIGHT_TEX_PROP))
                    Debug.LogWarning("[MAHS Setup] Property not found: " + RED_HEIGHT_TEX_PROP +
                        " — assign the Height texture manually.");
            }

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            EditorUtility.ClearProgressBar();

            EditorUtility.DisplayDialog(
                "Setup complete!",
                "Material: OPCD MAHS - " + folderName + "\n\n" +
                "Detail array:  Albedo slot = " + albedoSlot + "   MAHS slot = " + (albedoSlot + 1) + "\n" +
                "Normal array:  slot = " + normalSlot + "\n\n" +
                "Red Detail Index set to " + albedoSlot + ".\n" +
                "Red Base Blend unchecked.\n" +
                "Red Height Texture assigned.\n\n" +
                "All done!",
                "OK");
        }
        catch (Exception ex)
        {
            EditorUtility.ClearProgressBar();

            SetProgress(0f, "Rolling back…");
            for (int i = undo.Count - 1; i >= 0; i--)
            {
                try { undo[i](); }
                catch (Exception rollbackEx)
                {
                    Debug.LogError("[MAHS Setup] Rollback step " + i + " failed: " + rollbackEx.Message);
                }
            }

            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Setup failed — changes rolled back",
                ex.Message + "\n\nAll completed steps have been undone. Fix the issue above and run again.",
                "OK");
            Debug.LogException(ex);
        }
    }

    // ── Texture import settings ───────────────────────────────────────────────

    private static void ApplyNormalMapType(string assetPath)
    {
        var imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (imp == null) throw new Exception("No importer for: " + assetPath);
        imp.textureType    = TextureImporterType.NormalMap;
        imp.maxTextureSize = 2048;
        imp.SaveAndReimport();
    }

    private static void ApplyLinearImport(string assetPath, int maxSize)
    {
        var imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (imp == null) return;
        imp.textureType         = TextureImporterType.Default;
        imp.sRGBTexture         = false;
        imp.alphaIsTransparency = false;
        imp.maxTextureSize      = maxSize;
        imp.SaveAndReimport();
    }

    // ── Array expansion ───────────────────────────────────────────────────────

    private static int ArrayMipCount(Texture2DArray arr)
    {
        return Mathf.FloorToInt(Mathf.Log(Mathf.Max(arr.width, arr.height), 2f)) + 1;
    }

    private static bool ArrayHasMips(Texture2DArray arr)
    {
        try { arr.GetPixels(0, 1); return true; }
        catch { return false; }
    }

    private static Texture2DArray ExpandArray(
        Texture2DArray existing, Texture2D[] newTextures, bool[] sRGBFlags, int targetSize)
    {
        int newDepth  = existing.depth + newTextures.Length;
        bool hasMips  = ArrayHasMips(existing);
        int mipLevels = hasMips ? ArrayMipCount(existing) : 1;

        var result = new Texture2DArray(
            existing.width, existing.height, newDepth,
            existing.format, hasMips, true);
        result.filterMode = existing.filterMode;
        result.wrapMode   = existing.wrapMode;

        if (IsCompressedFormat(existing.format))
        {
            if ((SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0)
                throw new Exception(
                    "GPU texture copy is not supported on this system (CopyTextureSupport missing).\n" +
                    "The arrays cannot be expanded without reformatting on this hardware.");

            result.Apply(false, false);

            for (int i = 0; i < existing.depth; i++)
                for (int m = 0; m < mipLevels; m++)
                    Graphics.CopyTexture(existing, i, m, result, i, m);

            for (int n = 0; n < newTextures.Length; n++)
                AddCompressedSlice(result, existing.format, newTextures[n],
                                   existing.depth + n, sRGBFlags[n], targetSize, hasMips);
        }
        else
        {
            for (int i = 0; i < existing.depth; i++)
                result.SetPixels(existing.GetPixels(i, 0), i, 0);
            for (int n = 0; n < newTextures.Length; n++)
                AddSlice(result, newTextures[n], existing.depth + n, sRGBFlags[n], targetSize, false);
            result.Apply(false);
        }

        return result;
    }

    private static bool IsCompressedFormat(TextureFormat fmt)
    {
        return fmt == TextureFormat.DXT1 || fmt == TextureFormat.DXT5
            || fmt == TextureFormat.BC4  || fmt == TextureFormat.BC5
            || fmt == TextureFormat.BC6H || fmt == TextureFormat.BC7;
    }

    private static void AddCompressedSlice(Texture2DArray arr, TextureFormat format,
        Texture2D src, int slot, bool sRGB, int size, bool hasMips)
    {
        var tmp = BlitResized(src, size, size, sRGB, hasMips);
        EditorUtility.CompressTexture(tmp, format, TextureCompressionQuality.Best);
        tmp.Apply(hasMips);
        int mipCount = hasMips ? Mathf.Min(tmp.mipmapCount, ArrayMipCount(arr)) : 1;
        for (int m = 0; m < mipCount; m++)
            Graphics.CopyTexture(tmp, 0, m, arr, slot, m);
        DestroyImmediate(tmp);
    }

    private static void AddSlice(
        Texture2DArray arr, Texture2D src,
        int slot, bool sRGB, int size, bool generateMips)
    {
        var tmp      = BlitResized(src, size, size, sRGB, generateMips);
        int mipCount = Mathf.Min(tmp.mipmapCount, ArrayMipCount(arr));
        for (int mip = 0; mip < mipCount; mip++)
            arr.SetPixels(tmp.GetPixels(mip), slot, mip);
        DestroyImmediate(tmp);
    }

    private static void OverwriteArrayAsset(Texture2DArray newArray, string originalPath)
    {
        newArray.name = Path.GetFileNameWithoutExtension(originalPath);

        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TEMP_ASSET) != null)
            AssetDatabase.DeleteAsset(TEMP_ASSET);

        var original = AssetDatabase.LoadAssetAtPath<Texture2DArray>(originalPath);
        if (original == null) throw new Exception("Cannot load array asset to update:\n" + originalPath);

        EditorUtility.CopySerialized(newArray, original);
        EditorUtility.SetDirty(original);
        AssetDatabase.SaveAssets();

        DestroyImmediate(newArray);
    }

    private static string BackupAsset(string assetPath)
    {
        string dir  = Path.GetDirectoryName(assetPath).Replace('\\', '/');
        string stem = Path.GetFileNameWithoutExtension(assetPath);
        string ext  = Path.GetExtension(assetPath);

        string dest = dir + "/" + stem + "_backup" + ext;
        int n = 1;
        while (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dest) != null)
            dest = dir + "/" + stem + "_backup" + (n++) + ext;

        if (!AssetDatabase.CopyAsset(assetPath, dest))
            throw new Exception("Failed to back up asset: " + assetPath);
        return dest;
    }

    private static void RestoreFromBackup(string backupPath, string originalPath)
    {
        if (string.IsNullOrEmpty(backupPath)) return;

        var backup   = AssetDatabase.LoadAssetAtPath<Texture2DArray>(backupPath);
        var original = AssetDatabase.LoadAssetAtPath<Texture2DArray>(originalPath);

        if (backup == null)
        {
            Debug.LogError("[MAHS Setup] Backup asset not found: " + backupPath);
            return;
        }
        if (original == null)
        {
            Debug.LogError("[MAHS Setup] Original asset not found: " + originalPath);
            return;
        }

        EditorUtility.CopySerialized(backup, original);
        EditorUtility.SetDirty(original);
        AssetDatabase.SaveAssets();
        Debug.Log("[MAHS Setup] Restored " + originalPath + " from " + backupPath);
    }

    // ── Texture utilities ─────────────────────────────────────────────────────

    private static Texture2D BlitResized(Texture2D src, int w, int h, bool sRGB, bool mips = false)
    {
        var readWrite = sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;
        var rt   = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, readWrite);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, mips, !sRGB);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply(mips);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return tex;
    }

    // ── Asset / path utilities ────────────────────────────────────────────────

    private static T LoadAsset<T>(string assetPath) where T : UnityEngine.Object
    {
        var obj = AssetDatabase.LoadAssetAtPath<T>(assetPath);
        if (obj == null) throw new Exception("Asset not found:\n" + assetPath);
        return obj;
    }

    private static Texture2D FindTex(string folder, string suffix)
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new string[] { folder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string stem = Path.GetFileNameWithoutExtension(path);
            if (stem.EndsWith("_" + suffix, StringComparison.OrdinalIgnoreCase))
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        return null;
    }

    private static Texture2D Require(Texture2D tex, string label)
    {
        if (tex == null)
            throw new Exception("Could not find " + label + " texture in source folder.\n" +
                "Expected a file ending in _" + label + ".png");
        return tex;
    }

    private static void SetProgress(float p, string msg)
    {
        EditorUtility.DisplayProgressBar("MAHS Setup", msg, p);
    }

    // ── Emergency restore ─────────────────────────────────────────────────────

    private static void RestoreArrayBackups()
    {
        string[] originals = new string[] { DETAIL_ARRAY_PATH, NORMAL_ARRAY_PATH };
        string report = "";

        foreach (string original in originals)
        {
            string backup    = FindLatestBackup(original);
            string arrayName = Path.GetFileNameWithoutExtension(original);
            if (string.IsNullOrEmpty(backup))
            {
                report += arrayName + ": no backup found\n";
            }
            else
            {
                RestoreFromBackup(backup, original);
                report += arrayName + ":\n  restored from " + Path.GetFileNameWithoutExtension(backup) + "\n";
            }
        }

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Restore complete", report.TrimEnd(), "OK");
    }

    private static string FindLatestBackup(string originalPath)
    {
        string dir  = Path.GetDirectoryName(originalPath).Replace('\\', '/');
        string stem = Path.GetFileNameWithoutExtension(originalPath);
        string ext  = Path.GetExtension(originalPath);

        for (int n = 20; n >= 1; n--)
        {
            string c = dir + "/" + stem + "_backup" + n + ext;
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(c) != null)
                return c;
        }

        string plain = dir + "/" + stem + "_backup" + ext;
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(plain) != null)
            return plain;

        return null;
    }
}
