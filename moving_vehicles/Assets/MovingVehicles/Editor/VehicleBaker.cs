// VERSION MARKER: 2026-10-01 — includes the quaternion hemisphere-sign fix
// (search "360°" below) that prevents vehicles from doing a full spin at
// sharp-turn waypoints. If this line isn't at the very top of your copy of
// this file, you have an older version and should update it.
//
// VehicleBaker.cs  –  Editor only
// ──────────────────────────────────────────────────────────────────────────────
// WHY THIS EXISTS
// GSPro (and other games that load courses via asset bundles) cannot execute
// custom MonoBehaviour scripts at runtime.  This tool converts the runtime-
// spawner approach into a fully baked scene:
//
//   • Vehicle meshes  → saved as .asset files in the project
//   • Vehicle materials → saved as .mat files in the project
//   • Vehicle movement → baked into AnimationClip .anim files;
//                        played by Unity's built-in Animation component
//                        (no custom scripts needed at runtime)
//
// HOW TO USE
//  1.  Set up your VehiclePath + VehicleSpawner as normal and verify the
//      vehicles work in Play mode.
//  2.  Stop Play.
//  3.  Menu  →  Tools  →  chickenandribs  →  Moving Vehicles  →  Bake All Vehicles in Scene
//  4.  Save the scene (Ctrl+S).
//  5.  Export / build your course as usual.
//
// TO ITERATE
//  Menu  →  Tools  →  chickenandribs  →  Moving Vehicles  →  Clear Baked Vehicles
//  This removes baked vehicles and re-enables the spawner so you can
//  tweak settings and bake again.
// ──────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MovingVehicles
{
    public static class VehicleBaker
    {
        // ── Asset save locations ────────────────────────────────────────
        private const string RootFolder  = "Assets/MovingVehicles/Baked";
        private const string MeshFolder  = RootFolder + "/Meshes";
        private const string MatFolder   = RootFolder + "/Materials";
        private const string AnimFolder  = RootFolder + "/Animations";

        // ── Menu items ──────────────────────────────────────────────────
        [MenuItem("Tools/chickenandribs/Moving Vehicles/Bake All Vehicles in Scene", false, 100)]
        public static void BakeAll()
        {
            var spawners = Object.FindObjectsOfType<VehicleSpawner>();
            if (spawners == null || spawners.Length == 0)
            {
                EditorUtility.DisplayDialog("Moving Vehicles – Bake",
                    "No VehicleSpawner components found in the currently open scene.", "OK");
                return;
            }

            // Check each spawner has a path
            bool anyReady = false;
            foreach (var s in spawners)
            {
                if (s.vehiclePath != null) { anyReady = true; break; }
            }
            if (!anyReady)
            {
                EditorUtility.DisplayDialog("Moving Vehicles – Bake",
                    "Found VehicleSpawner(s) but none have a VehiclePath assigned.", "OK");
                return;
            }

            EnsureFolders();

            int total = 0;
            foreach (var spawner in spawners)
            {
                if (spawner.vehiclePath == null)
                {
                    Debug.LogWarning("[MovingVehicles] Skipping spawner '" +
                        spawner.name + "': no VehiclePath assigned.");
                    continue;
                }

                if (spawner.vehiclePath.WaypointCount < 2)
                {
                    Debug.LogWarning("[MovingVehicles] Skipping spawner '" +
                        spawner.name + "': path has fewer than 2 waypoints.");
                    continue;
                }

                total += BakeSpawner(spawner);
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            EditorUtility.DisplayDialog("Moving Vehicles – Bake Complete",
                total + " vehicles baked.\n\n" +
                "Assets saved to  " + RootFolder + "\n\n" +
                "IMPORTANT: Save your scene (Ctrl+S) before exporting to GSPro.",
                "OK");
        }

        [MenuItem("Tools/chickenandribs/Moving Vehicles/Clear Baked Vehicles", false, 101)]
        public static void ClearAll()
        {
            var tags = Object.FindObjectsOfType<BakedVehicleTag>();
            if (tags == null || tags.Length == 0)
            {
                EditorUtility.DisplayDialog("Moving Vehicles – Clear",
                    "No baked vehicles found in the scene.", "OK");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog("Moving Vehicles – Clear",
                "Remove all " + tags.Length + " baked vehicle group(s) and re-enable their spawners?",
                "Yes, clear", "Cancel");

            if (!confirmed) return;

            foreach (var tag in tags)
            {
                // Re-enable the source spawner
                if (tag.sourceSpawner != null)
                {
                    Undo.RecordObject(tag.sourceSpawner, "Re-enable Spawner");
                    tag.sourceSpawner.enabled = true;
                }
                Undo.DestroyObjectImmediate(tag.gameObject);
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[MovingVehicles] Cleared baked vehicles. Spawners re-enabled.");
        }

        // ── Validate menu items ─────────────────────────────────────────
        [MenuItem("Tools/chickenandribs/Moving Vehicles/Bake All Vehicles in Scene", true, 100)]
        static bool ValidateBake()
        {
            return Application.isPlaying == false;
        }

        [MenuItem("Tools/chickenandribs/Moving Vehicles/Clear Baked Vehicles", true, 101)]
        static bool ValidateClear()
        {
            return Application.isPlaying == false;
        }

        // ────────────────────────────────────────────────────────────────
        // Core bake logic
        // ────────────────────────────────────────────────────────────────

        private static int BakeSpawner(VehicleSpawner spawner)
        {
            VehiclePath path    = spawner.vehiclePath;
            int         count   = Mathf.Max(1, spawner.maxVehicles);
            float       avgSpd  = (spawner.minSpeed + spawner.maxSpeed) * 0.5f;
            if (avgSpd < 0.1f) avgSpd = 11f;
            float       fullDist = path.TotalTravelDistance;

            // Container – must stay at world origin so local == world for animations
            string containerName = "BakedVehicles_" + spawner.name;
            GameObject container = new GameObject(containerName);
            container.transform.position   = Vector3.zero;
            container.transform.rotation   = Quaternion.identity;
            container.transform.localScale = Vector3.one;
            Undo.RegisterCreatedObjectUndo(container, "Bake Vehicles");

            BakedVehicleTag tag = container.AddComponent<BakedVehicleTag>();
            tag.sourceSpawner = spawner;

            // ── Prefab list for custom mode ────────────────────────────────
            List<GameObject> validPrefabs = null;
            if (!spawner.useBuiltInVehicles)
            {
                validPrefabs = new List<GameObject>();
                if (spawner.customVehiclePrefabs != null)
                    foreach (var p in spawner.customVehiclePrefabs)
                        if (p != null) validPrefabs.Add(p);

                if (validPrefabs.Count == 0)
                {
                    Debug.LogWarning(
                        "[MovingVehicles] Spawner '" + spawner.name +
                        "' has Use Built-in Vehicles OFF but no valid Custom Prefabs. Skipping.");
                    return 0;
                }
            }

            // Weighted type pool (only used in built-in mode)
            int[] typePool = BuildTypePool(spawner);

            for (int i = 0; i < count; i++)
            {
                GameObject model;

                Quaternion modelRotOffset;

                if (spawner.useBuiltInVehicles)
                {
                    // ── Procedural vehicle ─────────────────────────────────
                    VehicleBuilder.VehicleType vType =
                        (VehicleBuilder.VehicleType)typePool[i % typePool.Length];

                    Color vColor = (spawner.colorPool.colors != null &&
                                    spawner.colorPool.colors.Length > 0)
                        ? spawner.colorPool.colors[i % spawner.colorPool.colors.Length]
                        : Color.gray;

                    model = VehicleBuilder.Build(vType, vColor);
                    model.name = "Vehicle_" + i + "_" + vType;
                    modelRotOffset = Quaternion.identity;  // procedural vehicles face +Z by design

                    // Persist in-memory meshes and materials so they survive export
                    SaveMeshAssets(model);
                    SaveMaterialAssets(model, i);
                }
                else
                {
                    // ── Custom prefab ──────────────────────────────────────
                    // Use PrefabUtility so the prefab connection is preserved in
                    // the scene and the bundle references the prefab assets directly.
                    GameObject prefab = validPrefabs[i % validPrefabs.Count];
                    model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    model.name = "Vehicle_" + i + "_Custom";
                    // Capture the prefab's natural orientation BEFORE overriding it.
                    // Kept as a static child-local rotation (see wrapper below) so the
                    // model's import-time axis correction survives without needing to
                    // be baked into every animated keyframe.
                    modelRotOffset = model.transform.localRotation;
                    // No mesh/material saving needed – assets are already in the prefab
                }

                // ── Wrap the model in an empty parent that owns movement ────
                // Some models (e.g. animated characters from blender_animation_creator)
                // already carry their own Animator driving an in-place animation
                // (running, idling, etc.). GSPro does not tolerate an Animator and
                // an Animation component coexisting on the same GameObject, so the
                // baked movement Animation always lives one level up on an empty
                // wrapper instead of on the model itself — mirroring how BirdFlock
                // separates its flock-movement container from each bird's own
                // wing-flap Animator.
                GameObject vehicle = new GameObject("Vehicle_" + i);
                vehicle.transform.SetParent(container.transform, false);
                model.transform.SetParent(vehicle.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = modelRotOffset;

                // ── Create and save movement AnimationClip ─────────────────
                AnimationClip clip = CreatePathClip(path, i, count, avgSpd, fullDist);
                clip.name = spawner.name + "_Vehicle_" + i;
                string clipPath = AssetDatabase.GenerateUniqueAssetPath(
                    AnimFolder + "/" + clip.name + ".anim");
                AssetDatabase.CreateAsset(clip, clipPath);

                // ── Attach the Animation component (Unity built-in, legacy) ─
                // No custom scripts needed at runtime.
                Animation anim = vehicle.AddComponent<Animation>();
                anim.AddClip(clip, "path");
                anim.clip              = clip;
                anim.playAutomatically = true;
                anim.wrapMode          = WrapMode.Loop;

                // ── Bake audio onto the vehicle ────────────────────────────
                // AudioSource is a built-in Unity component – it survives asset
                // bundle export and works in GSPro with no custom scripts needed.
                // Looks in children too, since a custom prefab's own AudioSource
                // now lives on 'model', one level below the new wrapper.
                BakeAudio(vehicle, spawner);

                // ── Set initial world position ──────────────────────────────
                float      startDist = ((float)i / count) * fullDist;
                Vector3    startPos;
                Quaternion startRot;
                path.Sample(startDist, out startPos, out startRot);
                vehicle.transform.position = startPos;
                vehicle.transform.rotation = startRot;
            }

            // Disable the runtime spawner – vehicles are now baked
            Undo.RecordObject(spawner, "Disable Spawner After Bake");
            spawner.enabled = false;

            Debug.Log("[MovingVehicles] Baked " + count + " vehicles for spawner '" +
                spawner.name + "'.");
            return count;
        }

        // ────────────────────────────────────────────────────────────────
        // AnimationClip creation
        // ────────────────────────────────────────────────────────────────
        //
        // Each vehicle gets a clip that covers one full cycle:
        //
        //   Segment A  (startDist → fullDist) : vehicle travels to the despawn point
        //   Teleport   (0.016 s)              : instant jump from despawn to spawn
        //                                       (both are off-screen → invisible)
        //   Segment B  (0 → startDist)        : vehicle travels back to its start
        //
        // All N vehicles have the same total clip duration so they maintain
        // their spacing after looping.
        // ────────────────────────────────────────────────────────────────

        private static AnimationClip CreatePathClip(
            VehiclePath path, int vehicleIndex, int vehicleCount,
            float avgSpeed, float fullDist)
        {
            float startDist    = ((float)vehicleIndex / vehicleCount) * fullDist;
            float totalStop    = path.TotalStopTime;
            float cycleTime    = fullDist / avgSpeed;
            float clipDuration = cycleTime + totalStop + 0.016f;   // same for every vehicle

            // Sample approximately every 0.5 m (up to 200 keyframes)
            float sampleStep = Mathf.Max(0.5f, fullDist / 200f);

            // Build stop events in travel-distance space (lead + path-local dist)
            float   lead          = path.spawnLeadDistance;
            float[] wpPathDists   = path.WaypointDists;
            float[] wpStopDurs    = path.StopDurations;
            float[] wpTravelDists = new float[wpPathDists.Length];
            for (int i = 0; i < wpPathDists.Length; i++)
                wpTravelDists[i] = lead + wpPathDists[i];

            AnimationCurve px = new AnimationCurve();
            AnimationCurve py = new AnimationCurve();
            AnimationCurve pz = new AnimationCurve();
            AnimationCurve qx = new AnimationCurve();
            AnimationCurve qy = new AnimationCurve();
            AnimationCurve qz = new AnimationCurve();
            AnimationCurve qw = new AnimationCurve();

            // ── Segment A: startDist → fullDist ──────────────────────────
            // stopAccA accumulates stop time for waypoints crossed in this segment.
            float stopAccA  = 0f;
            int   nextStopA = FirstWpAtOrAfter(wpTravelDists, startDist);

            // First sample at segment start (t = 0)
            AppendSample(path, startDist, 0f, px, py, pz, qx, qy, qz, qw);
            float d = startDist + sampleStep;

            while (d < fullDist)
            {
                stopAccA = EmitStopsBefore(path, d, wpTravelDists, wpStopDurs,
                                           ref nextStopA, startDist, stopAccA, avgSpeed,
                                           0f, px, py, pz, qx, qy, qz, qw);
                AppendSample(path, d, (d - startDist) / avgSpeed + stopAccA,
                             px, py, pz, qx, qy, qz, qw);
                d += sampleStep;
            }

            // Stops between last regular sample and fullDist
            stopAccA = EmitStopsBefore(path, fullDist, wpTravelDists, wpStopDurs,
                                       ref nextStopA, startDist, stopAccA, avgSpeed,
                                       0f, px, py, pz, qx, qy, qz, qw);

            // Exact endpoint of Segment A
            float tEndA = (fullDist - startDist) / avgSpeed + stopAccA;
            AppendSample(path, fullDist, tEndA, px, py, pz, qx, qy, qz, qw);

            // ── Teleport: spawn position at tEndA + 0.016 s ─────────────
            float tTeleport = tEndA + 0.016f;
            AppendSample(path, 0f, tTeleport, px, py, pz, qx, qy, qz, qw);

            // ── Segment B: 0 → startDist (skip when vehicle starts at spawn)
            if (startDist > 0.01f)
            {
                float stopAccB  = 0f;
                int   nextStopB = 0;   // start from first waypoint
                float bd        = sampleStep;

                while (bd < startDist)
                {
                    stopAccB = EmitStopsBefore(path, bd, wpTravelDists, wpStopDurs,
                                               ref nextStopB, 0f, stopAccB, avgSpeed,
                                               tTeleport, px, py, pz, qx, qy, qz, qw);
                    AppendSample(path, bd, tTeleport + bd / avgSpeed + stopAccB,
                                 px, py, pz, qx, qy, qz, qw);
                    bd += sampleStep;
                }

                // Stops between last regular sample and startDist
                EmitStopsBefore(path, startDist, wpTravelDists, wpStopDurs,
                                ref nextStopB, 0f, stopAccB, avgSpeed,
                                tTeleport, px, py, pz, qx, qy, qz, qw);
            }

            // ── Final key at clipDuration = identical position to t=0 ───
            // Ensures seamless loop: clip end == clip start.
            AppendSample(path, startDist, clipDuration, px, py, pz, qx, qy, qz, qw);

            // ── Linear tangents throughout (no easing) ──────────────────
            SetLinear(px); SetLinear(py); SetLinear(pz);
            SetLinear(qx); SetLinear(qy); SetLinear(qz); SetLinear(qw);

            // ── Build clip ───────────────────────────────────────────────
            AnimationClip clip = new AnimationClip();
            clip.legacy    = true;          // Required for Animation (not Animator)
            clip.wrapMode  = WrapMode.Loop;

            // "" = the Transform on the GameObject that owns this Animation component
            // Since the container is at world origin, localPosition == worldPosition.
            clip.SetCurve("", typeof(Transform), "localPosition.x", px);
            clip.SetCurve("", typeof(Transform), "localPosition.y", py);
            clip.SetCurve("", typeof(Transform), "localPosition.z", pz);
            clip.SetCurve("", typeof(Transform), "localRotation.x", qx);
            clip.SetCurve("", typeof(Transform), "localRotation.y", qy);
            clip.SetCurve("", typeof(Transform), "localRotation.z", qz);
            clip.SetCurve("", typeof(Transform), "localRotation.w", qw);

            return clip;
        }

        // Emits arrive+depart keyframe pairs for any stop waypoints with travel distance
        // strictly less than 'beforeDist'.  Returns the updated accumulated stop time.
        // segTBase = tTeleport for Segment B, 0 for Segment A.
        // segDStart = startDist for Segment A, 0 for Segment B.
        private static float EmitStopsBefore(
            VehiclePath path, float beforeDist,
            float[] wpTravelDists, float[] wpStopDurs,
            ref int nextIdx, float segDStart, float stopAcc, float avgSpeed,
            float segTBase,
            AnimationCurve px, AnimationCurve py, AnimationCurve pz,
            AnimationCurve qx, AnimationCurve qy, AnimationCurve qz, AnimationCurve qw)
        {
            while (nextIdx < wpTravelDists.Length && wpTravelDists[nextIdx] < beforeDist)
            {
                float sd  = wpTravelDists[nextIdx];
                float dur = wpStopDurs[nextIdx];
                nextIdx++;
                if (dur <= 0f) continue;

                float tArrive = segTBase + (sd - segDStart) / avgSpeed + stopAcc;
                AppendSample(path, sd, tArrive,       px, py, pz, qx, qy, qz, qw);
                stopAcc += dur;
                AppendSample(path, sd, tArrive + dur, px, py, pz, qx, qy, qz, qw);
            }
            return stopAcc;
        }

        // Returns the index of the first waypoint with travel distance >= d.
        private static int FirstWpAtOrAfter(float[] wpTravelDists, float d)
        {
            for (int i = 0; i < wpTravelDists.Length; i++)
                if (wpTravelDists[i] >= d) return i;
            return wpTravelDists.Length;
        }

        // ── Sample path and append one keyframe to all 7 curves ──────────
        private static void AppendSample(
            VehiclePath path, float dist, float time,
            AnimationCurve px, AnimationCurve py, AnimationCurve pz,
            AnimationCurve qx, AnimationCurve qy, AnimationCurve qz, AnimationCurve qw)
        {
            Vector3    pos;
            Quaternion rot;
            path.Sample(dist, out pos, out rot);
            // The model's import-time axis correction (rotOffset) is applied as a
            // static child-local rotation instead of baked in here — see the
            // wrapper GameObject set up in BakeSpawner.

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

        // ── Set every keyframe in a curve to Linear tangent mode ──────────
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

        // ────────────────────────────────────────────────────────────────
        // Asset persistence helpers
        // ────────────────────────────────────────────────────────────────

        // Save any in-memory Mesh objects found in the hierarchy to .asset files.
        // Already-saved meshes (GetAssetPath != "") are skipped automatically.
        private static void SaveMeshAssets(GameObject root)
        {
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                if (AssetDatabase.GetAssetPath(mf.sharedMesh) != "") continue;

                string meshName = mf.sharedMesh.name;
                if (string.IsNullOrEmpty(meshName)) meshName = "Mesh_" + mf.gameObject.name;

                string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                    MeshFolder + "/" + meshName + ".asset");
                AssetDatabase.CreateAsset(mf.sharedMesh, assetPath);
            }
        }

        // Save any in-memory Material objects to .mat files.
        private static void SaveMaterialAssets(GameObject root, int vehicleIndex)
        {
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            foreach (MeshRenderer mr in renderers)
            {
                Material[] mats = mr.sharedMaterials;
                for (int mi = 0; mi < mats.Length; mi++)
                {
                    if (mats[mi] == null) continue;
                    if (AssetDatabase.GetAssetPath(mats[mi]) != "") continue;

                    // Rename body materials so each vehicle's color is identifiable
                    if (mats[mi].name == "MV_Body")
                        mats[mi].name = "MV_Body_Vehicle" + vehicleIndex;

                    string matName = mats[mi].name;
                    if (string.IsNullOrEmpty(matName))
                        matName = "Mat_" + mr.gameObject.name + "_" + mi;

                    string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                        MatFolder + "/" + matName + ".mat");
                    AssetDatabase.CreateAsset(mats[mi], assetPath);
                }
            }
        }

        // ────────────────────────────────────────────────────────────────
        // Misc helpers
        // ────────────────────────────────────────────────────────────────

        private static int[] BuildTypePool(VehicleSpawner spawner)
        {
            var pool = new List<int>();
            for (int i = 0; i < spawner.sedanWeight;      i++) pool.Add(0);
            for (int i = 0; i < spawner.hatchbackWeight;  i++) pool.Add(1);
            for (int i = 0; i < spawner.pickupWeight;     i++) pool.Add(2);
            for (int i = 0; i < spawner.tractorWeight;    i++) pool.Add(3);
            if (pool.Count == 0) pool.Add(0);
            return pool.ToArray();
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(RootFolder))
                AssetDatabase.CreateFolder("Assets/MovingVehicles", "Baked");

            if (!AssetDatabase.IsValidFolder(MeshFolder))
                AssetDatabase.CreateFolder(RootFolder, "Meshes");

            if (!AssetDatabase.IsValidFolder(MatFolder))
                AssetDatabase.CreateFolder(RootFolder, "Materials");

            if (!AssetDatabase.IsValidFolder(AnimFolder))
                AssetDatabase.CreateFolder(RootFolder, "Animations");
        }

        // ────────────────────────────────────────────────────────────────
        // Audio baking
        // Configures an AudioSource on a baked vehicle so it plays
        // automatically when the scene loads – no custom scripts needed.
        //
        // Priority:
        //   1. Existing AudioSource.clip on the vehicle/prefab  → kept as-is,
        //      only spatial/loop settings are enforced.
        //   2. VehicleSpawner.vehicleAudioClip                  → applied as
        //      fallback when the vehicle has no clip of its own.
        //
        // 'vehicle' is the empty movement wrapper, not the model itself, so a
        // custom prefab's own AudioSource lives one level down — search children.
        // ────────────────────────────────────────────────────────────────
        private static void BakeAudio(GameObject vehicle, VehicleSpawner spawner)
        {
            AudioSource src = vehicle.GetComponentInChildren<AudioSource>();

            if (src != null && src.clip != null)
            {
                // Prefab / existing source already has a clip – just enforce settings
                src.loop         = true;
                src.playOnAwake  = true;
                src.spatialBlend = 1.0f;
                src.dopplerLevel = 0.0f;
                src.pitch        = UnityEngine.Random.Range(0.93f, 1.07f);
                return;
            }

            // No existing clip – use the spawner's fallback
            if (spawner.vehicleAudioClip == null) return;

            // Verify the clip is a saved project asset (required for bundles)
            if (AssetDatabase.GetAssetPath(spawner.vehicleAudioClip) == "")
            {
                Debug.LogWarning(
                    "[MovingVehicles] VehicleSpawner '" + spawner.name +
                    "': vehicleAudioClip is not a saved project asset and cannot be " +
                    "included in an asset bundle.  Import the audio file into your project first.");
                return;
            }

            if (src == null) src = vehicle.AddComponent<AudioSource>();

            src.clip         = spawner.vehicleAudioClip;
            src.loop         = true;
            src.playOnAwake  = true;
            src.volume       = spawner.audioVolume;
            src.spatialBlend = 1.0f;
            src.minDistance  = 2.0f;
            src.maxDistance  = spawner.audioMaxDistance;
            src.rolloffMode  = AudioRolloffMode.Linear;
            src.dopplerLevel = 0.0f;
            src.pitch        = UnityEngine.Random.Range(0.93f, 1.07f);
        }
    }
}
