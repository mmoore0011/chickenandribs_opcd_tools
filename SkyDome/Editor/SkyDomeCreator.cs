// SkyDomeCreator.cs
// Editor window that generates a large inside-out sphere (sky dome) and bakes
// a looping Legacy AnimationClip for slow Y-axis rotation onto it.
//
// Open via:  Tools > chickenandribs > Sky Dome > Create Sky Dome

using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyDome
{
    public class SkyDomeCreator : EditorWindow
    {
        // ── Configurable fields ───────────────────────────────────────────────
        private float    _radius          = 1000f;
        private int      _slices          = 64;
        private int      _stacks          = 32;
        private float    _rotationMinutes = 30f;

        // Sky source — JPG path takes priority over the material field
        private string   _jpgSourcePath   = "";   // filesystem path outside Unity
        private Material _skyMaterial;            // fallback: existing material

        private GUIStyle _header;

        // ── Menu item ─────────────────────────────────────────────────────────
        [MenuItem("Tools/chickenandribs/Sky Dome/Create Sky Dome", false, 200)]
        private static void OpenWindow()
        {
            var win = GetWindow<SkyDomeCreator>("Sky Dome Creator");
            win.minSize = new Vector2(380, 460);
        }

        // ── Inspector GUI ─────────────────────────────────────────────────────
        private void OnGUI()
        {
            if (_header == null)
            {
                _header        = new GUIStyle(EditorStyles.boldLabel);
                _header.margin = new RectOffset(0, 0, 8, 2);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Sky Dome Creator", _header);
            EditorGUILayout.HelpBox(
                "Generates an inside-out sphere with a looping rotation clip " +
                "that works in GSPro without any runtime scripts.",
                MessageType.Info);

            EditorGUILayout.Space();

            // ── Geometry ──────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Geometry", _header);

            _radius = EditorGUILayout.FloatField(
                new GUIContent("Radius  (world units)",
                    "Should be larger than the widest extent of your course. " +
                    "1000 units covers most courses at 1 unit = 1 metre scale."),
                _radius);
            _radius = Mathf.Max(100f, _radius);

            _slices = EditorGUILayout.IntSlider(
                new GUIContent("Slices  (longitude)",
                    "Divisions around the equator. 64 gives a smooth silhouette."),
                _slices, 16, 128);

            _stacks = EditorGUILayout.IntSlider(
                new GUIContent("Stacks  (latitude)",
                    "Rings from pole to pole. 32 is plenty."),
                _stacks, 8, 64);

            EditorGUILayout.Space();

            // ── Animation ─────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Animation", _header);

            _rotationMinutes = EditorGUILayout.FloatField(
                new GUIContent("Rotation Period  (minutes)",
                    "How long one full 360° revolution takes. " +
                    "30 min = slow visible cloud drift. " +
                    "60+ min = very subtle movement."),
                _rotationMinutes);
            _rotationMinutes = Mathf.Max(1f, _rotationMinutes);

            EditorGUILayout.Space();

            // ── Sky Texture ───────────────────────────────────────────────────
            EditorGUILayout.LabelField("Sky Texture", _header);
            EditorGUILayout.HelpBox(
                "Browse for a 4K JPG equirectangular panorama (e.g. from polyhaven.com). " +
                "Texture import settings and material are created automatically.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Browse for JPG...", GUILayout.Width(140)))
            {
                string picked = EditorUtility.OpenFilePanel(
                    "Select Sky Panorama JPG",
                    Application.dataPath,
                    "jpg,jpeg");
                if (!string.IsNullOrEmpty(picked))
                    _jpgSourcePath = picked;
            }

            string displayName = string.IsNullOrEmpty(_jpgSourcePath)
                ? "No file selected"
                : Path.GetFileName(_jpgSourcePath);
            EditorGUILayout.LabelField(displayName, EditorStyles.wordWrappedMiniLabel);

            if (!string.IsNullOrEmpty(_jpgSourcePath))
            {
                if (GUILayout.Button("✕", GUILayout.Width(24)))
                    _jpgSourcePath = "";
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // ── Existing material (fallback) ──────────────────────────────────
            bool jpgSelected = !string.IsNullOrEmpty(_jpgSourcePath);
            EditorGUILayout.LabelField(
                jpgSelected ? "Or: Assign Existing Material  (disabled — JPG selected above)"
                            : "Or: Assign Existing Material",
                EditorStyles.miniLabel);

            GUI.enabled = !jpgSelected;
            _skyMaterial = (Material)EditorGUILayout.ObjectField(
                new GUIContent("Sky Material",
                    "Assign a material you have already created. " +
                    "Ignored when a JPG is selected above."),
                _skyMaterial, typeof(Material), false);
            GUI.enabled = true;

            GUILayout.Space(12f);

            GUI.backgroundColor = new Color(0.55f, 0.9f, 0.55f);
            if (GUILayout.Button("Create Sky Dome", GUILayout.Height(36)))
                CreateDome();
            GUI.backgroundColor = Color.white;
        }

        // ── Creation ──────────────────────────────────────────────────────────
        private void CreateDome()
        {
            EnsureFolder("Assets/SkyDome");
            EnsureFolder("Assets/SkyDome/Meshes");
            EnsureFolder("Assets/SkyDome/Animations");
            EnsureFolder("Assets/SkyDome/Prefabs");
            EnsureFolder("Assets/SkyDome/Textures");
            EnsureFolder("Assets/SkyDome/Materials");

            // ── Resolve the material to use ───────────────────────────────────
            Material activeMaterial = _skyMaterial;

            if (!string.IsNullOrEmpty(_jpgSourcePath))
            {
                if (!File.Exists(_jpgSourcePath))
                {
                    EditorUtility.DisplayDialog("Sky Dome – Error",
                        "The selected JPG file no longer exists:\n\n" + _jpgSourcePath +
                        "\n\nClick Browse to select the file again.", "OK");
                    return;
                }

                Texture2D tex = ImportSkyTexture(_jpgSourcePath);
                if (tex == null)
                {
                    EditorUtility.DisplayDialog("Sky Dome – Error",
                        "Failed to import the texture. Check the Console for details.", "OK");
                    return;
                }

                string baseName = Path.GetFileNameWithoutExtension(_jpgSourcePath);
                activeMaterial = CreateSkyMaterial(tex, baseName);
                if (activeMaterial == null) return;
            }

            // ── Mesh ──────────────────────────────────────────────────────────
            string meshPath = "Assets/SkyDome/Meshes/SkyDome.asset";
            AssetDatabase.DeleteAsset(meshPath);
            Mesh mesh = BuildInvertedSphere(_radius, _slices, _stacks);
            AssetDatabase.CreateAsset(mesh, meshPath);

            // ── Animation clip ────────────────────────────────────────────────
            float periodSeconds = _rotationMinutes * 60f;
            string clipPath     = "Assets/SkyDome/Animations/SkyDome_Rotation.anim";
            AssetDatabase.DeleteAsset(clipPath);
            AnimationClip clip = BuildRotationClip(periodSeconds);
            AssetDatabase.CreateAsset(clip, clipPath);

            // ── Scene object ──────────────────────────────────────────────────
            var go = new GameObject("SkyDome");

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            if (activeMaterial != null)
            {
                mr.sharedMaterial = activeMaterial;
            }
            else
            {
                Shader skyShader = Shader.Find("Custom/SkyDome");
                if (skyShader != null)
                    mr.sharedMaterial = new Material(skyShader);
            }
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = false;

            var anim = go.AddComponent<Animation>();
            anim.clip              = clip;
            anim.wrapMode          = WrapMode.Loop;
            anim.playAutomatically = true;
            anim.AddClip(clip, clip.name);

            // ── Save prefab ───────────────────────────────────────────────────
            string prefabPath = "Assets/SkyDome/Prefabs/SkyDome.prefab";
            AssetDatabase.DeleteAsset(prefabPath);
#pragma warning disable 618
            GameObject prefab = PrefabUtility.CreatePrefab(prefabPath, go);
#pragma warning restore 618

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            DestroyImmediate(go);

            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            Debug.Log(
                "[SkyDome] Done.\n" +
                "Prefab: " + prefabPath + "\n" +
                "Radius: " + _radius + " units  |  " +
                "Rotation: 1 rev / " + _rotationMinutes + " min");
        }

        // ── Texture import ────────────────────────────────────────────────────
        //
        // Copies the source JPG into Assets/SkyDome/Textures/, applies the
        // correct import settings (Repeat, Trilinear, Aniso 8, 4096 max,
        // High Quality compression), and returns the imported Texture2D.

        private static Texture2D ImportSkyTexture(string sourcePath)
        {
            string filename     = Path.GetFileName(sourcePath);
            string destRelative = "Assets/SkyDome/Textures/" + filename;
            string destAbsolute = Path.Combine(
                Application.dataPath, "SkyDome", "Textures", filename);

            try
            {
                File.Copy(sourcePath, destAbsolute, overwrite: true);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[SkyDome] Could not copy texture file: " + e.Message);
                return null;
            }

            AssetDatabase.Refresh();

            TextureImporter importer = AssetImporter.GetAtPath(destRelative) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError("[SkyDome] TextureImporter not available for " + destRelative);
                return null;
            }

            importer.textureType        = TextureImporterType.Default;
            importer.sRGBTexture        = true;
            importer.wrapMode           = TextureWrapMode.Repeat;
            importer.filterMode         = FilterMode.Trilinear;
            importer.anisoLevel         = 8;
            importer.maxTextureSize     = 4096;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(destRelative);
        }

        // ── Material creation ─────────────────────────────────────────────────

        private static Material CreateSkyMaterial(Texture2D tex, string baseName)
        {
            Shader skyShader = Shader.Find("Custom/SkyDome");
            if (skyShader == null)
            {
                Debug.LogError(
                    "[SkyDome] Custom/SkyDome shader not found. " +
                    "Make sure SkyDome.shader is in your project.");
                return null;
            }

            string matPath = "Assets/SkyDome/Materials/" + baseName + ".mat";
            AssetDatabase.DeleteAsset(matPath);

            Material mat = new Material(skyShader);
            mat.name = baseName;
            mat.SetTexture("_MainTex", tex);

            AssetDatabase.CreateAsset(mat, matPath);
            Debug.Log("[SkyDome] Material saved to " + matPath);
            return mat;
        }

        // ── Geometry builder ──────────────────────────────────────────────────
        //
        // Standard UV sphere with normals inverted (pointing inward) and
        // reversed triangle winding.  The combination means Unity's default
        // Cull Back renders the surface from inside without any shader changes.
        //
        // UV layout:  u = 0..1 east around equator
        //             v = 1 at north pole, 0 at south pole
        // This matches a standard equirectangular panorama (flip v in the
        // texture import settings if the sky appears upside-down).

        private static Mesh BuildInvertedSphere(float radius, int slices, int stacks)
        {
            int vertCount = (stacks + 1) * (slices + 1);
            var verts = new Vector3[vertCount];
            var norms = new Vector3[vertCount];
            var uvs   = new Vector2[vertCount];

            int idx = 0;
            for (int j = 0; j <= stacks; j++)
            {
                float phi = Mathf.PI * j / stacks;
                float vt  = (float)j / stacks;

                for (int i = 0; i <= slices; i++)
                {
                    float theta = 2f * Mathf.PI * i / slices;
                    float ut    = (float)i / slices;

                    float sx = Mathf.Sin(phi) * Mathf.Cos(theta);
                    float sy = Mathf.Cos(phi);
                    float sz = Mathf.Sin(phi) * Mathf.Sin(theta);

                    verts[idx] = new Vector3(sx, sy, sz) * radius;
                    norms[idx] = new Vector3(-sx, -sy, -sz);
                    uvs[idx]   = new Vector2(ut, 1f - vt);
                    idx++;
                }
            }

            int cols     = slices + 1;
            int triCount = stacks * slices * 6;
            var tris     = new int[triCount];
            int ti       = 0;

            for (int j = 0; j < stacks; j++)
            {
                for (int i = 0; i < slices; i++)
                {
                    int a = j       * cols + i;
                    int b = j       * cols + i + 1;
                    int c = (j + 1) * cols + i;
                    int d = (j + 1) * cols + i + 1;

                    tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                    tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
                }
            }

            var mesh = new Mesh();
            mesh.name      = "SkyDome";
            mesh.vertices  = verts;
            mesh.normals   = norms;
            mesh.uv        = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        // ── Animation builder ─────────────────────────────────────────────────

        private static AnimationClip BuildRotationClip(float periodSeconds)
        {
            var clip      = new AnimationClip();
            clip.name     = "SkyDome_Rotation";
            clip.legacy   = true;
            clip.wrapMode = WrapMode.Loop;

            var settings      = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var curve = new AnimationCurve(
                new Keyframe(0f,            0f),
                new Keyframe(periodSeconds, 360f));

            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(
                    curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(
                    curve, i, AnimationUtility.TangentMode.Linear);
            }

            clip.SetCurve("", typeof(Transform), "localEulerAngles.y", curve);
            return clip;
        }

        // ── Folder utility ────────────────────────────────────────────────────

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            int    slash  = assetPath.LastIndexOf('/');
            string parent = assetPath.Substring(0, slash);
            string name   = assetPath.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
