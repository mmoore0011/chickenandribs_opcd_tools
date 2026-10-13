// MakeIntroVideoOverlay.cs  –  Editor only
// Select a VideoClip in the Project window, then:
//   Tools > chickenandribs > Video Animations > Create Intro Video Overlay from Video
//
// Full-screen video overlay (letterboxed with black bars), triggered by the
// tee, that fades to transparent 5 seconds after the video ends. No 3D
// placement of anything around the golfer at all — this is a screen-space UI
// overlay, not world geometry.
//
// WHY A UI CANVAS, NOT VideoPlayer's CameraNearPlane MODE
// An earlier version used VideoPlayer.renderMode = CameraNearPlane (Unity's
// built-in full-screen video overlay). It never worked: audio confirmed the
// video was genuinely decoding, but nothing ever appeared on screen, in a
// scene with Deferred rendering + Post-Processing Stack v2 + a custom camera
// script — and disabling post-processing didn't fix it either. A web search
// turned up documented, unresolved community reports of VideoPlayer's Camera
// Near/Far Plane mode being unreliable prior to Unity 2019.1 ("mysteriously
// not working" despite correct configuration) — this project is 2018.2,
// squarely in the affected range. Rather than keep fighting a
// documented-unreliable feature, this renders the video into a RenderTexture
// (the well-established, mature VideoPlayer output mode) and displays that
// via a Screen Space - Overlay Canvas + RawImage — one of the most
// battle-tested UI patterns in Unity, and one that renders after everything
// else in the frame, independent of the camera/post-processing pipeline
// entirely.
//
// HOW IT STAYS SCRIPT-FREE
// Same mechanism as the rest of this session's work (GSPro doesn't execute
// custom MonoBehaviour scripts — see MovingVehicles/VehicleBaker.cs,
// BirdFlock/BirdBaker.cs, TriggerFormations/TriggerFormationBaker.cs):
// VideoPlayer.playOnAwake fires at Awake(), and a GameObject that starts
// inactive defers Awake() until first activated, so a baked Animator curve
// toggling that GameObject's active state (no script) is what starts
// playback. TriggerScript's own timing (confirmed via its sibling
// ColliderHitScript.cs, since TriggerScript itself ships compiled-only)
// holds its bool true for exactly one clip-length after a hit.
//
// LETTERBOXING AND FADE
// AspectRatioFitter (Unity's built-in UI component for exactly this) keeps
// the video's real aspect ratio within the full-screen black Background
// image, at any screen resolution, no custom math needed.
//
// The fade only ever animates ONE layer (Background's Color.a) — VideoImage
// switches off outright (m_IsActive, instant) the moment the video's content
// ends, rather than fading. Two earlier versions tried fading Background and
// VideoImage together (via a shared CanvasGroup.alpha, then via both bound
// directly to the literal same AnimationCurve object) and in both cases the
// video area visibly outlasted the plain black bars around it despite
// mathematically identical alpha values. The reason: Background is
// full-screen, sitting behind VideoImage too, not just behind the letterbox
// bars — with both layers semi-transparent at once, each dims what the
// other has already dimmed (50% of 50% is 25%, not 50%), so the overlapping
// region reveals the scene behind it more slowly than the single-layer
// bars do. Cutting VideoImage out cleanly instead of fading it removes the
// overlap entirely — by the time the fade starts, the video's actual
// content is already over anyway, so nothing is lost.
//
// No target-camera assignment needed anywhere — Screen Space Overlay
// canvases aren't tied to any particular Camera, which also sidesteps that
// whole category of "which camera, is it the right one" uncertainty.
//
// Also drops a ready-to-use "Intro video package N" group into the active
// scene (N auto-increments, so reusing this per tee box doesn't collide):
// the overlay plus an instance of the project's own Trigger.prefab
// (WanderAIv2andTrigger), already wired ("My Object Controller" -> the
// overlay's Animator, "Bool Condition Name" -> "Go", "Game Ball Collision"
// -> on). Only the Trigger's Box Collider needs positioning/sizing to the tee.

using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace VideoAnimations
{
    public static class MakeIntroVideoOverlay
    {
        private const float FadeDuration = 5f;
        private const int CanvasSortOrder = 1000; // stays above any other UI in the scene

        [MenuItem("Tools/chickenandribs/Video Animations/Create Intro Video Overlay from Video", false, 101)]
        private static void Create()
        {
            VideoClip clip = Selection.activeObject as VideoClip;

            string videoName    = clip.name;
            string prefabFolder = "Assets/Video Animations/Prefabs/" + videoName + "_IntroOverlay";

            if (!AssetDatabase.IsValidFolder("Assets/Video Animations"))
                AssetDatabase.CreateFolder("Assets", "Video Animations");
            if (!AssetDatabase.IsValidFolder("Assets/Video Animations/Prefabs"))
                AssetDatabase.CreateFolder("Assets/Video Animations", "Prefabs");
            if (!AssetDatabase.IsValidFolder(prefabFolder))
                AssetDatabase.CreateFolder("Assets/Video Animations/Prefabs", videoName + "_IntroOverlay");

            // ── RenderTexture sized to match video ────────────────────────────
            int w = (int)clip.width;
            int h = (int)clip.height;
            RenderTexture rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            rt.name = videoName + "_IntroRenderTex";
            CreateAssetReplacing(rt, prefabFolder + "/" + videoName + "_IntroRenderTex.renderTexture");

            // ── Hierarchy ───────────────────────────────────────────────────
            // Root stays always-active and owns the Animator: if the Animator
            // lived on VideoOverlay itself, deactivating it would disable its
            // own Animator along with it, leaving nothing able to reactivate it.
            GameObject root = new GameObject(videoName + "_IntroOverlay");

            // VideoOverlay holds the Canvas and the VideoPlayer together —
            // safe to combine here (unlike the Animator/Animation conflict
            // from other bakers in this session) since neither is the thing
            // being deactivated for placement purposes; nothing here is ever
            // touched except by the real trigger.
            GameObject videoOverlay = new GameObject("VideoOverlay");
            videoOverlay.transform.SetParent(root.transform, false);

            Canvas canvas = videoOverlay.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortOrder;
            videoOverlay.AddComponent<CanvasScaler>();

            // Background: solid black, fills the whole canvas — this is what
            // shows as the letterbox/pillarbox bars around the video.
            GameObject background = new GameObject("Background");
            background.transform.SetParent(videoOverlay.transform, false);
            RectTransform bgRect = background.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = background.AddComponent<Image>();
            bgImage.color = Color.black;

            // Video image: fills the canvas, AspectRatioFitter keeps the
            // video's real aspect ratio within those bounds at any screen size.
            GameObject videoImageGo = new GameObject("VideoImage");
            videoImageGo.transform.SetParent(videoOverlay.transform, false);
            RectTransform videoRect = videoImageGo.AddComponent<RectTransform>();
            videoRect.anchorMin = Vector2.zero;
            videoRect.anchorMax = Vector2.one;
            videoRect.offsetMin = Vector2.zero;
            videoRect.offsetMax = Vector2.zero;
            RawImage videoImage = videoImageGo.AddComponent<RawImage>();
            videoImage.texture = rt;
            AspectRatioFitter fitter = videoImageGo.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = h > 0 ? (float)w / h : 16f / 9f;

            VideoPlayer vp        = videoOverlay.AddComponent<VideoPlayer>();
            vp.clip               = clip;
            vp.renderMode         = VideoRenderMode.RenderTexture;
            vp.targetTexture      = rt;
            vp.isLooping          = false;   // one-shot intro, not an ambient loop
            vp.playOnAwake        = true;    // fires the instant this object activates
            vp.waitForFirstFrame  = true;
            vp.audioOutputMode    = VideoAudioOutputMode.Direct; // play the video's own audio

            // Starts hidden. The prefab's default active state IS this value —
            // it's what makes the "Awake deferred until first activation" trick work.
            videoOverlay.SetActive(false);

            // ── Bake the reveal/hide/fade clip ────────────────────────────────
            float videoLength = (float)clip.length;
            float totalLength = videoLength + FadeDuration;

            AnimationCurve activeCurve = new AnimationCurve();
            activeCurve.AddKey(new Keyframe(0f, 1f));
            activeCurve.AddKey(new Keyframe(totalLength, 0f));
            SetConstant(activeCurve);

            AnimationClip playClip = new AnimationClip();
            playClip.legacy = false; // Mecanim/Animator clip, not the legacy Animation-component flag
            AnimationUtility.SetEditorCurve(
                playClip,
                EditorCurveBinding.FloatCurve("VideoOverlay", typeof(GameObject), "m_IsActive"),
                activeCurve);

            // VideoImage switches off the instant the video content ends —
            // not faded. Two overlapping semi-transparent layers (Background
            // sits full-screen, behind VideoImage too, not just behind the
            // letterbox bars) fading at the same time compound: each dims
            // what the other has already dimmed, so 50%+50% lets through
            // 25%, not 50% — which is exactly why an earlier version, fading
            // both together (even with mathematically identical curves),
            // visibly had the video area outlast the plain black bars around
            // it. By the time the fade starts the video's actual content is
            // already over, so there's nothing lost by cutting VideoImage
            // out cleanly rather than fading it.
            AnimationCurve videoImageActiveCurve = new AnimationCurve();
            videoImageActiveCurve.AddKey(new Keyframe(0f, 1f));
            videoImageActiveCurve.AddKey(new Keyframe(videoLength, 0f));
            SetConstant(videoImageActiveCurve);
            AnimationUtility.SetEditorCurve(
                playClip,
                EditorCurveBinding.FloatCurve("VideoOverlay/VideoImage", typeof(GameObject), "m_IsActive"),
                videoImageActiveCurve);

            // Fade: only Background remains after VideoImage switches off, so
            // this is now a single, uncompounded layer fading from full
            // opacity to 0 over the last FadeDuration seconds.
            AnimationCurve alphaCurve = new AnimationCurve();
            alphaCurve.AddKey(new Keyframe(0f, 1f));
            alphaCurve.AddKey(new Keyframe(videoLength, 1f));
            alphaCurve.AddKey(new Keyframe(totalLength, 0f));
            SetLinear(alphaCurve);
            AnimationUtility.SetEditorCurve(
                playClip,
                EditorCurveBinding.FloatCurve("VideoOverlay/Background", typeof(Image), "m_Color.a"),
                alphaCurve);

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(playClip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(playClip, settings);

            string clipPath = prefabFolder + "/" + videoName + "_IntroPlay.anim";
            CreateAssetReplacing(playClip, clipPath);

            // ── Trigger-gated AnimatorController ──────────────────────────────
            string controllerPath = prefabFolder + "/" + videoName + "_IntroController.controller";
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

            Animator animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            // ── Prefab ────────────────────────────────────────────────────────
            string prefabPath = prefabFolder + "/" + videoName + "_IntroOverlay.prefab";
            DeleteAssetIfExists(prefabPath);
#pragma warning disable 618
            GameObject prefab = PrefabUtility.CreatePrefab(prefabPath, root);
#pragma warning restore 618

            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ── Scene placement: overlay + a wired trigger, one root ──────────
            // Reuses the project's own Trigger.prefab (WanderAIv2andTrigger)
            // rather than adding a fresh TriggerScript component — this
            // package has no compile-time reference to WanderAI.dll (and never
            // saw its source, only its serialized field names), so wiring is
            // done generically via SerializedObject by field name.
            GameObject packageRoot = new GameObject(GetNextPackageName());
            Undo.RegisterCreatedObjectUndo(packageRoot, "Create Intro Video Package");

            GameObject overlayInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            overlayInstance.transform.SetParent(packageRoot.transform, false);
            Animator overlayAnimator = overlayInstance.GetComponent<Animator>();

            const string triggerPrefabPath = "Assets/WanderAIv2andTrigger/Trigger.prefab";
            GameObject triggerPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(triggerPrefabPath);

            if (triggerPrefabAsset == null)
            {
                Debug.LogWarning("[VideoAnimations] Couldn't find " + triggerPrefabPath + " in this project — " +
                                  "created '" + packageRoot.name + "' with just the video overlay. Drag a Trigger " +
                                  "prefab in yourself, set 'My Object Controller' to " + overlayInstance.name +
                                  "'s Animator, 'Bool Condition Name' to 'Go', and check 'Game Ball Collision'.");
            }
            else
            {
                GameObject triggerInstance = (GameObject)PrefabUtility.InstantiatePrefab(triggerPrefabAsset);
                triggerInstance.name = "Trigger";
                triggerInstance.transform.SetParent(packageRoot.transform, false);
                Undo.RegisterCreatedObjectUndo(triggerInstance, "Create Intro Video Package");

                Component triggerScript = FindComponentByTypeName(triggerInstance, "TriggerScript");
                if (triggerScript == null)
                {
                    Debug.LogWarning("[VideoAnimations] " + triggerPrefabPath + " has no TriggerScript component — " +
                                      "wire 'My Object Controller' / 'Bool Condition Name' / 'Game Ball Collision' " +
                                      "by hand on '" + packageRoot.name + "/Trigger'.");
                }
                else
                {
                    SerializedObject so = new SerializedObject(triggerScript);
                    bool wiredController = TrySetObjectProperty(so, "MyObjectController", overlayAnimator);
                    bool wiredBoolName   = TrySetStringProperty(so, "BoolConditionName", "Go");
                    bool wiredGameBall   = TrySetBoolProperty(so, "GameBallCollision", true);
                    so.ApplyModifiedProperties();

                    if (!wiredController || !wiredBoolName || !wiredGameBall)
                        Debug.LogWarning("[VideoAnimations] TriggerScript's field names didn't fully match what " +
                                          "this tool expects (controller: " + wiredController + ", bool name: " +
                                          wiredBoolName + ", game ball collision: " + wiredGameBall +
                                          ") — check those fields on '" + packageRoot.name + "/Trigger' by hand.");
                }
            }

            Selection.activeObject = packageRoot;
            EditorGUIUtility.PingObject(packageRoot);

            Debug.Log("[VideoAnimations] Created '" + packageRoot.name + "' (video overlay + wired trigger) and " +
                      "saved the reusable overlay prefab to " + prefabPath +
                      ".\nThe overlay needs no placement at all — only size/position the Trigger's Box Collider " +
                      "to the tee.");
        }

        [MenuItem("Tools/chickenandribs/Video Animations/Create Intro Video Overlay from Video", true)]
        private static bool CreateValidate()
        {
            return Selection.activeObject is VideoClip;
        }

        // Every asset this tool creates lives at a deterministic, clip-name-based
        // path, so re-running the command on the same VideoClip (e.g. to pick up
        // a tool update) always targets the exact same paths as the previous run.
        // AssetDatabase.CreateAsset does not cleanly overwrite an existing asset
        // at its target path — deleting first makes every run a clean, full
        // replace instead of silently keeping stale data from an earlier run.
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

        // "Intro video package 1", "Intro video package 2", ... — scans the
        // active scene's root objects for the highest existing number and
        // returns the next one, so repeated use (e.g. one per tee box) doesn't
        // collide.
        private static string GetNextPackageName()
        {
            var regex = new System.Text.RegularExpressions.Regex(@"^Intro video package (\d+)$");
            int maxN = 0;

            GameObject[] roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (GameObject root in roots)
            {
                var match = regex.Match(root.name);
                if (match.Success)
                {
                    int n = int.Parse(match.Groups[1].Value);
                    if (n > maxN) maxN = n;
                }
            }

            return "Intro video package " + (maxN + 1);
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
