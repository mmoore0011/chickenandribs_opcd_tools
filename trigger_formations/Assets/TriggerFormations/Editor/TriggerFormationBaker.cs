// TriggerFormationBaker.cs  –  Editor only
// ──────────────────────────────────────────────────────────────────────────────
// WHY THIS EXISTS
// GSPro cannot execute custom MonoBehaviour scripts at runtime (same
// constraint documented in MovingVehicles/VehicleBaker.cs and
// BirdFlock/BirdBaker.cs), and its TriggerScript component needs an Animator
// with a Bool parameter to gate behavior. This tool bakes a
// TriggerFormationSpawner's settings into a reusable PREFAB (not a one-off
// scene bake) with an Animator that has two states — Idle (default, no
// motion) and Move (the baked path clip) — switched by that bool, so it
// drops straight into TriggerScript.MyObjectController / BoolConditionName.
//
// TWO-LEVEL HIERARCHY: "<name>_Formation" (root, holds BakedFormationTag,
// this is what you drag/position in a scene) > "Mover" (child, holds the
// Animator + all the soldiers). This is deliberate, not incidental: a
// parentless Transform's localPosition IS its world position, so the
// Animator can't live on the root itself — animating "localPosition"
// starting at (0,0,0) would snap straight to world origin the instant it
// played, instead of staying relative to wherever the root was placed. Point
// TriggerScript.MyObjectController at the Mover child's Animator, not the
// root's.
//
// Unlike VehicleBaker/BirdBaker, the baked movement clip stores
// POSITION/ROTATION RELATIVE TO THE MOVER'S PARENT (the root), not absolute
// world space pinned to the origin — because this is meant to be a normal,
// freely-placeable prefab, not a container that must never move.
//
// TIERED FIDELITY (GroupedObjects only)
// Only the front 'animatedCount' formation slots get a full, individually
// animated character instance. The rest are baked into static combined
// mesh(es) — a handful of frozen run-cycle poses, instanced across the
// remaining slots — so a 200-strong formation costs roughly animatedCount
// Animators instead of 200.
//
// HOW TO USE
//  1. Set up a TriggerFormationSpawner (path + character prefab + settings).
//  2. Menu → Tools → chickenandribs → Trigger Formations → Bake Selected
//     (or "Bake All In Scene"). This also instantiates the project's
//     Trigger.prefab as a child of the new scene instance and wires it
//     automatically (My Object Controller → the Mover's Animator, Bool
//     Condition Name → the spawner's Bool Parameter Name, Game Ball
//     Collision → true) — same mechanism TriggerEffectBaker uses. Only its
//     Box Collider needs sizing/positioning by hand over the trigger zone.
//  3. Because every bake creates a brand-new "<name>_Formation" scene
//     instance rather than updating a previous one, re-baking after tuning
//     the spawner means repeating step 2's placement on the new instance —
//     delete the stale one once the new one is positioned correctly.
//
// TO ITERATE
//  Menu → Tools → chickenandribs → Trigger Formations → Clear Baked Formation
// ──────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using System.Linq;
using MovingVehicles;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace TriggerFormations
{
    public static class TriggerFormationBaker
    {
        private const string RootFolder       = "Assets/TriggerFormations/Baked";
        private const string PrefabFolder     = RootFolder + "/Prefabs";
        private const string AnimFolder       = RootFolder + "/Animations";
        private const string ControllerFolder = RootFolder + "/Controllers";
        private const string MeshFolder       = RootFolder + "/Meshes";

        private const int PosesPerFillerBatch = 5;   // distinct frozen run-cycle poses sampled for variety
        private const int FillerBatchSize     = 50;  // filler instances combined per static mesh

        // ── Menu items ──────────────────────────────────────────────────
        [MenuItem("Tools/chickenandribs/Trigger Formations/Bake Selected", false, 100)]
        public static void BakeSelected()
        {
            var spawners = Selection.gameObjects
                .Select(go => go.GetComponentInParent<TriggerFormationSpawner>())
                .Where(s => s != null)
                .Distinct()
                .ToArray();

            if (spawners.Length == 0)
            {
                EditorUtility.DisplayDialog("Trigger Formations – Bake",
                    "Select a GameObject with a TriggerFormationSpawner first.", "OK");
                return;
            }

            int total = 0;
            foreach (var spawner in spawners)
                if (BakeSpawner(spawner)) total++;
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog("Trigger Formations – Bake Complete",
                total + " formation(s) baked.\n\nAssets saved to  " + RootFolder, "OK");
        }

        [MenuItem("Tools/chickenandribs/Trigger Formations/Bake All In Scene", false, 101)]
        public static void BakeAllInScene()
        {
            var spawners = Object.FindObjectsOfType<TriggerFormationSpawner>();
            if (spawners == null || spawners.Length == 0)
            {
                EditorUtility.DisplayDialog("Trigger Formations – Bake",
                    "No TriggerFormationSpawner components found in the currently open scene.", "OK");
                return;
            }

            int total = 0;
            foreach (var spawner in spawners)
                if (BakeSpawner(spawner)) total++;
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog("Trigger Formations – Bake Complete",
                total + " formation(s) baked.\n\nAssets saved to  " + RootFolder, "OK");
        }

        [MenuItem("Tools/chickenandribs/Trigger Formations/Clear Baked Formation", false, 102)]
        public static void ClearBakedFormation()
        {
            var tags = Object.FindObjectsOfType<BakedFormationTag>();
            if (tags == null || tags.Length == 0)
            {
                EditorUtility.DisplayDialog("Trigger Formations – Clear",
                    "No baked formations found in the scene.", "OK");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog("Trigger Formations – Clear",
                "Remove all " + tags.Length + " baked formation instance(s) from the scene? " +
                "(The saved prefab assets are not deleted.)",
                "Yes, clear", "Cancel");
            if (!confirmed) return;

            foreach (var tag in tags)
                Undo.DestroyObjectImmediate(tag.gameObject);

            Debug.Log("[TriggerFormations] Cleared baked formation instance(s) from the scene.");
        }

        [MenuItem("Tools/chickenandribs/Trigger Formations/Bake Selected", true, 100)]
        private static bool ValidateBakeSelected() { return !Application.isPlaying; }

        [MenuItem("Tools/chickenandribs/Trigger Formations/Bake All In Scene", true, 101)]
        private static bool ValidateBakeAll() { return !Application.isPlaying; }

        [MenuItem("Tools/chickenandribs/Trigger Formations/Clear Baked Formation", true, 102)]
        private static bool ValidateClear() { return !Application.isPlaying; }

        // ────────────────────────────────────────────────────────────────
        // Core bake logic
        // ────────────────────────────────────────────────────────────────

        private static bool BakeSpawner(TriggerFormationSpawner spawner)
        {
            if (spawner.path == null)
            {
                Debug.LogWarning("[TriggerFormations] Skipping '" + spawner.name + "': no Path assigned.");
                return false;
            }
            if (spawner.path.WaypointCount < 2)
            {
                Debug.LogWarning("[TriggerFormations] Skipping '" + spawner.name + "': path has fewer than 2 waypoints.");
                return false;
            }
            if (spawner.characterPrefab == null)
            {
                Debug.LogWarning("[TriggerFormations] Skipping '" + spawner.name + "': no Character Prefab assigned.");
                return false;
            }

            EnsureFolders();

            VehiclePath path = spawner.path;
            float fullDist = path.TotalTravelDistance;

            Vector3 spawnPos;
            Quaternion spawnRot;
            path.Sample(0f, out spawnPos, out spawnRot);

            // ── Build the temporary hierarchy at the path's spawn point ──
            //
            // Two levels, not one: a parentless Transform's localPosition IS its
            // world position, so a root-level Animator animating "localPosition"
            // starting at (0,0,0) would snap straight to world origin the instant
            // it plays — not "stay relative to wherever this was placed." The fix
            // is a real parent: 'root' is what you position/drag around (holds the
            // tag, never animated), 'mover' is a child one level in that actually
            // carries the Animator + movement clip, so its (0,0,0)-at-t=0 curve
            // correctly means "coincident with root" instead of "world origin."
            GameObject root = new GameObject(spawner.name + "_Formation");
            root.transform.position = spawnPos;
            root.transform.rotation = spawnRot;
            Undo.RegisterCreatedObjectUndo(root, "Bake Trigger Formation");
            root.AddComponent<BakedFormationTag>();   // sourceSpawner set on the scene instance later

            GameObject mover = new GameObject("Mover");
            mover.transform.SetParent(root.transform, false);
            mover.transform.localPosition = Vector3.zero;
            mover.transform.localRotation = Quaternion.identity;

            if (spawner.arrangement == TriggerFormationSpawner.Arrangement.SingleObject)
            {
                GameObject single = (GameObject)PrefabUtility.InstantiatePrefab(spawner.characterPrefab);
                single.transform.SetParent(mover.transform, false);
                single.transform.localPosition = Vector3.zero;
                single.transform.localRotation = Quaternion.identity;
            }
            else
            {
                Vector3[] offsets = FormationBuilder.GetGridOffsets(spawner.count, spawner.spacing);
                int animatedCount = Mathf.Clamp(spawner.animatedCount, 1, spawner.count);

                // Animate the slots closest to the formation's own center,
                // not simply the first 'animatedCount' entries (which
                // GetGridOffsets returns front-rank-first) — otherwise the
                // frozen filler mass always ends up clustered at the back,
                // which reads badly since it's a static, repeated pose.
                // Spreading filler to the perimeter (front/back/sides) and
                // keeping real animated instances in the visually central
                // slots hides the frozen mass better.
                Vector3 centroid = Vector3.zero;
                for (int i = 0; i < offsets.Length; i++) centroid += offsets[i];
                centroid /= offsets.Length;

                int[] indicesByDistance = new int[offsets.Length];
                for (int i = 0; i < offsets.Length; i++) indicesByDistance[i] = i;
                System.Array.Sort(indicesByDistance, (a, b) =>
                    (offsets[a] - centroid).sqrMagnitude.CompareTo((offsets[b] - centroid).sqrMagnitude));

                bool[] isAnimated = new bool[offsets.Length];
                for (int i = 0; i < animatedCount; i++)
                    isAnimated[indicesByDistance[i]] = true;

                for (int i = 0; i < offsets.Length; i++)
                {
                    if (!isAnimated[i]) continue;
                    GameObject member = (GameObject)PrefabUtility.InstantiatePrefab(spawner.characterPrefab);
                    member.name = "Animated_" + i;
                    member.transform.SetParent(mover.transform, false);
                    member.transform.localPosition = offsets[i];
                    member.transform.localRotation = Quaternion.identity;
                }

                if (animatedCount < offsets.Length)
                {
                    List<Vector3> fillerOffsets = new List<Vector3>();
                    for (int i = 0; i < offsets.Length; i++)
                        if (!isAnimated[i]) fillerOffsets.Add(offsets[i]);
                    BuildFillerBatches(spawner.characterPrefab, fillerOffsets, mover.transform);
                }
            }

            // ── Bake the movement clip (relative to root's own placement) ──
            float arrivalNormalizedTime;
            AnimationClip clip = CreateRelativeMovementClip(
                path, spawner.speed, fullDist, spawnPos, spawnRot, out arrivalNormalizedTime);
            clip.name = spawner.name + "_Move";
            string clipPath = AssetDatabase.GenerateUniqueAssetPath(AnimFolder + "/" + clip.name + ".anim");
            AssetDatabase.CreateAsset(clip, clipPath);

            // ── Build the trigger-gated AnimatorController ──
            string controllerPath = ControllerFolder + "/" + spawner.name + "Controller.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
                AssetDatabase.DeleteAsset(controllerPath);

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter(spawner.boolParameterName, AnimatorControllerParameterType.Bool);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            AnimatorState idleState = stateMachine.AddState("Idle");
            AnimatorState moveState = stateMachine.AddState("Move");
            moveState.motion = clip;
            stateMachine.defaultState = idleState;

            AnimatorStateTransition toMove = idleState.AddTransition(moveState);
            toMove.hasExitTime = false;
            toMove.duration = 0.1f;
            toMove.AddCondition(AnimatorConditionMode.If, 0, spawner.boolParameterName);

            // playCount > 0: after that many full runs of the path, permanently
            // stop in a dead-end "Done" state holding the arrival pose. Move
            // deliberately has NO transition back to Idle in this mode — only
            // a real GSPro TriggerScript exposed the reason why that matters:
            // it apparently only holds its bool true for roughly one clip's
            // length before releasing it, which is far shorter than a big
            // formation's actual travel+stop time. With a Move→Idle(bool false)
            // transition present, that early release would kick the group back
            // to Idle mid-march, and a later re-trigger would restart the whole
            // clip from the spawn point — the group appears to loop/respawn
            // forever instead of completing one charge. Removing that
            // transition makes Move uninterruptible once entered: it always
            // plays through to Done regardless of what the bool does
            // afterward. playCount <= 0 keeps the old patrol behavior instead
            // (Move CAN return to Idle on bool false, no Done state at all) —
            // appropriate there since interruptibility is the point of a
            // patrol, not a one-time triggered event.
            //
            // Exit Time is normalized to the MOVE clip's own length, where 1.0
            // means "one full loop." The clip's raw length also includes a
            // ~0.016s teleport-back-to-spawn tail after the arrival/hold point
            // (needed for seamless looping) — arrivalNormalizedTime is the exact
            // fraction where the clip reaches arrival, BEFORE that teleport, so
            // Done captures the held arrival pose instead of the snapped-back
            // spawn pose an instant later.
            if (spawner.playCount > 0)
            {
                // Done needs its OWN single-frame clip, not a reference to the
                // Move clip — a state always starts playing its motion from
                // time zero the instant it's entered, regardless of where the
                // previous state left off. Reusing the looping travel clip
                // here meant Done immediately restarted that whole multi-stage
                // "travel → arrive → hold → teleport back to spawn" sequence
                // from scratch the moment it was entered, and being a looping
                // clip, kept doing that forever — which is exactly the bug
                // reported ("starting a new one when it finishes"). A tiny
                // clip holding just the single arrival pose has nothing to
                // loop back to, so it just... holds.
                AnimatorState doneState = stateMachine.AddState("Done");
                AnimationClip heldPoseClip = CreateHeldPoseClip(path, fullDist, spawnPos, spawnRot);
                heldPoseClip.name = spawner.name + "_Held";
                string heldClipPath = AssetDatabase.GenerateUniqueAssetPath(AnimFolder + "/" + heldPoseClip.name + ".anim");
                AssetDatabase.CreateAsset(heldPoseClip, heldClipPath);
                doneState.motion = heldPoseClip;

                AnimatorStateTransition toDone = moveState.AddTransition(doneState);
                toDone.hasExitTime = true;
                toDone.exitTime = (spawner.playCount - 1) + arrivalNormalizedTime;
                toDone.duration = 0f;
            }
            else
            {
                AnimatorStateTransition toIdle = moveState.AddTransition(idleState);
                toIdle.hasExitTime = false;
                toIdle.duration = 0.1f;
                toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, spawner.boolParameterName);
            }

            EditorUtility.SetDirty(controller);

            // ── Attach the Animator (not Animation — see BAC/VehicleBaker fix notes) ──
            // On 'mover', not 'root' — see the hierarchy comment above.
            Animator animator = mover.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            BakeAudio(mover, spawner);

            // ── Save as a reusable prefab asset (2018.2 predates SaveAsPrefabAsset) ──
            string prefabPath = PrefabFolder + "/" + spawner.name + "_Formation.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                AssetDatabase.DeleteAsset(prefabPath);

            GameObject prefabAsset = PrefabUtility.CreatePrefab(prefabPath, root, ReplacePrefabOptions.ReplaceNameBased);
            bool prefabSuccess = prefabAsset != null;

            Object.DestroyImmediate(root);

            if (!prefabSuccess)
            {
                Debug.LogError("[TriggerFormations] Failed to save prefab at " + prefabPath + " for '" + spawner.name + "'.");
                return false;
            }

            // ── Leave a properly-linked instance in the scene at the spawn point ──
            GameObject sceneInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset);
            sceneInstance.name = spawner.name + "_Formation";
            sceneInstance.transform.position = spawnPos;
            sceneInstance.transform.rotation = spawnRot;
            Undo.RegisterCreatedObjectUndo(sceneInstance, "Bake Trigger Formation");
            sceneInstance.GetComponent<BakedFormationTag>().sourceSpawner = spawner;

            // ── Trigger child: wires this bake's Animator into GSPro's ball-
            // collision trigger, same mechanism/field names TriggerEffectBaker
            // uses. Every bake makes a brand-new sceneInstance (this tool never
            // reuses one across re-bakes), so this always adds a fresh Trigger
            // too — size/position its Box Collider over the ball-collision spot
            // after baking, same as you would the first time either way.
            //
            // Must look up the Animator fresh on sceneInstance here, NOT reuse
            // the 'animator' variable from earlier — that one lived on the
            // temporary root/mover hierarchy, already destroyed a few lines up
            // by Object.DestroyImmediate(root). Wiring that dangling reference
            // into the TriggerScript silently produced an empty field every
            // time, which is why "My Object Controller" always needed to be
            // set by hand after baking.
            Transform sceneMover = sceneInstance.transform.Find("Mover");
            Animator sceneAnimator = sceneMover != null ? sceneMover.GetComponent<Animator>() : null;
            AddAndWireTrigger(sceneInstance.transform, sceneAnimator, spawner.boolParameterName);

            Debug.Log("[TriggerFormations] Baked '" + spawner.name + "' → " + prefabPath +
                       "  |  Bool parameter: " + spawner.boolParameterName);
            return true;
        }

        // ────────────────────────────────────────────────────────────────
        // Movement clip — RELATIVE to the formation root's own placement,
        // so the prefab can be dropped anywhere and still trace the same
        // path shape from wherever it's placed. Loops via an instant
        // "teleport" back to distance 0, same technique VehicleBaker/
        // BirdBaker use, just as one shared clip instead of per-instance.
        //
        // Any waypoint under the path carrying a WaypointStop component
        // pauses the whole formation in place for that many seconds before
        // it continues — same mechanism/field VehicleBaker reads for
        // traffic, ported here (see EmitStopsBefore below) so a formation
        // can, e.g., crest a hill and hold at a waypoint near a gate instead
        // of marching straight through. A large stopDuration on the final
        // waypoint (bigger than a round realistically takes) effectively
        // means "arrive and stay put" — it technically still loops back to
        // spawn eventually, just not within any playable timeframe.
        // ────────────────────────────────────────────────────────────────

        private static AnimationClip CreateRelativeMovementClip(
            VehiclePath path, float speed, float fullDist, Vector3 originPos, Quaternion originRot,
            out float arrivalNormalizedTime)
        {
            speed = Mathf.Max(0.1f, speed);
            Quaternion invOriginRot = Quaternion.Inverse(originRot);
            float sampleStep = Mathf.Max(0.5f, fullDist / 200f);

            // WaypointDists is path-local (relative to the first real
            // waypoint, after the spawn lead-in) — offset by spawnLeadDistance
            // to match the "travel distance" space appendSample/fullDist use
            // (distance 0 = the spawn point itself), same conversion
            // VehicleBaker does for the identical reason.
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

            System.Action<float, float> appendSample = (dist, time) =>
            {
                Vector3 pos;
                Quaternion rot;
                path.Sample(dist, out pos, out rot);

                Vector3 relPos = invOriginRot * (pos - originPos);
                Quaternion relRot = invOriginRot * rot;

                // Component-wise quaternion curves need same-hemisphere keys to
                // avoid a 360° spin glitch (see VehicleBaker/BirdBaker AppendSample).
                if (qw.length > 0)
                {
                    float dot = qx[qx.length - 1].value * relRot.x
                              + qy[qy.length - 1].value * relRot.y
                              + qz[qz.length - 1].value * relRot.z
                              + qw[qw.length - 1].value * relRot.w;
                    if (dot < 0f)
                    {
                        relRot.x = -relRot.x;
                        relRot.y = -relRot.y;
                        relRot.z = -relRot.z;
                        relRot.w = -relRot.w;
                    }
                }

                px.AddKey(time, relPos.x);
                py.AddKey(time, relPos.y);
                pz.AddKey(time, relPos.z);
                qx.AddKey(time, relRot.x);
                qy.AddKey(time, relRot.y);
                qz.AddKey(time, relRot.z);
                qw.AddKey(time, relRot.w);
            };

            // Emits an arrive/depart keyframe pair for any stop waypoints
            // crossed before 'beforeDist', accumulating their durations into
            // the clip's time mapping so the group actually pauses in place
            // instead of just passing through. Mirrors VehicleBaker's
            // EmitStopsBefore (the single-segment case, since this clip has
            // no spawn/despawn split to worry about — it always starts at
            // distance 0). nextStop/stopAcc are captured and mutated by
            // reference, same as any local closure.
            int   nextStop = 0;
            float stopAcc  = 0f;
            System.Func<float, float> emitStopsBefore = (beforeDist) =>
            {
                while (nextStop < wpTravelDists.Length && wpTravelDists[nextStop] < beforeDist)
                {
                    float sd  = wpTravelDists[nextStop];
                    float dur = wpStopDurs[nextStop];
                    nextStop++;
                    if (dur <= 0f) continue;

                    float tArrive = sd / speed + stopAcc;
                    appendSample(sd, tArrive);
                    stopAcc += dur;
                    appendSample(sd, tArrive + dur);
                }
                return stopAcc;
            };

            appendSample(0f, 0f);
            float d = sampleStep;
            while (d < fullDist)
            {
                stopAcc = emitStopsBefore(d);
                appendSample(d, d / speed + stopAcc);
                d += sampleStep;
            }
            stopAcc = emitStopsBefore(fullDist);
            float tEnd = fullDist / speed + stopAcc;
            appendSample(fullDist, tEnd);

            // Teleport back to the start an instant later, then hold the same
            // value at clipDuration so the wrap is seamless.
            float tTeleport = tEnd + 0.016f;
            appendSample(0f, tTeleport);

            // Fraction of the clip's own full (teleport-inclusive) length at
            // which the group is arrived/held, i.e. an instant BEFORE the
            // teleport-back-to-spawn keyframe — the exact Exit Time a
            // playCount-gated transition needs so it captures the held
            // arrival pose, not the snapped-back spawn pose.
            arrivalNormalizedTime = tEnd / tTeleport;

            SetLinear(px); SetLinear(py); SetLinear(pz);
            SetLinear(qx); SetLinear(qy); SetLinear(qz); SetLinear(qw);

            AnimationClip clip = new AnimationClip();
            clip.legacy = false; // Mecanim/Animator clip — NOT the legacy Animation-component flag

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            clip.SetCurve("", typeof(Transform), "localPosition.x", px);
            clip.SetCurve("", typeof(Transform), "localPosition.y", py);
            clip.SetCurve("", typeof(Transform), "localPosition.z", pz);
            clip.SetCurve("", typeof(Transform), "localRotation.x", qx);
            clip.SetCurve("", typeof(Transform), "localRotation.y", qy);
            clip.SetCurve("", typeof(Transform), "localRotation.z", qz);
            clip.SetCurve("", typeof(Transform), "localRotation.w", qw);

            return clip;
        }

        private static void SetLinear(AnimationCurve curve)
        {
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
        }

        // A single-keyframe clip holding one fixed pose (the arrival point,
        // relative to root's own placement, same convention as
        // CreateRelativeMovementClip) — for the Done state, which must NOT
        // reference the looping travel clip (see the comment where this is
        // used for why that caused an infinite restart).
        private static AnimationClip CreateHeldPoseClip(
            VehiclePath path, float fullDist, Vector3 originPos, Quaternion originRot)
        {
            Vector3 pos;
            Quaternion rot;
            path.Sample(fullDist, out pos, out rot);

            Quaternion invOriginRot = Quaternion.Inverse(originRot);
            Vector3 relPos = invOriginRot * (pos - originPos);
            Quaternion relRot = invOriginRot * rot;

            AnimationClip clip = new AnimationClip();
            clip.legacy = false;

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            clip.SetCurve("", typeof(Transform), "localPosition.x", ConstantCurve(relPos.x));
            clip.SetCurve("", typeof(Transform), "localPosition.y", ConstantCurve(relPos.y));
            clip.SetCurve("", typeof(Transform), "localPosition.z", ConstantCurve(relPos.z));
            clip.SetCurve("", typeof(Transform), "localRotation.x", ConstantCurve(relRot.x));
            clip.SetCurve("", typeof(Transform), "localRotation.y", ConstantCurve(relRot.y));
            clip.SetCurve("", typeof(Transform), "localRotation.z", ConstantCurve(relRot.z));
            clip.SetCurve("", typeof(Transform), "localRotation.w", ConstantCurve(relRot.w));

            return clip;
        }

        private static AnimationCurve ConstantCurve(float value)
        {
            return new AnimationCurve(new Keyframe(0f, value));
        }

        // ────────────────────────────────────────────────────────────────
        // Tiered fidelity: bake the filler mass into static combined
        // mesh(es) instead of individually animated instances.
        // ────────────────────────────────────────────────────────────────

        private static void BuildFillerBatches(GameObject characterPrefab, List<Vector3> fillerOffsets, Transform parent)
        {
            // One temporary instance, posed at a handful of points across its
            // own run cycle, gives visual variety without per-instance Animators.
            GameObject tempInstance = (GameObject)Object.Instantiate(characterPrefab);
            tempInstance.transform.position = Vector3.zero;
            tempInstance.transform.rotation = Quaternion.identity;
            tempInstance.transform.localScale = Vector3.one;

            SkinnedMeshRenderer smr = tempInstance.GetComponentInChildren<SkinnedMeshRenderer>();
            Animator tempAnimator = tempInstance.GetComponentInChildren<Animator>();

            if (smr == null)
            {
                Debug.LogWarning("[TriggerFormations] Character prefab has no SkinnedMeshRenderer — " +
                                  "can't build a static filler mesh. Filler slots will be left empty.");
                Object.DestroyImmediate(tempInstance);
                return;
            }

            AnimationClip poseClip = null;
            if (tempAnimator != null && tempAnimator.runtimeAnimatorController != null)
            {
                AnimationClip[] clips = tempAnimator.runtimeAnimatorController.animationClips;
                if (clips != null && clips.Length > 0) poseClip = clips[0];
            }

            if (smr.sharedMaterials != null && smr.sharedMaterials.Length > 1)
            {
                Debug.LogWarning("[TriggerFormations] Character prefab uses multiple materials — " +
                                  "the static filler mass only reproduces the first material " +
                                  "(sub-meshes beyond the first are dropped).");
            }
            Material fillerMaterial = (smr.sharedMaterials != null && smr.sharedMaterials.Length > 0)
                ? smr.sharedMaterials[0] : smr.sharedMaterial;

            // Relative transform of the SkinnedMeshRenderer within the prefab —
            // valid because tempInstance sits at world-origin/identity above.
            Matrix4x4 smrLocalMatrix = smr.transform.localToWorldMatrix;

            // Bake a handful of distinct poses across the run cycle.
            List<Mesh> poseMeshes = new List<Mesh>();
            bool startedAnimationMode = false;
            if (poseClip != null)
            {
                AnimationMode.StartAnimationMode();
                startedAnimationMode = true;
            }

            for (int p = 0; p < PosesPerFillerBatch; p++)
            {
                Mesh baked = new Mesh();
                if (poseClip != null)
                {
                    float time = (p / (float)PosesPerFillerBatch) * poseClip.length;
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(tempInstance, poseClip, time);
                    AnimationMode.EndSampling();
                }
                smr.BakeMesh(baked);
                poseMeshes.Add(baked);
            }

            if (startedAnimationMode)
                AnimationMode.StopAnimationMode();

            // Combine filler slots into batches, capped in size to keep any
            // single combined mesh's vertex count reasonable.
            int batchIndex = 0;
            for (int start = 0; start < fillerOffsets.Count; start += FillerBatchSize)
            {
                int batchCount = Mathf.Min(FillerBatchSize, fillerOffsets.Count - start);
                CombineInstance[] combine = new CombineInstance[batchCount];

                for (int i = 0; i < batchCount; i++)
                {
                    Vector3 offset = fillerOffsets[start + i];
                    Matrix4x4 slotMatrix = Matrix4x4.TRS(offset, Quaternion.identity, Vector3.one);

                    combine[i] = new CombineInstance
                    {
                        mesh = poseMeshes[(start + i) % poseMeshes.Count],
                        transform = slotMatrix * smrLocalMatrix
                    };
                }

                Mesh combinedMesh = new Mesh();
                combinedMesh.indexFormat = IndexFormat.UInt32;
                combinedMesh.CombineMeshes(combine, true);

                string meshName = "FillerBatch_" + batchIndex;
                string meshPath = AssetDatabase.GenerateUniqueAssetPath(MeshFolder + "/" + meshName + ".asset");
                AssetDatabase.CreateAsset(combinedMesh, meshPath);

                GameObject batchGo = new GameObject(meshName);
                batchGo.transform.SetParent(parent, false);
                batchGo.AddComponent<MeshFilter>().sharedMesh = combinedMesh;
                MeshRenderer renderer = batchGo.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = fillerMaterial;

                batchIndex++;
            }

            Object.DestroyImmediate(tempInstance);
        }

        // ────────────────────────────────────────────────────────────────
        // Audio — one shared AudioSource for the whole formation, on 'mover'
        // (same object the Animator lives on), not per-soldier. Mirrors
        // VehicleBaker.BakeAudio's spatial/rolloff settings exactly, except
        // Loop is exposed as a spawner checkbox instead of hardcoded true —
        // this bakes with Play On Awake, so (like vehicle engine sound) it is
        // NOT gated to the Idle/Move trigger state; see the spawner field's
        // own tooltip.
        // ────────────────────────────────────────────────────────────────
        private static void BakeAudio(GameObject mover, TriggerFormationSpawner spawner)
        {
            if (spawner.audioClip == null) return;

            AudioSource src = mover.GetComponent<AudioSource>();
            if (src == null) src = mover.AddComponent<AudioSource>();

            src.clip         = spawner.audioClip;
            src.loop         = spawner.loopAudio;
            src.playOnAwake  = true;
            src.volume       = spawner.audioVolume;
            src.spatialBlend = 1.0f;
            src.minDistance  = 2.0f;
            src.maxDistance  = spawner.audioMaxDistance;
            src.rolloffMode  = AudioRolloffMode.Linear;
            src.dopplerLevel = 0.0f;
        }

        // ────────────────────────────────────────────────────────────────
        // Trigger wiring — same building block/field names TriggerEffectBaker
        // uses, so a formation drops straight into GSPro's ball-collision
        // trigger the same way an explosion does. Ported here rather than
        // shared between the two packages so each mm_tool stays self-
        // contained/independently packageable.
        // ────────────────────────────────────────────────────────────────
        private static void AddAndWireTrigger(Transform parent, Animator animator, string boolParameterName)
        {
            const string triggerPrefabPath = "Assets/WanderAIv2andTrigger/Trigger.prefab";
            GameObject triggerPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(triggerPrefabPath);
            if (triggerPrefabAsset == null)
            {
                Debug.LogWarning("[TriggerFormations] Couldn't find " + triggerPrefabPath + " in this project — " +
                                  "drag a Trigger prefab in yourself as a child of '" + parent.name +
                                  "', set 'My Object Controller' to its Animator, 'Bool Condition Name' to '" +
                                  boolParameterName + "', and check 'Game Ball Collision'.");
                return;
            }

            GameObject triggerInstance = (GameObject)PrefabUtility.InstantiatePrefab(triggerPrefabAsset);
            triggerInstance.name = "Trigger";
            triggerInstance.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(triggerInstance, "Bake Trigger Formation");

            Component triggerScript = FindComponentByTypeName(triggerInstance, "TriggerScript");
            if (triggerScript == null)
            {
                Debug.LogWarning("[TriggerFormations] Trigger child has no TriggerScript component — wire " +
                                  "'My Object Controller' / 'Bool Condition Name' / 'Game Ball Collision' by hand.");
                return;
            }

            SerializedObject so = new SerializedObject(triggerScript);
            bool wiredController = TrySetObjectProperty(so, "MyObjectController", animator);
            bool wiredBoolName   = TrySetStringProperty(so, "BoolConditionName", boolParameterName);
            bool wiredGameBall   = TrySetBoolProperty(so, "GameBallCollision", true);
            so.ApplyModifiedProperties();

            if (!wiredController || !wiredBoolName || !wiredGameBall)
                Debug.LogWarning("[TriggerFormations] TriggerScript's field names didn't fully match what " +
                                  "this tool expects (controller: " + wiredController + ", bool name: " +
                                  wiredBoolName + ", game ball collision: " + wiredGameBall +
                                  ") — check those fields on '" + parent.name + "/Trigger' by hand.");
        }

        // Finds a component by type name string rather than a generic type
        // parameter — this package has no compile-time reference to
        // WanderAI.dll, so TriggerScript's actual .NET type isn't available
        // to code against directly.
        private static Component FindComponentByTypeName(GameObject go, string typeName)
        {
            foreach (Component comp in go.GetComponents<Component>())
                if (comp != null && comp.GetType().Name == typeName)
                    return comp;
            return null;
        }

        // Sets a serialized field by name, generically — works regardless of
        // the component's compiled type, same reasoning as FindComponentByTypeName.
        // Returns false (rather than throwing) if the field name doesn't exist,
        // so a mismatch degrades to a warning instead of a hard failure.
        private static bool TrySetObjectProperty(SerializedObject so, string propertyName, Object value)
        {
            SerializedProperty prop = so.FindProperty(propertyName);
            if (prop == null) return false;
            prop.objectReferenceValue = value;
            return true;
        }

        private static bool TrySetStringProperty(SerializedObject so, string propertyName, string value)
        {
            SerializedProperty prop = so.FindProperty(propertyName);
            if (prop == null) return false;
            prop.stringValue = value;
            return true;
        }

        private static bool TrySetBoolProperty(SerializedObject so, string propertyName, bool value)
        {
            SerializedProperty prop = so.FindProperty(propertyName);
            if (prop == null) return false;
            prop.boolValue = value;
            return true;
        }

        // ────────────────────────────────────────────────────────────────
        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(RootFolder))
                AssetDatabase.CreateFolder("Assets/TriggerFormations", "Baked");
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
                AssetDatabase.CreateFolder(RootFolder, "Prefabs");
            if (!AssetDatabase.IsValidFolder(AnimFolder))
                AssetDatabase.CreateFolder(RootFolder, "Animations");
            if (!AssetDatabase.IsValidFolder(ControllerFolder))
                AssetDatabase.CreateFolder(RootFolder, "Controllers");
            if (!AssetDatabase.IsValidFolder(MeshFolder))
                AssetDatabase.CreateFolder(RootFolder, "Meshes");
        }
    }
}
