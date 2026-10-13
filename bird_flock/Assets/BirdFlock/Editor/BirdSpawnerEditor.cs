// BirdSpawnerEditor.cs
// Custom Inspector for BirdSpawner.
// Shows the interval fields in minutes/hours alongside the raw second values
// so the timing is immediately obvious without mental conversion.

using UnityEditor;
using UnityEngine;

namespace BirdFlock
{
    [CustomEditor(typeof(BirdSpawner))]
    public class BirdSpawnerEditor : Editor
    {
        // ── Serialized properties ──────────────────────────────────────────
        SerializedProperty _birdPath;
        SerializedProperty _flightPattern;
        SerializedProperty _numberOfBirds;
        SerializedProperty _useBuiltInBirds;
        SerializedProperty _customBirdPrefabs;
        SerializedProperty _minInterval;
        SerializedProperty _maxInterval;
        SerializedProperty _minSpeed;
        SerializedProperty _maxSpeed;
        SerializedProperty _speedVariation;
        SerializedProperty _flockParent;
        SerializedProperty _birdAudioClip;
        SerializedProperty _audioVolume;
        SerializedProperty _audioMaxDistance;

        // ── Style ──────────────────────────────────────────────────────────
        private GUIStyle _sectionLabel;

        private void OnEnable()
        {
            _birdPath        = serializedObject.FindProperty("birdPath");
            _flightPattern   = serializedObject.FindProperty("flightPattern");
            _numberOfBirds   = serializedObject.FindProperty("numberOfBirds");
            _useBuiltInBirds    = serializedObject.FindProperty("useBuiltInBirds");
            _customBirdPrefabs  = serializedObject.FindProperty("customBirdPrefabs");
            _minInterval     = serializedObject.FindProperty("minInterval");
            _maxInterval     = serializedObject.FindProperty("maxInterval");
            _minSpeed        = serializedObject.FindProperty("minSpeed");
            _maxSpeed        = serializedObject.FindProperty("maxSpeed");
            _speedVariation  = serializedObject.FindProperty("speedVariation");
            _flockParent     = serializedObject.FindProperty("flockParent");
            _birdAudioClip   = serializedObject.FindProperty("birdAudioClip");
            _audioVolume     = serializedObject.FindProperty("audioVolume");
            _audioMaxDistance= serializedObject.FindProperty("audioMaxDistance");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (_sectionLabel == null)
            {
                _sectionLabel        = new GUIStyle(EditorStyles.boldLabel);
                _sectionLabel.margin = new RectOffset(0, 0, 6, 2);
            }

            // ── Path ──────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Path", _sectionLabel);
            EditorGUILayout.PropertyField(_birdPath,
                new GUIContent("Bird Path", "The BirdPath this spawner sends flocks along."));

            GUILayout.Space(6);

            // ── Flock ─────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Flock", _sectionLabel);

            EditorGUILayout.PropertyField(_flightPattern,
                new GUIContent("Flight Pattern",
                    "Formation shape for each flock.\n" +
                    "V Formation: classic asymmetric V with stragglers.\n" +
                    "Single Object: places one custom prefab at the spawn point — use for murmuration etc."));

            bool isSingleObject = _flightPattern.enumValueIndex == (int)FlightPattern.SingleObject;

            if (!isSingleObject)
            {
                EditorGUILayout.PropertyField(_numberOfBirds,
                    new GUIContent("Number of Birds",
                        "How many birds appear in each flock.\n" +
                        "The V Formation distributes them across leader, two wings, and 1-2 stragglers."));
            }

            if (isSingleObject && _useBuiltInBirds.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "Single Object pattern requires a custom prefab. " +
                    "Disable 'Use Built-In Birds' and assign your prefab below.",
                    MessageType.Warning);
            }

            GUILayout.Space(6);

            // ── Bird Source ───────────────────────────────────────────────
            EditorGUILayout.LabelField("Bird Source", _sectionLabel);

            EditorGUILayout.PropertyField(_useBuiltInBirds,
                new GUIContent("Use Built-In Birds",
                    "When enabled, birds are built from low-poly procedural geometry.\n" +
                    "Disable to supply your own bird prefab."));

            if (!_useBuiltInBirds.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "Add 1–6 bird prefabs. Each slot in the formation picks one at random.",
                    MessageType.Info);
                EditorGUILayout.PropertyField(_customBirdPrefabs,
                    new GUIContent("Bird Prefabs",
                        "1–6 prefabs to randomly assign across the flock formation."),
                    true);
                if (_customBirdPrefabs.arraySize > 6)
                    EditorGUILayout.HelpBox(
                        "Maximum 6 prefabs supported — remove the extras.",
                        MessageType.Warning);
            }

            GUILayout.Space(6);

            // ── Timing ────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Timing", _sectionLabel);

            EditorGUILayout.PropertyField(_minInterval,
                new GUIContent("Min Interval  (seconds)",
                    "Minimum wait before the next flock appears.\n" +
                    "60 = 1 minute  |  3600 = 1 hour"));

            // Human-readable hint beneath the slider
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(
                FormatSeconds(_minInterval.floatValue),
                EditorStyles.miniLabel);
            EditorGUI.indentLevel--;

            EditorGUILayout.PropertyField(_maxInterval,
                new GUIContent("Max Interval  (seconds)",
                    "Maximum wait before the next flock appears."));

            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(
                FormatSeconds(_maxInterval.floatValue),
                EditorStyles.miniLabel);
            EditorGUI.indentLevel--;

            // Warn if min > max
            if (_minInterval.floatValue > _maxInterval.floatValue)
            {
                EditorGUILayout.HelpBox(
                    "Min Interval is greater than Max Interval – Max will be clamped to Min at runtime.",
                    MessageType.Warning);
            }

            GUILayout.Space(6);

            // ── Speed ─────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Speed  (world units / second)", _sectionLabel);

            EditorGUILayout.PropertyField(_minSpeed,
                new GUIContent("Min Speed  (m/s)",
                    "Slowest a flock can travel.  ~8 m/s ≈ 29 km/h, typical geese cruising speed."));
            EditorGUILayout.PropertyField(_maxSpeed,
                new GUIContent("Max Speed  (m/s)",
                    "Fastest a flock can travel."));
            EditorGUILayout.PropertyField(_speedVariation,
                new GUIContent("Speed Variation  (bake only)",
                    "How much the speed fluctuates along the path when baked.\n" +
                    "0 = constant speed.  0.4 = speeds up and slows down by ±40%.\n" +
                    "Has no effect at runtime — only applied by the baker."));

            GUILayout.Space(6);

            // ── Scene Organization ────────────────────────────────────────
            EditorGUILayout.LabelField("Scene Organization  (optional)", _sectionLabel);
            EditorGUILayout.PropertyField(_flockParent,
                new GUIContent("Flock Parent",
                    "If assigned, spawned flocks are parented here to keep the Hierarchy tidy.\n" +
                    "Leave empty to place flocks at scene root."));

            GUILayout.Space(6);

            // ── Bird Sound ────────────────────────────────────────────────
            EditorGUILayout.LabelField("Bird Sound  (optional)", _sectionLabel);

            EditorGUILayout.PropertyField(_birdAudioClip,
                new GUIContent("Audio Clip",
                    "Optional sound played on each spawned flock (wing beats, calls, etc.).\n" +
                    "If the prefab already has an AudioSource with a clip, that clip takes priority."));

            if (_birdAudioClip.objectReferenceValue != null)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_audioVolume,
                    new GUIContent("Volume", "Volume of the flock audio source (0–1)."));
                EditorGUILayout.PropertyField(_audioMaxDistance,
                    new GUIContent("Max Distance",
                        "Distance (world units) at which the sound becomes inaudible."));
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }

        // ---------------------------------------------------------------
        // Helper: convert raw seconds to a readable string
        // ---------------------------------------------------------------
        private static string FormatSeconds(float seconds)
        {
            if (seconds < 60f)
                return "(" + seconds.ToString("F0") + " seconds)";

            float minutes = seconds / 60f;
            if (minutes < 60f)
                return "(" + minutes.ToString("F1") + " minutes)";

            float hours = minutes / 60f;
            return "(" + hours.ToString("F2") + " hours)";
        }
    }
}
