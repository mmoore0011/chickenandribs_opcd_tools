using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;

namespace VideoAnimations
{
    public static class MakeQuadFromVideo
    {
        [MenuItem("Tools/chickenandribs/Video Animations/Create Quad from Video", false, 100)]
        private static void Create()
        {
            VideoClip clip = Selection.activeObject as VideoClip;

            string videoName = clip.name;
            string prefabFolder = "Assets/Video Animations/Prefabs/" + videoName;

            if (!AssetDatabase.IsValidFolder("Assets/Video Animations"))
                AssetDatabase.CreateFolder("Assets", "Video Animations");
            if (!AssetDatabase.IsValidFolder("Assets/Video Animations/Prefabs"))
                AssetDatabase.CreateFolder("Assets/Video Animations", "Prefabs");
            if (!AssetDatabase.IsValidFolder(prefabFolder))
                AssetDatabase.CreateFolder("Assets/Video Animations/Prefabs", videoName);

            // ── RenderTexture sized to match video ────────────────────────────
            int w = (int)clip.width;
            int h = (int)clip.height;
            RenderTexture rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            rt.name = videoName + "_RenderTex";
            AssetDatabase.CreateAsset(rt, prefabFolder + "/" + videoName + "_RenderTex.renderTexture");

            // ── Material ──────────────────────────────────────────────────────
            Material mat = new Material(Shader.Find("Unlit/Transparent"));
            mat.mainTexture = rt;
            AssetDatabase.CreateAsset(mat, prefabFolder + "/" + videoName + "_Mat.mat");

            // ── Quad ──────────────────────────────────────────────────────────
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = videoName + "_Quad";
            Object.DestroyImmediate(go.GetComponent<MeshCollider>());

            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial    = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = false;

            // ── VideoPlayer ───────────────────────────────────────────────────
            VideoPlayer vp       = go.AddComponent<VideoPlayer>();
            vp.clip              = clip;
            vp.renderMode        = VideoRenderMode.RenderTexture;
            vp.targetTexture     = rt;
            vp.isLooping         = true;
            vp.playOnAwake       = true;
            vp.waitForFirstFrame = true;
            vp.audioOutputMode   = VideoAudioOutputMode.None;

            // ── Prefab ────────────────────────────────────────────────────────
            string prefabPath = prefabFolder + "/" + videoName + "_Quad.prefab";
#pragma warning disable 618
            GameObject prefab = PrefabUtility.CreatePrefab(prefabPath, go);
#pragma warning restore 618

            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            Debug.Log("[VideoAnimations] Created prefab: " + prefabPath +
                      "\nDrag it into the scene and scale to taste." +
                      "\nRotate Y 180° if it faces away from the camera.");
        }

        [MenuItem("Tools/chickenandribs/Video Animations/Create Quad from Video", true)]
        private static bool CreateValidate()
        {
            return Selection.activeObject is VideoClip;
        }
    }
}
