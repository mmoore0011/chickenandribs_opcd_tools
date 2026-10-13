// VehicleSpawnerEditor.cs
// Custom Inspector for VehicleSpawner.
// • When "Use Built-in Vehicles" is OFF the color pool and type-weight
//   sliders are hidden because they only apply to the procedural generator.
// • The Vehicle Sound section is always shown (both modes support audio).

using UnityEditor;
using UnityEngine;

namespace MovingVehicles
{
    [CustomEditor(typeof(VehicleSpawner))]
    public class VehicleSpawnerEditor : Editor
    {
        // ── Cached serialized properties ──────────────────────────────────
        SerializedProperty _vehiclePath;

        // Vehicle source
        SerializedProperty _useBuiltIn;
        SerializedProperty _customPrefabs;

        // Traffic density
        SerializedProperty _maxVehicles;
        SerializedProperty _minInterval;
        SerializedProperty _maxInterval;

        // Speed
        SerializedProperty _minSpeed;
        SerializedProperty _maxSpeed;

        // Sound (always shown)
        SerializedProperty _audioClip;
        SerializedProperty _audioVolume;
        SerializedProperty _audioMaxDist;

        // Appearance (built-in only)
        SerializedProperty _colorPool;

        // Type mix (built-in only)
        SerializedProperty _sedanW;
        SerializedProperty _hatchW;
        SerializedProperty _pickupW;
        SerializedProperty _tractorW;
        SerializedProperty _foodTruckW;

        // Scene organization
        SerializedProperty _vehicleParent;

        // ── Foldout state ──────────────────────────────────────────────────
        private bool _prefabsFoldout = true;

        // ── Styles ─────────────────────────────────────────────────────────
        private GUIStyle _sectionLabel;

        private void OnEnable()
        {
            _vehiclePath   = serializedObject.FindProperty("vehiclePath");

            _useBuiltIn    = serializedObject.FindProperty("useBuiltInVehicles");
            _customPrefabs = serializedObject.FindProperty("customVehiclePrefabs");

            _maxVehicles   = serializedObject.FindProperty("maxVehicles");
            _minInterval   = serializedObject.FindProperty("minSpawnInterval");
            _maxInterval   = serializedObject.FindProperty("maxSpawnInterval");

            _minSpeed      = serializedObject.FindProperty("minSpeed");
            _maxSpeed      = serializedObject.FindProperty("maxSpeed");

            _audioClip     = serializedObject.FindProperty("vehicleAudioClip");
            _audioVolume   = serializedObject.FindProperty("audioVolume");
            _audioMaxDist  = serializedObject.FindProperty("audioMaxDistance");

            _colorPool     = serializedObject.FindProperty("colorPool");

            _sedanW        = serializedObject.FindProperty("sedanWeight");
            _hatchW        = serializedObject.FindProperty("hatchbackWeight");
            _pickupW       = serializedObject.FindProperty("pickupWeight");
            _tractorW      = serializedObject.FindProperty("tractorWeight");
            _foodTruckW    = serializedObject.FindProperty("foodTruckWeight");

            _vehicleParent = serializedObject.FindProperty("vehicleParent");
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
            EditorGUILayout.PropertyField(_vehiclePath);

            GUILayout.Space(6);

            // ── Vehicle Source ────────────────────────────────────────────
            EditorGUILayout.LabelField("Vehicle Source", _sectionLabel);

            EditorGUILayout.PropertyField(_useBuiltIn,
                new GUIContent("Use Built-in Vehicles",
                    "Checked  → vehicles are built from the included low-poly generator.\n" +
                    "Unchecked → vehicles are randomly chosen from your Custom Prefabs list."));

            if (!_useBuiltIn.boolValue)
            {
                GUILayout.Space(2);
                EditorGUILayout.HelpBox(
                    "Add your own vehicle prefabs below.  One is chosen at random each spawn.\n" +
                    "• Prefabs should face  +Z  (forward / into the screen).\n" +
                    "• A VehicleController is added automatically at runtime\n" +
                    "  unless one already exists on the prefab root.",
                    MessageType.Info);

                // Draw the prefab array manually so we can label the count
                // field "Number of Objects" instead of Unity's default "Size".
                _prefabsFoldout = EditorGUILayout.Foldout(_prefabsFoldout,
                    "Custom Prefabs", true);

                if (_prefabsFoldout)
                {
                    EditorGUI.indentLevel++;

                    int newCount = EditorGUILayout.IntField(
                        new GUIContent("Number of Objects",
                            "How many different vehicle prefabs are in the list."),
                        _customPrefabs.arraySize);

                    newCount = Mathf.Max(0, newCount);
                    if (newCount != _customPrefabs.arraySize)
                        _customPrefabs.arraySize = newCount;

                    for (int i = 0; i < _customPrefabs.arraySize; i++)
                    {
                        EditorGUILayout.PropertyField(
                            _customPrefabs.GetArrayElementAtIndex(i),
                            new GUIContent("Vehicle " + (i + 1)));
                    }

                    EditorGUI.indentLevel--;
                }
            }

            GUILayout.Space(6);

            // ── Traffic Density (always shown) ────────────────────────────
            EditorGUILayout.LabelField("Traffic Density", _sectionLabel);
            EditorGUILayout.PropertyField(_maxVehicles,
                new GUIContent("Max Vehicles",
                    "Maximum vehicles on the road at the same time."));
            EditorGUILayout.PropertyField(_minInterval,
                new GUIContent("Min Spawn Interval",
                    "Minimum seconds between successive spawns."));
            EditorGUILayout.PropertyField(_maxInterval,
                new GUIContent("Max Spawn Interval",
                    "Maximum seconds between successive spawns."));

            GUILayout.Space(6);

            // ── Speed (always shown) ──────────────────────────────────────
            EditorGUILayout.LabelField("Vehicle Speed", _sectionLabel);
            EditorGUILayout.PropertyField(_minSpeed,
                new GUIContent("Min Speed  (m/s)",
                    "Slowest a vehicle can travel.  ~8 m/s ≈ 29 km/h."));
            EditorGUILayout.PropertyField(_maxSpeed,
                new GUIContent("Max Speed  (m/s)",
                    "Fastest a vehicle can travel.  ~14 m/s ≈ 50 km/h."));

            GUILayout.Space(6);

            // ── Vehicle Sound (always shown) ──────────────────────────────
            EditorGUILayout.LabelField("Vehicle Sound", _sectionLabel);

            EditorGUILayout.PropertyField(_audioClip,
                new GUIContent("Engine Clip",
                    "Audio clip looped on every vehicle (engine / tire noise).\n" +
                    "Leave empty for silent vehicles.\n\n" +
                    "For custom prefabs: if the prefab already has an AudioSource\n" +
                    "with a clip assigned, that clip is used and this field is ignored."));

            // Only show volume / distance when a clip is assigned or a prefab might carry one
            bool showAudioDetail = _audioClip.objectReferenceValue != null
                                || !_useBuiltIn.boolValue;

            if (showAudioDetail)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_audioVolume,
                    new GUIContent("Volume",
                        "Master volume for the engine sound (0 = silent, 1 = full)."));
                EditorGUILayout.PropertyField(_audioMaxDist,
                    new GUIContent("Max Distance  (m)",
                        "Distance at which the sound fully fades out.\n" +
                        "Tune to your scene scale – ~50 m is a good start."));
                EditorGUI.indentLevel--;

                if (!_useBuiltIn.boolValue && _audioClip.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        "No Engine Clip assigned here.  Vehicles will only have sound if their\n" +
                        "prefab already contains an AudioSource with a clip.",
                        MessageType.Info);
                }
            }

            // ── Built-in only ─────────────────────────────────────────────
            if (_useBuiltIn.boolValue)
            {
                GUILayout.Space(6);
                EditorGUILayout.LabelField("Appearance", _sectionLabel);
                EditorGUILayout.PropertyField(_colorPool,
                    new GUIContent("Body Color Pool",
                        "Body colors assigned at random to each spawned vehicle."),
                    true);

                GUILayout.Space(6);
                EditorGUILayout.LabelField("Vehicle Type Mix  (0 = never spawns)", _sectionLabel);
                EditorGUILayout.PropertyField(_sedanW,
                    new GUIContent("Sedan",           "Relative spawn frequency for sedan cars."));
                EditorGUILayout.PropertyField(_hatchW,
                    new GUIContent("Hatchback",       "Relative spawn frequency for hatchback / crossovers."));
                EditorGUILayout.PropertyField(_pickupW,
                    new GUIContent("Pickup Truck",    "Relative spawn frequency for pickup trucks."));
                EditorGUILayout.PropertyField(_tractorW,
                    new GUIContent("Tractor/Trailer", "Relative spawn frequency for 18-wheeler semis."));
                EditorGUILayout.PropertyField(_foodTruckW,
                    new GUIContent("Food Truck",      "Relative spawn frequency for BBQ food trucks."));
            }

            GUILayout.Space(6);

            // ── Scene Organization (always shown) ─────────────────────────
            EditorGUILayout.LabelField("Scene Organization  (optional)", _sectionLabel);
            EditorGUILayout.PropertyField(_vehicleParent,
                new GUIContent("Vehicle Parent",
                    "If assigned, spawned vehicles are parented here to keep the Hierarchy tidy.\n" +
                    "Leave empty to place vehicles at scene root."));

            serializedObject.ApplyModifiedProperties();
        }
    }
}
