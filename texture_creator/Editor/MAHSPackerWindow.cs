// MAHS Texture Packer — Unity Editor Window
// Open via:  Tools > chickenandribs > MAHS Texture Packer
//
// Use this when your source textures are raw, separate PBR maps (e.g. from
// PolyHaven, or any site/tool that doesn't already deliver a packed MAHS
// file) — drag each map into its slot by reference, so filenames and
// suffix conventions don't matter at all. Produces the exact set of files
// MAHSSetupWindow.cs looks for (_Albedo, _Normal, _Height, _MAHS), so after
// exporting here, go straight into OPCD > MAHS Setup on the same folder.
//
// What it does:
//   1. Packs Metalness / AO / Height / Roughness into a single RGBA PNG (MAHS)
//      (Metalness is optional — if left blank, treated as fully non-metallic)
//   2. Copies Albedo, Normal, and Height at 2048x2048
//   3. Automatically sets Normal Map type and Linear flag on import

using System.IO;
using UnityEngine;
using UnityEditor;

public class MAHSPackerWindow : EditorWindow
{
    private const int SIZE = 2048;

    private Texture2D _albedo;
    private Texture2D _normal;
    private Texture2D _metalness;
    private Texture2D _ao;
    private Texture2D _height;
    private Texture2D _roughness;

    private string _texName   = "my_texture";
    private string _outFolder = "Assets/Resources/OPCD MAHS Resources/ExtraMaterials";

    [MenuItem("Tools/chickenandribs/MAHS Texture Packer")]
    public static void Open() => GetWindow<MAHSPackerWindow>("MAHS Packer");

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Input Maps", EditorStyles.boldLabel);
        _albedo    = Slot("Albedo  *",    _albedo);
        _normal    = Slot("Normal  *",    _normal);
        _metalness = Slot("Metalness",    _metalness);
        _ao        = Slot("AO  *",        _ao);
        _height    = Slot("Height  *",    _height);
        _roughness = Slot("Roughness  *", _roughness);
        EditorGUILayout.LabelField(
            "* required.  Metalness optional — blank = fully non-metallic.",
            EditorStyles.centeredGreyMiniLabel);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        _texName   = EditorGUILayout.TextField("Texture name", _texName);
        _outFolder = EditorGUILayout.TextField("Folder",       _outFolder);

        EditorGUILayout.Space();

        bool ready = _albedo && _normal && _ao && _height && _roughness
                     && !string.IsNullOrWhiteSpace(_texName);

        using (new EditorGUI.DisabledScope(!ready))
        {
            if (GUILayout.Button("Pack & Export", GUILayout.Height(32)))
                Run();
        }

        if (!ready)
            EditorGUILayout.HelpBox(
                "Assign Albedo, Normal, AO, Height, and Roughness maps to enable export.",
                MessageType.Info);
    }

    private static Texture2D Slot(string label, Texture2D tex) =>
        (Texture2D)EditorGUILayout.ObjectField(label, tex, typeof(Texture2D), false);

    // Forces a source texture's import to Default type with sRGB off, so a raw grayscale
    // map (AO/Height/Roughness/Metalness) or a not-yet-converted Normal map reads as true
    // linear data during BlitTo, instead of getting gamma-decoded by the GPU because Unity
    // still thinks it's a color image. Reimports only when the flags are actually wrong,
    // so this doesn't touch textures the user already set up correctly.
    private static Texture2D EnsureLinearImport(Texture2D tex)
    {
        if (tex == null) return null;

        string path = AssetDatabase.GetAssetPath(tex);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return tex;

        if (imp.sRGBTexture || imp.textureType != TextureImporterType.Default)
        {
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = false;
            imp.SaveAndReimport();
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        return tex;
    }

    // ── Main export ──────────────────────────────────────────────────────────

    private void Run()
    {
        string name    = _texName.Trim();
        string outPath = _outFolder.TrimEnd('/') + "/" + name;
        EnsureFolder(outPath);

        try
        {
            // Freshly-imported source textures default to sRGB on. Graphics.Blit samples a
            // texture using its own import-time sRGB flag regardless of the destination
            // RenderTexture's read/write mode, so in a Linear-space project an untouched
            // AO/Height/Roughness/Metalness/Normal source gets silently gamma-decoded on
            // read — crushing its values down before it's packed. Force these inputs to a
            // Default/Linear import first so BlitTo's sRGB:false calls actually get linear data.
            EditorUtility.DisplayProgressBar("MAHS Packer", "Normalizing source import settings…", 0.05f);
            _metalness = EnsureLinearImport(_metalness);
            _ao        = EnsureLinearImport(_ao);
            _height    = EnsureLinearImport(_height);
            _roughness = EnsureLinearImport(_roughness);
            _normal    = EnsureLinearImport(_normal);

            EditorUtility.DisplayProgressBar("MAHS Packer", "Packing MAHS channels…", 0.1f);
            var mahs = PackMAHS(_metalness, _ao, _height, _roughness);
            Save(mahs, outPath + "/" + name + "_MAHS.png");
            DestroyImmediate(mahs);

            EditorUtility.DisplayProgressBar("MAHS Packer", "Saving Albedo…", 0.40f);
            var alb = BlitTo(_albedo, sRGB: true);
            Save(alb, outPath + "/" + name + "_Albedo.png");
            DestroyImmediate(alb);

            EditorUtility.DisplayProgressBar("MAHS Packer", "Saving Normal…", 0.60f);
            var nrm = BlitTo(_normal, sRGB: false);
            string nrmAssetPath = outPath + "/" + name + "_Normal.png";
            Save(nrm, nrmAssetPath);
            DestroyImmediate(nrm);

            EditorUtility.DisplayProgressBar("MAHS Packer", "Saving Height…", 0.75f);
            var hgt = BlitTo(_height, sRGB: false);
            Save(hgt, outPath + "/" + name + "_Height.png");
            DestroyImmediate(hgt);

            EditorUtility.DisplayProgressBar("MAHS Packer", "Applying import settings…", 0.90f);
            AssetDatabase.Refresh();

            SetImport(outPath + "/" + name + "_MAHS.png", isNormal: false, linear: true);
            SetImport(outPath + "/" + name + "_Height.png", isNormal: false, linear: true);
            SetImport(nrmAssetPath, isNormal: true, linear: false);

            AssetDatabase.Refresh();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        EditorUtility.DisplayDialog(
            "Done!",
            "Exported to:\n" + outPath + "\n\n" +
            "Normal map type and Linear flag set automatically.\n\n" +
            "Next step:  Tools > chickenandribs > MAHS Setup\n" +
            "Browse to this same folder and click Run Setup.",
            "OK");
    }

    // ── Pixel operations ─────────────────────────────────────────────────────

    private static Texture2D PackMAHS(
        Texture2D metal, Texture2D ao, Texture2D height, Texture2D rough)
    {
        var m = metal != null ? BlitTo(metal, sRGB: false) : null;
        var a = BlitTo(ao,     sRGB: false);
        var h = BlitTo(height, sRGB: false);
        var r = BlitTo(rough,  sRGB: false);

        var mPx = m != null ? m.GetPixels32() : null;
        var aPx = a.GetPixels32();
        var hPx = h.GetPixels32();
        var rPx = r.GetPixels32();

        if (m != null) DestroyImmediate(m);
        DestroyImmediate(a);
        DestroyImmediate(h);
        DestroyImmediate(r);

        var result  = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false, linear: true);
        var output  = new Color32[SIZE * SIZE];
        for (int i = 0; i < output.Length; i++)
            output[i] = new Color32(
                mPx != null ? mPx[i].r : (byte)0,  // R = Metalness (0 = non-metallic if absent)
                aPx[i].r,               // G = AO
                hPx[i].r,               // B = Height
                (byte)(255 - rPx[i].r)  // A = Smoothness (Roughness inverted — Unity expects Smoothness)
            );

        result.SetPixels32(output);
        result.Apply();
        return result;
    }

    // Blit src to a readable SIZE×SIZE texture via RenderTexture (bypasses read-restriction).
    private static Texture2D BlitTo(Texture2D src, bool sRGB)
    {
        var readWrite = sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;
        var rt   = RenderTexture.GetTemporary(SIZE, SIZE, 0, RenderTextureFormat.ARGB32, readWrite);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex  = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false, !sRGB);
        tex.ReadPixels(new Rect(0, 0, SIZE, SIZE), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return tex;
    }

    // ── File / asset helpers ─────────────────────────────────────────────────

    private static void Save(Texture2D tex, string assetPath)
    {
        string disk = AssetPathToDisk(assetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(disk));
        File.WriteAllBytes(disk, tex.EncodeToPNG());
    }

    private static void SetImport(string assetPath, bool isNormal, bool linear)
    {
        var imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (imp == null) return;
        imp.textureType    = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        imp.maxTextureSize = 2048;
        if (!isNormal)
        {
            imp.sRGBTexture          = !linear;
            imp.alphaIsTransparency  = false;
        }
        imp.SaveAndReimport();
    }

    private static void EnsureFolder(string assetPath)
    {
        string[] parts  = assetPath.Split('/');
        string   current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static string AssetPathToDisk(string assetPath) =>
        Application.dataPath + "/" + assetPath.Substring("Assets/".Length);
}
