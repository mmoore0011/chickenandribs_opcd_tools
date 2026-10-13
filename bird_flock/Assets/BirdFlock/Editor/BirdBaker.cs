// BirdBaker.cs  –  Editor only
// ──────────────────────────────────────────────────────────────────────────────
// WHY THIS EXISTS
// GSPro loads courses as asset bundles which cannot run custom MonoBehaviour
// scripts.  This tool converts the runtime BirdSpawner into fully baked scene
// objects:
//
//   • Bird meshes    → saved as .asset files
//   • Bird materials → saved as .mat files
//   • Flock movement → baked into AnimationClip .anim files that include
//                      BOTH the flight path AND the wait gap between flocks,
//                      played by Unity's built-in Animation component
//                      (no custom scripts needed at GSPro runtime)
//
// CLIP STRUCTURE (per baked flock)
// ─────────────────────────────────
//   Segment A  startDist → fullDist   flock in flight (spawn → despawn, off-screen ends)
//   Teleport   0.016 s                instant jump back to spawn point
//   Wait       avgInterval seconds    flock idles at spawn (off-screen) – held position
//   Segment B  0 → startDist          flock returns to its stagger start position
//   ──────────────────────────────────────────────────────────────────────────
//   Total clip = travelTime + 0.016 + avgInterval  (identical for all flocks)
//
// One flock is baked per spawner.  It flies the full path, waits at the
// spawn point for avgInterval seconds, then loops.
//
// HOW TO USE
//  1.  Set up your BirdPath + BirdSpawner and verify flocks work in Play mode.
//  2.  Stop Play.
//  3.  Menu → Tools → chickenandribs → Bird Flock → Bake All Flocks in Scene
//  4.  Save the scene (Ctrl+S).
//  5.  Export your course to GSPro as normal.
//
// TO ITERATE
//  Menu → Tools → chickenandribs → Bird Flock → Clear Baked Flocks
//  This removes baked flocks and re-enables the spawner so you can adjust
//  settings and bake again.
// ──────────────────────────────────────────────────────────────────────────────

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BirdFlock
{
    public static class BirdBaker
    {
        // ── Asset save locations ────────────────────────────────────────
        private const string RootFolder = "Assets/BirdFlock/Baked";
        private const string MeshFolder = RootFolder + "/Meshes";
        private const string MatFolder  = RootFolder + "/Materials";
        private const string AnimFolder = RootFolder + "/Animations";

        private const int FlocksPerSpawner = 1;

        // ── Menu items ──────────────────────────────────────────────────
        [MenuItem("Tools/chickenandribs/Bird Flock/Bake All Flocks in Scene", false, 100)]
        public static void BakeAll()
        {
            var spawners = Object.FindObjectsOfType<BirdSpawner>();
            if (spawners == null || spawners.Length == 0)
            {
                EditorUtility.DisplayDialog("Bird Flock – Bake",
                    "No BirdSpawner components found in the currently open scene.", "OK");
                return;
            }

            bool anyReady = false;
            foreach (var s in spawners)
                if (s.birdPath != null) { anyReady = true; break; }

            if (!anyReady)
            {
                EditorUtility.DisplayDialog("Bird Flock – Bake",
                    "Found BirdSpawner(s) but none have a BirdPath assigned.", "OK");
                return;
            }

            EnsureFolders();

            int total = 0;
            foreach (var spawner in spawners)
            {
                if (spawner.birdPath == null)
                {
                    Debug.LogWarning("[BirdFlock] Skipping spawner '" +
                        spawner.name + "': no BirdPath assigned.");
                    continue;
                }

                if (spawner.birdPath.WaypointCount < 2)
                {
                    Debug.LogWarning("[BirdFlock] Skipping spawner '" +
                        spawner.name + "': path has fewer than 2 waypoints.");
                    continue;
                }

                total += BakeSpawner(spawner);
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            EditorUtility.DisplayDialog("Bird Flock – Bake Complete",
                total + " flock(s) baked.\n\n" +
                "Assets saved to  " + RootFolder + "\n\n" +
                "IMPORTANT: Save your scene (Ctrl+S) before exporting to GSPro.",
                "OK");
        }

        [MenuItem("Tools/chickenandribs/Bird Flock/Clear Baked Flocks", false, 101)]
        public static void ClearAll()
        {
            var tags = Object.FindObjectsOfType<BakedFlockTag>();
            if (tags == null || tags.Length == 0)
            {
                EditorUtility.DisplayDialog("Bird Flock – Clear",
                    "No baked flocks found in the scene.", "OK");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog("Bird Flock – Clear",
                "Remove all " + tags.Length + " baked flock group(s) and re-enable their spawners?",
                "Yes, clear", "Cancel");

            if (!confirmed) return;

            foreach (var tag in tags)
            {
                if (tag.sourceSpawner != null)
                {
                    Undo.RecordObject(tag.sourceSpawner, "Re-enable Bird Spawner");
                    tag.sourceSpawner.enabled = true;
                }
                Undo.DestroyObjectImmediate(tag.gameObject);
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[BirdFlock] Cleared baked flocks. Spawners re-enabled.");
        }

        // ── Validate: disable menu items during Play mode ───────────────
        [MenuItem("Tools/chickenandribs/Bird Flock/Bake All Flocks in Scene", true, 100)]
        static bool ValidateBake()  { return !Application.isPlaying; }

        [MenuItem("Tools/chickenandribs/Bird Flock/Clear Baked Flocks", true, 101)]
        static bool ValidateClear() { return !Application.isPlaying; }

        // ────────────────────────────────────────────────────────────────
        // Core bake logic
        // ────────────────────────────────────────────────────────────────

        private static int BakeSpawner(BirdSpawner spawner)
        {
            BirdPath path     = spawner.birdPath;
            float    avgSpd   = (spawner.minSpeed + spawner.maxSpeed) * 0.5f;
            if (avgSpd < 0.1f) avgSpd = 11f;

            float fullDist = path.TotalTravelDistance;
            float avgWait  = (spawner.minInterval + spawner.maxInterval) * 0.5f;

            // Container must stay at world origin so localPosition == worldPosition
            string     containerName = "BakedFlocks_" + spawner.name;
            GameObject container     = new GameObject(containerName);
            container.transform.position   = Vector3.zero;
            container.transform.rotation   = Quaternion.identity;
            container.transform.localScale = Vector3.one;
            Undo.RegisterCreatedObjectUndo(container, "Bake Bird Flocks");

            BakedFlockTag tag = container.AddComponent<BakedFlockTag>();
            tag.sourceSpawner = spawner;

            for (int i = 0; i < FlocksPerSpawner; i++)
            {
                GameObject flock;

                if (spawner.useBuiltInBirds)
                {
                    // BirdBuilder.BuildFlock uses Random internally, so each call
                    // produces a different V formation – visual variety across the loop.
                    flock = BirdBuilder.BuildFlock(spawner.flightPattern, spawner.numberOfBirds);
                    flock.name = "Flock_" + i;
                    flock.transform.SetParent(container.transform, false);
                    SaveMeshAssets(flock, i);
                    SaveMaterialAssets(flock, i);
                }
                else
                {
                    GameObject[] validPrefabs = System.Array.FindAll(
                        spawner.customBirdPrefabs ?? new GameObject[0], p => p != null);
                    if (validPrefabs.Length == 0)
                    {
                        Debug.LogWarning("[BirdFlock] Skipping flock " + i + " for spawner '" +
                            spawner.name + "': no bird prefabs assigned.");
                        continue;
                    }
                    // Build formation positions and instantiate a randomly chosen prefab at each slot
                    Vector3[] offsets = BirdBuilder.GetFormationOffsets(
                        spawner.flightPattern, spawner.numberOfBirds);
                    flock = new GameObject("Flock_" + i);
                    flock.transform.SetParent(container.transform, false);
                    for (int b = 0; b < offsets.Length; b++)
                    {
                        GameObject prefab = validPrefabs[
                            UnityEngine.Random.Range(0, validPrefabs.Length)];
                        GameObject bird = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                        bird.name = "Bird_" + b;
                        bird.transform.SetParent(flock.transform, false);
                        bird.transform.localPosition = offsets[b];
                    }
                }

                BakeAudio(flock, spawner);

                // Movement clip: flight + teleport + wait gap + stagger return
                AnimationClip clip = CreatePathClip(
                    path, i, FlocksPerSpawner, avgSpd, fullDist, avgWait,
                    spawner.speedVariation);
                clip.name = spawner.name + "_Flock_" + i;
                string clipPath = AssetDatabase.GenerateUniqueAssetPath(
                    AnimFolder + "/" + clip.name + ".anim");
                AssetDatabase.CreateAsset(clip, clipPath);

                // Unity's built-in Animation component – no custom scripts at runtime
                Animation anim = flock.AddComponent<Animation>();
                anim.AddClip(clip, "path");
                anim.clip              = clip;
                anim.playAutomatically = true;
                anim.wrapMode          = WrapMode.Loop;

                // Set initial world position to the flock's stagger start
                float      startDist = ((float)i / FlocksPerSpawner) * fullDist;
                Vector3    startPos;
                Quaternion startRot;
                path.Sample(startDist, out startPos, out startRot);
                flock.transform.position = startPos;
                flock.transform.rotation = startRot;
            }

            Undo.RecordObject(spawner, "Disable Bird Spawner After Bake");
            spawner.enabled = false;

            Debug.Log("[BirdFlock] Baked flock for spawner '" + spawner.name + "'.  " +
                "Cycle duration: " +
                System.Math.Round(fullDist / avgSpd + avgWait, 1) + " s.");

            return FlocksPerSpawner;
        }

        // ────────────────────────────────────────────────────────────────
        // Build one AnimationClip for a single baked flock.
        //
        // All flocks share the same clipDuration so they stay in sync after
        // looping.  Staggering is achieved by shifting each flock's start
        // position along the flight path.
        // ────────────────────────────────────────────────────────────────
        // Returns normalized time [0,1] for a given progress [0,1] through a segment,
        // with sinusoidal speed variation. The integral over a full period is always 1
        // so clip duration is preserved exactly regardless of variation amount.
        private static float VaryingProgress(float progress, float variation, float phase)
        {
            return progress - (variation / (2f * Mathf.PI)) *
                   (Mathf.Cos(2f * Mathf.PI * progress + phase) - Mathf.Cos(phase));
        }

        private static AnimationClip CreatePathClip(
            BirdPath path, int flockIndex, int flockCount,
            float avgSpeed, float fullDist, float avgWait,
            float speedVariation = 0f)
        {
            // Stagger start: flock i begins at a fraction of the way through its flight
            float startDist    = ((float)flockIndex / flockCount) * fullDist;
            float travelTime   = fullDist / avgSpeed;
            float clipDuration = travelTime + 0.016f + avgWait;

            // Sample approximately every 0.5 m along the path (up to 200 keyframes)
            float sampleStep = Mathf.Max(0.5f, fullDist / 200f);

            AnimationCurve px = new AnimationCurve();
            AnimationCurve py = new AnimationCurve();
            AnimationCurve pz = new AnimationCurve();
            AnimationCurve qx = new AnimationCurve();
            AnimationCurve qy = new AnimationCurve();
            AnimationCurve qz = new AnimationCurve();
            AnimationCurve qw = new AnimationCurve();

            // Each flock gets a different phase so their speed patterns don't sync up
            float phase = flockIndex * Mathf.PI * 1.3f;

            // ── Segment A: startDist → fullDist (flight to despawn) ───────
            float segALen = fullDist - startDist;
            float tA      = segALen / avgSpeed;   // total segment time — preserved exactly

            float d = startDist;
            while (d < fullDist)
            {
                float progress = (segALen > 0.01f) ? (d - startDist) / segALen : 0f;
                float t = tA * VaryingProgress(progress, speedVariation, phase);
                AppendSample(path, d, t, px, py, pz, qx, qy, qz, qw);
                d += sampleStep;
            }
            AppendSample(path, fullDist, tA, px, py, pz, qx, qy, qz, qw);

            // ── Teleport: jump to spawn point (0.016 s, both ends off-screen) ─
            float tTeleport = tA + 0.016f;
            AppendSample(path, 0f, tTeleport, px, py, pz, qx, qy, qz, qw);

            // ── Wait: hold at spawn for avgWait seconds ───────────────────
            // Two keyframes at the same spawn position → no movement during gap.
            float tWaitEnd = tTeleport + avgWait;
            AppendSample(path, 0f, tWaitEnd, px, py, pz, qx, qy, qz, qw);

            // ── Segment B: 0 → startDist (return to stagger position) ────
            if (startDist > 0.01f)
            {
                float tB = startDist / avgSpeed;
                float bd = sampleStep;
                while (bd < startDist)
                {
                    float progress = bd / startDist;
                    float t = tWaitEnd + tB * VaryingProgress(progress, speedVariation, phase);
                    AppendSample(path, bd, t, px, py, pz, qx, qy, qz, qw);
                    bd += sampleStep;
                }
            }

            // ── Final key: must equal t=0 position for a seamless loop ───
            AppendSample(path, startDist, clipDuration, px, py, pz, qx, qy, qz, qw);

            SetLinear(px); SetLinear(py); SetLinear(pz);
            SetLinear(qx); SetLinear(qy); SetLinear(qz); SetLinear(qw);

            // ── Scale: hide flock during wait-at-spawn so it is never visible ─
            // The spawn lead point may still be in camera view (especially for
            // large billboard quads), so we snap scale to zero at the teleport
            // moment and snap back to one when movement resumes.
            AnimationCurve sx = new AnimationCurve();
            AnimationCurve sy = new AnimationCurve();
            AnimationCurve sz = new AnimationCurve();

            sx.AddKey(0f,           1f); sy.AddKey(0f,           1f); sz.AddKey(0f,           1f);
            sx.AddKey(tA,           0f); sy.AddKey(tA,           0f); sz.AddKey(tA,           0f);
            sx.AddKey(tWaitEnd,     1f); sy.AddKey(tWaitEnd,     1f); sz.AddKey(tWaitEnd,     1f);
            sx.AddKey(clipDuration, 1f); sy.AddKey(clipDuration, 1f); sz.AddKey(clipDuration, 1f);

            SetConstant(sx); SetConstant(sy); SetConstant(sz);

            AnimationClip clip = new AnimationClip();
            clip.legacy   = true;           // required for the Animation component
            clip.wrapMode = WrapMode.Loop;

            // Path "" targets the Transform on the same GameObject as the Animation component.
            // Container is at world origin, so localPosition == worldPosition.
            clip.SetCurve("", typeof(Transform), "localPosition.x", px);
            clip.SetCurve("", typeof(Transform), "localPosition.y", py);
            clip.SetCurve("", typeof(Transform), "localPosition.z", pz);
            clip.SetCurve("", typeof(Transform), "localRotation.x", qx);
            clip.SetCurve("", typeof(Transform), "localRotation.y", qy);
            clip.SetCurve("", typeof(Transform), "localRotation.z", qz);
            clip.SetCurve("", typeof(Transform), "localRotation.w", qw);
            clip.SetCurve("", typeof(Transform), "localScale.x",    sx);
            clip.SetCurve("", typeof(Transform), "localScale.y",    sy);
            clip.SetCurve("", typeof(Transform), "localScale.z",    sz);

            return clip;
        }

        // ────────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────────

        private static void AppendSample(
            BirdPath path, float dist, float time,
            AnimationCurve px, AnimationCurve py, AnimationCurve pz,
            AnimationCurve qx, AnimationCurve qy, AnimationCurve qz, AnimationCurve qw)
        {
            Vector3    pos;
            Quaternion rot;
            path.Sample(dist, out pos, out rot);

            // Unity interpolates quaternion curves component-by-component (not SLERP).
            // If consecutive keyframes are on opposite hemispheres (dot < 0), linear
            // interpolation goes the long way around, causing a 360° spin glitch.
            // Negating the quaternion gives the same rotation on the same hemisphere.
            if (qw.length > 0)
            {
                float dot = qx[qx.length - 1].value * rot.x
                          + qy[qy.length - 1].value * rot.y
                          + qz[qz.length - 1].value * rot.z
                          + qw[qw.length - 1].value * rot.w;
                if (dot < 0f)
                {
                    rot.x = -rot.x;
                    rot.y = -rot.y;
                    rot.z = -rot.z;
                    rot.w = -rot.w;
                }
            }

            px.AddKey(time, pos.x);
            py.AddKey(time, pos.y);
            pz.AddKey(time, pos.z);
            qx.AddKey(time, rot.x);
            qy.AddKey(time, rot.y);
            qz.AddKey(time, rot.z);
            qw.AddKey(time, rot.w);
        }

        private static void SetLinear(AnimationCurve curve)
        {
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(
                    curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(
                    curve, i, AnimationUtility.TangentMode.Linear);
            }
        }

        private static void SetConstant(AnimationCurve curve)
        {
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(
                    curve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(
                    curve, i, AnimationUtility.TangentMode.Constant);
            }
        }

        private static void SaveMeshAssets(GameObject root, int flockIndex)
        {
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                // Skip meshes already saved as project assets (shared cache meshes)
                if (AssetDatabase.GetAssetPath(mf.sharedMesh) != "") continue;

                string meshName = mf.sharedMesh.name;
                if (string.IsNullOrEmpty(meshName)) meshName = "BF_Mesh";

                string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                    MeshFolder + "/" + meshName + "_Flock" + flockIndex + ".asset");
                AssetDatabase.CreateAsset(mf.sharedMesh, assetPath);
            }
        }

        private static void SaveMaterialAssets(GameObject root, int flockIndex)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var mr in renderers)
            {
                Material[] mats = mr.sharedMaterials;
                for (int mi = 0; mi < mats.Length; mi++)
                {
                    if (mats[mi] == null) continue;
                    // Skip materials already saved (the shared BF_Bird material
                    // is saved on the first flock and reused by all subsequent ones)
                    if (AssetDatabase.GetAssetPath(mats[mi]) != "") continue;

                    string matName = mats[mi].name;
                    if (string.IsNullOrEmpty(matName)) matName = "BF_Mat";

                    string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                        MatFolder + "/" + matName + ".mat");
                    AssetDatabase.CreateAsset(mats[mi], assetPath);
                }
            }
        }

        private static void BakeAudio(GameObject flock, BirdSpawner spawner)
        {
            if (spawner.birdAudioClip == null) return;

            // Honor an existing clip on the flock root (same priority logic as runtime ApplyAudio)
            AudioSource existing = flock.GetComponent<AudioSource>();
            if (existing != null && existing.clip != null) return;

            // The clip must be a saved project asset – in-memory clips are lost on export
            string clipPath = AssetDatabase.GetAssetPath(spawner.birdAudioClip);
            if (string.IsNullOrEmpty(clipPath))
            {
                Debug.LogWarning(
                    "[BirdFlock] Flock '" + flock.name + "': birdAudioClip is not a saved project asset " +
                    "(assign an audio file from your Project window, not an in-memory clip).", spawner);
                return;
            }

            AudioSource src = (existing != null) ? existing : flock.AddComponent<AudioSource>();
            src.clip         = spawner.birdAudioClip;
            src.volume       = spawner.audioVolume;
            src.loop         = true;
            src.playOnAwake  = true;
            src.spatialBlend = 1f;
            src.dopplerLevel = 0f;
            src.maxDistance  = spawner.audioMaxDistance;
            src.rolloffMode  = AudioRolloffMode.Linear;
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(RootFolder))
                AssetDatabase.CreateFolder("Assets/BirdFlock", "Baked");
            if (!AssetDatabase.IsValidFolder(MeshFolder))
                AssetDatabase.CreateFolder(RootFolder, "Meshes");
            if (!AssetDatabase.IsValidFolder(MatFolder))
                AssetDatabase.CreateFolder(RootFolder, "Materials");
            if (!AssetDatabase.IsValidFolder(AnimFolder))
                AssetDatabase.CreateFolder(RootFolder, "Animations");
        }
    }
}
