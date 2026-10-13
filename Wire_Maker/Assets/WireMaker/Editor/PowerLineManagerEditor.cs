using UnityEditor;
using UnityEngine;

namespace WireMaker
{
    [CustomEditor(typeof(PowerLineManager))]
    public class PowerLineManagerEditor : Editor
    {
        // ── Cached serialized properties ───────────────────────────────────
        SerializedProperty _attachmentPoints;
        SerializedProperty _sagAmount;
        SerializedProperty _segmentsPerSpan;
        SerializedProperty _wireRadius;
        SerializedProperty _wireMaterial;

        // ── Styles ─────────────────────────────────────────────────────────
        private GUIStyle _sectionLabel;

        private void OnEnable()
        {
            _attachmentPoints = serializedObject.FindProperty("attachmentPoints");
            _sagAmount        = serializedObject.FindProperty("sagAmount");
            _segmentsPerSpan  = serializedObject.FindProperty("segmentsPerSpan");
            _wireRadius       = serializedObject.FindProperty("wireRadius");
            _wireMaterial     = serializedObject.FindProperty("wireMaterial");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (_sectionLabel == null)
            {
                _sectionLabel        = new GUIStyle(EditorStyles.boldLabel);
                _sectionLabel.margin = new RectOffset(0, 0, 6, 2);
            }

            // ── Attachment Points ─────────────────────────────────────────
            EditorGUILayout.LabelField("Attachment Points", _sectionLabel);
            EditorGUILayout.HelpBox(
                "Assign the top-of-pole Transform for each pole in span order.\n" +
                "One wire is generated per consecutive pair of points.\n" +
                "Tip: create an empty child GameObject at the cable-anchor spot on each pole.",
                MessageType.Info);

            EditorGUILayout.PropertyField(_attachmentPoints,
                new GUIContent("Points", "Top-of-pole transforms in order."), true);

            GUILayout.Space(6);

            // ── Wire Settings ─────────────────────────────────────────────
            EditorGUILayout.LabelField("Wire Settings", _sectionLabel);

            EditorGUILayout.PropertyField(_sagAmount,
                new GUIContent("Sag Amount  (m)",
                    "Maximum downward displacement at the midpoint of each wire span.\n" +
                    "Uniform across all spans regardless of span length."));

            EditorGUILayout.PropertyField(_segmentsPerSpan,
                new GUIContent("Segments Per Span",
                    "Number of cylinder segments that approximate each catenary curve.\n" +
                    "4–8 is usually plenty; higher values cost more draw calls."));

            EditorGUILayout.PropertyField(_wireRadius,
                new GUIContent("Wire Radius  (m)",
                    "Visual radius of the wire cylinders."));

            EditorGUILayout.PropertyField(_wireMaterial,
                new GUIContent("Wire Material",
                    "Material for wire cylinders.\n" +
                    "Leave empty to auto-create a flat black Standard material."));

            GUILayout.Space(10);

            // ── Build / Clear ─────────────────────────────────────────────
            PowerLineManager mgr = (PowerLineManager)target;

            bool canBuild = mgr.attachmentPoints != null && mgr.attachmentPoints.Length >= 2;
            bool hasWires = mgr.generatedWires != null && mgr.generatedWires.Count > 0;

            EditorGUI.BeginDisabledGroup(!canBuild);
            if (GUILayout.Button("Build Wires", GUILayout.Height(30)))
            {
                serializedObject.ApplyModifiedProperties();
                mgr.BuildWires();
                EditorUtility.SetDirty(mgr);
            }
            EditorGUI.EndDisabledGroup();

            if (!canBuild)
            {
                EditorGUILayout.HelpBox(
                    "Assign at least 2 Attachment Points to enable Build.",
                    MessageType.Warning);
            }

            GUILayout.Space(2);

            EditorGUI.BeginDisabledGroup(!hasWires);
            if (GUILayout.Button("Clear Wires"))
            {
                mgr.ClearWires();
                EditorUtility.SetDirty(mgr);
            }
            EditorGUI.EndDisabledGroup();

            if (hasWires)
            {
                GUILayout.Space(4);
                EditorGUILayout.HelpBox(
                    mgr.generatedWires.Count + " cylinder segments active.  " +
                    "Click Build Wires again after changing any setting.",
                    MessageType.None);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            PowerLineManager mgr = (PowerLineManager)target;
            if (mgr.attachmentPoints == null) return;

            // Draw a small yellow disc at each attachment point for visibility.
            Handles.color = new Color(1f, 0.9f, 0f, 0.8f);
            foreach (Transform pt in mgr.attachmentPoints)
            {
                if (pt == null) continue;
                Handles.DrawWireDisc(pt.position, Vector3.up, 0.12f);
            }
        }
    }
}
