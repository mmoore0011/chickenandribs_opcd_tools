// TriggerFormationSpawnerEditor.cs  –  Editor only
// Custom inspector for TriggerFormationSpawner:
//  - Hides Count / Animated Count / Spacing when Arrangement is Single Object
//    (they're meaningless for a single instance).
//  - Draws a Scene-view gizmo preview of the formation block at the path's
//    spawn point, color-coding which slots will get a full Animator
//    (animated front ranks, cyan) vs. which will be folded into the static
//    combined filler mesh (grey) — see TriggerFormationBaker's tiered
//    fidelity notes for why that split exists. This tool has no Play-mode
//    preview simulation, so this gizmo is the only layout feedback before
//    baking.

using UnityEditor;
using UnityEngine;

namespace TriggerFormations
{
    [CustomEditor(typeof(TriggerFormationSpawner))]
    [CanEditMultipleObjects]
    public class TriggerFormationSpawnerEditor : Editor
    {
        private SerializedProperty _path;
        private SerializedProperty _characterPrefab;
        private SerializedProperty _arrangement;
        private SerializedProperty _count;
        private SerializedProperty _animatedCount;
        private SerializedProperty _spacing;
        private SerializedProperty _speed;
        private SerializedProperty _boolParameterName;
        private SerializedProperty _playCount;
        private SerializedProperty _audioClip;
        private SerializedProperty _loopAudio;
        private SerializedProperty _audioVolume;
        private SerializedProperty _audioMaxDistance;

        private void OnEnable()
        {
            _path              = serializedObject.FindProperty("path");
            _characterPrefab   = serializedObject.FindProperty("characterPrefab");
            _arrangement       = serializedObject.FindProperty("arrangement");
            _count             = serializedObject.FindProperty("count");
            _animatedCount     = serializedObject.FindProperty("animatedCount");
            _spacing           = serializedObject.FindProperty("spacing");
            _speed             = serializedObject.FindProperty("speed");
            _boolParameterName = serializedObject.FindProperty("boolParameterName");
            _playCount         = serializedObject.FindProperty("playCount");
            _audioClip         = serializedObject.FindProperty("audioClip");
            _loopAudio         = serializedObject.FindProperty("loopAudio");
            _audioVolume       = serializedObject.FindProperty("audioVolume");
            _audioMaxDistance  = serializedObject.FindProperty("audioMaxDistance");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_path);
            EditorGUILayout.PropertyField(_characterPrefab);

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_arrangement);

            bool grouped = _arrangement.enumValueIndex == (int)TriggerFormationSpawner.Arrangement.GroupedObjects;
            if (grouped)
            {
                EditorGUILayout.PropertyField(_count);
                EditorGUILayout.PropertyField(_animatedCount);
                EditorGUILayout.PropertyField(_spacing);
            }

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_speed);

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_boolParameterName);
            EditorGUILayout.PropertyField(_playCount);

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_audioClip);
            if (_audioClip.objectReferenceValue != null)
            {
                EditorGUILayout.PropertyField(_loopAudio);
                EditorGUILayout.PropertyField(_audioVolume);
                EditorGUILayout.PropertyField(_audioMaxDistance);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            var spawner = (TriggerFormationSpawner)target;
            if (spawner.path == null) return;

            Vector3 spawnPos;
            Quaternion spawnRot;
            if (!spawner.path.Sample(0f, out spawnPos, out spawnRot)) return;

            bool grouped = spawner.arrangement == TriggerFormationSpawner.Arrangement.GroupedObjects;
            Vector3[] offsets = grouped
                ? FormationBuilder.GetGridOffsets(spawner.count, spawner.spacing)
                : new[] { Vector3.zero };
            int animatedCount = grouped ? Mathf.Clamp(spawner.animatedCount, 1, spawner.count) : 1;

            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3 worldPos = spawnPos + spawnRot * offsets[i];
                Handles.color = (i < animatedCount) ? Color.cyan : new Color(0.6f, 0.6f, 0.6f);
                Handles.SphereHandleCap(0, worldPos, spawnRot, 0.4f, EventType.Repaint);
            }
        }
    }
}
