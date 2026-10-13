// TriggerEffectBaker.cs  –  Editor only
// Tools > chickenandribs > Trigger Effects > Create Trigger Effect Spawner
// Tools > chickenandribs > Trigger Effects > Bake Selected Trigger Effect Spawner
//
// Wraps a TriggerEffectSpawner's assigned prefab so GSPro's TriggerScript
// can turn it on — no custom runtime scripts (GSPro doesn't execute those
// unless they're already part of GSPro's own compiled set: confirmed this
// session for both WanderAI.dll's TriggerScript and Unity Standard Assets'
// ParticleSystems scripts).
//
// HOW IT STAYS SCRIPT-FREE
// Whatever the wrapped prefab's own Start()/Awake() does fires the instant
// its GameObject first activates. A GameObject that starts inactive defers
// that until first activation, so a baked Animator curve toggling active
// state (no script from us) is what starts it — same mechanism as every
// other baker this session. The Play clip's length is deliberately non-zero
// (EffectDuration below) — a single-keyframe, zero-length clip loses a race
// against TriggerScript's own "hold the bool true for one clip-length" logic
// and any wrapped prefab whose own Start() does delayed (coroutine) work.
//
// WHY A SHOCKWAVE INSTEAD OF THE WRAPPED PREFAB'S OWN PHYSICS
// Standard Assets' ExplosionPhysicsForce.cs calls Rigidbody.AddExplosionForce
// — confirmed via live GSPro testing this session to produce no visible push
// there, even though the same object's particle effects (a different
// component) fire correctly. But driving a Rigidbody+Collider vehicle
// *through* a group of Rigidbody soldiers was separately confirmed to push
// them via ordinary collision. So instead of depending on the force API that
// doesn't work, this bakes a ring of small kinematic-rigidbody "pushers"
// that physically move outward and shove into whatever they touch — the
// same collision mechanism already proven working, not AddExplosionForce.
//
// SPAWNER + BAKE, NOT A ONE-SHOT COMMAND
// A TriggerEffectSpawner (plain data component, see that file) sits at the
// blast location with Inspector-editable fields for the shockwave's
// force/size. Baking rebuilds the invisible Effect/Shockwave children in
// place under the spawner every time it's run, but only ever creates the
// Trigger child once — so re-tuning a field and re-baking doesn't disturb
// the Trigger's Box Collider once you've sized/positioned it.

using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TriggerEffects
{
    public static class TriggerEffectBaker
    {
        private const string ControllerFolder = "Assets/Trigger Effects/Controllers";
        private const string AnimFolder       = "Assets/Trigger Effects/Animations";
        private const float  EffectDuration   = 6f; // how long the wrapped prefab stays active

        [MenuItem("Tools/chickenandribs/Trigger Effects/Create Trigger Effect Spawner", false, 100)]
        private static void CreateSpawner()
        {
            GameObject go = new GameObject("Trigger Effect Spawner");
            go.AddComponent<TriggerEffectSpawner>();
            Undo.RegisterCreatedObjectUndo(go, "Create Trigger Effect Spawner");
            Selection.activeObject = go;
            EditorGUIUtility.PingObject(go);
        }

        [MenuItem("Tools/chickenandribs/Trigger Effects/Bake Selected Trigger Effect Spawner", false, 101)]
        private static void Bake()
        {
            GameObject selected = Selection.activeGameObject;
            TriggerEffectSpawner spawner = selected.GetComponent<TriggerEffectSpawner>();

            if (spawner.effectPrefab == null)
            {
                EditorUtility.DisplayDialog("Bake Trigger Effect",
                    "Assign an Effect Prefab on the spawner first.", "OK");
                return;
            }

            EnsureFolders();

            Transform spawnerT = spawner.transform;

            // ── Re-bake safe: drop any previous bake's Effect/Shockwave, leave Trigger alone ──
            RemoveChildIfExists(spawnerT, "Effect");
            RemoveChildIfExists(spawnerT, "Shockwave");

            // ── Effect child ────────────────────────────────────────────────
            GameObject effect = (GameObject)PrefabUtility.InstantiatePrefab(spawner.effectPrefab);
            effect.name = "Effect";
            effect.transform.SetParent(spawnerT, false);
            effect.SetActive(false); // starts hidden — see header note on why this matters

            // ── Bake the activation (+ optional shockwave) clip ────────────
            AnimationClip playClip = new AnimationClip();
            playClip.legacy = false; // Mecanim/Animator clip, not the legacy Animation-component flag

            AnimationCurve effectActiveCurve = new AnimationCurve();
            effectActiveCurve.AddKey(new Keyframe(0f, 1f));
            effectActiveCurve.AddKey(new Keyframe(EffectDuration, 0f));
            SetConstant(effectActiveCurve);
            AnimationUtility.SetEditorCurve(
                playClip,
                EditorCurveBinding.FloatCurve("Effect", typeof(GameObject), "m_IsActive"),
                effectActiveCurve);

            if (spawner.addShockwave)
                BuildShockwave(spawnerT, spawner, playClip);

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(playClip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(playClip, settings);

            // Keyed by the spawner's OWN name, not just the wrapped prefab's —
            // otherwise every spawner wrapping the same prefab (e.g. several
            // Explosion.prefab copies along a fairway) would share one baked
            // asset, and re-baking one after individually re-tuning it would
            // silently overwrite every other spawner's controller/clip too.
            // Unity keeps duplicate sibling names unique (appending " (1)",
            // " (2)", ...), so the spawner's name is enough on its own here.
            string safeName = SanitizeForPath(spawner.name) + "_" + SanitizeForPath(spawner.effectPrefab.name);
            string clipPath = AnimFolder + "/" + safeName + "_Play.anim";
            CreateAssetReplacing(playClip, clipPath);

            // ── Trigger-gated AnimatorController ────────────────────────────
            string controllerPath = ControllerFolder + "/" + safeName + "Controller.controller";
            DeleteAssetIfExists(controllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Go", AnimatorControllerParameterType.Bool);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            AnimatorState idleState = stateMachine.AddState("Idle");
            AnimatorState playState = stateMachine.AddState("Play");
            playState.motion = playClip;
            stateMachine.defaultState = idleState;

            AnimatorStateTransition toPlay = idleState.AddTransition(playState);
            toPlay.hasExitTime = false;
            toPlay.duration = 0f;
            toPlay.AddCondition(AnimatorConditionMode.If, 0, "Go");

            AnimatorStateTransition toIdle = playState.AddTransition(idleState);
            toIdle.hasExitTime = false;
            toIdle.duration = 0f;
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "Go");

            EditorUtility.SetDirty(controller);

            Animator animator = selected.GetComponent<Animator>();
            if (animator == null) animator = selected.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ── Trigger child: created once, re-wired every bake ────────────
            Transform triggerT = spawnerT.Find("Trigger");
            bool triggerAlreadyExisted = triggerT != null;
            GameObject triggerInstance = triggerAlreadyExisted ? triggerT.gameObject : null;

            if (!triggerAlreadyExisted)
            {
                const string triggerPrefabPath = "Assets/WanderAIv2andTrigger/Trigger.prefab";
                GameObject triggerPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(triggerPrefabPath);
                if (triggerPrefabAsset == null)
                {
                    Debug.LogWarning("[TriggerEffects] Couldn't find " + triggerPrefabPath + " in this project — " +
                                      "drag a Trigger prefab in yourself as a child of '" + spawner.name +
                                      "', set 'My Object Controller' to its Animator, 'Bool Condition Name' to " +
                                      "'Go', and check 'Game Ball Collision'.");
                }
                else
                {
                    triggerInstance = (GameObject)PrefabUtility.InstantiatePrefab(triggerPrefabAsset);
                    triggerInstance.name = "Trigger";
                    triggerInstance.transform.SetParent(spawnerT, false);
                    Undo.RegisterCreatedObjectUndo(triggerInstance, "Bake Trigger Effect Spawner");
                }
            }

            if (triggerInstance != null)
            {
                Component triggerScript = FindComponentByTypeName(triggerInstance, "TriggerScript");
                if (triggerScript == null)
                {
                    Debug.LogWarning("[TriggerEffects] Trigger child has no TriggerScript component — wire " +
                                      "'My Object Controller' / 'Bool Condition Name' / 'Game Ball Collision' by hand.");
                }
                else
                {
                    SerializedObject so = new SerializedObject(triggerScript);
                    bool wiredController = TrySetObjectProperty(so, "MyObjectController", animator);
                    bool wiredBoolName   = TrySetStringProperty(so, "BoolConditionName", "Go");
                    bool wiredGameBall   = TrySetBoolProperty(so, "GameBallCollision", true);
                    so.ApplyModifiedProperties();

                    if (!wiredController || !wiredBoolName || !wiredGameBall)
                        Debug.LogWarning("[TriggerEffects] TriggerScript's field names didn't fully match what " +
                                          "this tool expects (controller: " + wiredController + ", bool name: " +
                                          wiredBoolName + ", game ball collision: " + wiredGameBall +
                                          ") — check those fields on '" + spawner.name + "/Trigger' by hand.");
                }
            }

            Selection.activeObject = selected;
            EditorGUIUtility.PingObject(selected);

            Debug.Log("[TriggerEffects] Baked '" + spawner.name + "' (" + safeName +
                      (spawner.addShockwave ? " + shockwave" : "") + "). " +
                      (triggerAlreadyExisted
                          ? "Existing Trigger left untouched — re-tune fields and Bake again anytime."
                          : "Size/position the new Trigger's Box Collider over the blast spot."));
        }

        [MenuItem("Tools/chickenandribs/Trigger Effects/Bake Selected Trigger Effect Spawner", true)]
        private static bool BakeValidate()
        {
            return Selection.activeGameObject != null &&
                   Selection.activeGameObject.GetComponent<TriggerEffectSpawner>() != null;
        }

        // ────────────────────────────────────────────────────────────────
        // Shockwave: a horizontal ring of small kinematic-rigidbody pushers
        // that physically move outward from center, shoving into dynamic
        // Rigidbodies via ordinary collision — the same mechanism a moving
        // vehicle was confirmed (in GSPro) to push soldiers with. A kinematic
        // body's Transform-driven movement (however it's driven — script,
        // Animator, doesn't matter) is exactly what Unity's physics engine
        // uses to compute collision push-out against dynamic bodies.
        // Kinematic-vs-static collisions (e.g. clipping through non-Rigidbody
        // terrain) generate events but never obstruct a kinematic body's
        // motion, so there's no risk of a pusher getting stuck on the ground.
        // ────────────────────────────────────────────────────────────────
        private static void BuildShockwave(Transform spawnerT, TriggerEffectSpawner spawner, AnimationClip playClip)
        {
            GameObject shockwave = new GameObject("Shockwave");
            shockwave.transform.SetParent(spawnerT, false);
            shockwave.SetActive(false);

            float pitchRad        = spawner.pusherPitchDegrees * Mathf.Deg2Rad;
            float horizontalScale = Mathf.Cos(pitchRad);
            float verticalScale   = Mathf.Sin(pitchRad);

            for (int i = 0; i < spawner.pusherCount; i++)
            {
                float angle = i * (360f / spawner.pusherCount) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(
                    Mathf.Cos(angle) * horizontalScale,
                    verticalScale,
                    Mathf.Sin(angle) * horizontalScale).normalized;

                GameObject pusher = new GameObject("Pusher_" + i);
                pusher.transform.SetParent(shockwave.transform, false);
                pusher.transform.localPosition = dir * spawner.pusherStartRadius;

                SphereCollider col = pusher.AddComponent<SphereCollider>();
                col.radius = spawner.pusherRadius;

                Rigidbody rb = pusher.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity  = false;

                string path = "Shockwave/Pusher_" + i;
                Vector3 startPos = dir * spawner.pusherStartRadius;
                Vector3 endPos   = dir * spawner.shockwaveRadius;

                AnimationUtility.SetEditorCurve(playClip,
                    EditorCurveBinding.FloatCurve(path, typeof(Transform), "localPosition.x"),
                    LinearCurve(0f, startPos.x, spawner.shockwaveDuration, endPos.x));
                AnimationUtility.SetEditorCurve(playClip,
                    EditorCurveBinding.FloatCurve(path, typeof(Transform), "localPosition.y"),
                    LinearCurve(0f, startPos.y, spawner.shockwaveDuration, endPos.y));
                AnimationUtility.SetEditorCurve(playClip,
                    EditorCurveBinding.FloatCurve(path, typeof(Transform), "localPosition.z"),
                    LinearCurve(0f, startPos.z, spawner.shockwaveDuration, endPos.z));
            }

            // One shared active-toggle on the parent — deactivating it takes every
            // pusher with it, rather than needing one curve per pusher.
            AnimationCurve shockwaveActiveCurve = new AnimationCurve();
            shockwaveActiveCurve.AddKey(new Keyframe(0f, 1f));
            shockwaveActiveCurve.AddKey(new Keyframe(spawner.shockwaveDuration + 0.2f, 0f));
            SetConstant(shockwaveActiveCurve);
            AnimationUtility.SetEditorCurve(
                playClip,
                EditorCurveBinding.FloatCurve("Shockwave", typeof(GameObject), "m_IsActive"),
                shockwaveActiveCurve);
        }

        private static AnimationCurve LinearCurve(float t0, float v0, float t1, float v1)
        {
            AnimationCurve curve = new AnimationCurve();
            curve.AddKey(new Keyframe(t0, v0));
            curve.AddKey(new Keyframe(t1, v1));
            SetLinear(curve);
            return curve;
        }

        // ────────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────────

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Trigger Effects"))
                AssetDatabase.CreateFolder("Assets", "Trigger Effects");
            if (!AssetDatabase.IsValidFolder(ControllerFolder))
                AssetDatabase.CreateFolder("Assets/Trigger Effects", "Controllers");
            if (!AssetDatabase.IsValidFolder(AnimFolder))
                AssetDatabase.CreateFolder("Assets/Trigger Effects", "Animations");
        }

        private static readonly char[] InvalidPathChars = System.IO.Path.GetInvalidFileNameChars();

        private static string SanitizeForPath(string name)
        {
            string result = name;
            foreach (char c in InvalidPathChars)
                result = result.Replace(c, '_');
            return result;
        }

        private static void RemoveChildIfExists(Transform parent, string childName)
        {
            Transform existing = parent.Find(childName);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);
        }

        // Every asset this tool creates lives at a deterministic, prefab-name-based
        // path, so re-baking the same spawner always targets the exact same paths.
        // AssetDatabase.CreateAsset does not cleanly overwrite an existing asset at
        // its target path — deleting first makes every bake a clean, full replace.
        private static void DeleteAssetIfExists(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                AssetDatabase.DeleteAsset(path);
        }

        private static void CreateAssetReplacing(Object asset, string path)
        {
            DeleteAssetIfExists(path);
            AssetDatabase.CreateAsset(asset, path);
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

        private static void SetConstant(AnimationCurve curve)
        {
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
            }
        }

        private static void SetLinear(AnimationCurve curve)
        {
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
        }
    }
}
