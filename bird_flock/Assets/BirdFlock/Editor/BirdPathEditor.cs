// BirdPathEditor.cs
// Custom Inspector + Scene-view handles for BirdPath.
// Lets you add/remove waypoints, drag them in the Scene view,
// and snap them to terrain or geometry with one click.
//
// SNAP TO GROUND
// Uses Physics.Raycast downward from above each waypoint.
// Set your waypoints high (birds fly at altitude) then snap to the
// terrain to confirm they're above it, or place them freely in the air.
//
// This file MUST stay inside an Editor folder – it is stripped from builds.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BirdFlock
{
    [CustomEditor(typeof(BirdPath))]
    public class BirdPathEditor : Editor
    {
        private BirdPath _path;
        private float    _snapOffset   = 20f;   // metres above terrain surface (birds fly high)
        private float    _snapSearchUp = 500f;

        // ---------------------------------------------------------------
        // Inspector GUI
        // ---------------------------------------------------------------

        public override void OnInspectorGUI()
        {
            _path = (BirdPath)target;

            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Waypoints", EditorStyles.boldLabel);

            var children = GetChildren();

            if (children.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No waypoints yet.  Click '+ Add Waypoint' to start.",
                    MessageType.Info);
            }
            else
            {
                for (int i = 0; i < children.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();

                    EditorGUILayout.LabelField(
                        "  " + i.ToString("D2") + "  " + children[i].name,
                        GUILayout.ExpandWidth(true));

                    if (GUILayout.Button("Select", GUILayout.Width(60)))
                        Selection.activeGameObject = children[i].gameObject;

                    GUI.color = new Color(1f, 0.5f, 0.5f);
                    if (GUILayout.Button("✕", GUILayout.Width(26)))
                    {
                        Undo.DestroyObjectImmediate(children[i].gameObject);
                        GUIUtility.ExitGUI();
                        return;
                    }
                    GUI.color = Color.white;

                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Space();

            // ── Add / Remove ─────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("+ Add Waypoint"))
                AddWaypoint(children);

            GUI.enabled = children.Count > 0;
            GUI.color   = new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Button("Remove Last"))
                RemoveLastWaypoint(children);
            GUI.color   = Color.white;
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            // ── Status summary ─────────────────────────────────────────────
            EditorGUILayout.Space();
            if (children.Count >= 2)
            {
                float total = 0f;
                for (int i = 0; i < children.Count - 1; i++)
                    total += Vector3.Distance(children[i].position, children[i + 1].position);

                EditorGUILayout.HelpBox(
                    string.Format(
                        "Waypoints: {0}   Path length: {1:F1} m   " +
                        "Total travel: {2:F1} m  (incl. spawn/despawn lead)",
                        children.Count,
                        total,
                        total + _path.spawnLeadDistance + _path.despawnLeadDistance),
                    MessageType.None);
            }
            else if (children.Count == 1)
            {
                EditorGUILayout.HelpBox("Add at least one more waypoint.", MessageType.Warning);
            }

            // ── Ground / Terrain Snap ──────────────────────────────────────
            EditorGUILayout.Space();
            DrawSeparator();
            EditorGUILayout.LabelField("Terrain Snap", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Casts a ray straight down from each waypoint and lands it on the " +
                "first collider below (terrain, ground mesh, etc.).\n" +
                "Use 'Surface Offset' to keep birds at a comfortable altitude above the terrain.",
                MessageType.Info);

            _snapOffset = EditorGUILayout.FloatField(
                new GUIContent(
                    "Altitude Above Surface  (m)",
                    "How high above the terrain hit-point to place the waypoint.\n" +
                    "Birds typically fly 15–40 m above the ground."),
                _snapOffset);
            _snapOffset = Mathf.Max(0f, _snapOffset);

            GUI.enabled = children.Count > 0;
            GUI.color   = new Color(0.5f, 1f, 0.6f);
            if (GUILayout.Button("⬇  Snap All Waypoints to Terrain  (+ offset)", GUILayout.Height(30)))
                SnapWaypointsToGround(children);
            GUI.color   = Color.white;
            GUI.enabled = true;
        }

        // ---------------------------------------------------------------
        // Scene GUI
        // ---------------------------------------------------------------

        private void OnSceneGUI()
        {
            _path = (BirdPath)target;
            var children = GetChildren();

            for (int i = 0; i < children.Count; i++)
            {
                Transform wp = children[i];

                Handles.Label(
                    wp.position + Vector3.up * 2.0f,
                    "WP " + i,
                    EditorStyles.boldLabel);

                EditorGUI.BeginChangeCheck();
                Vector3 newPos = Handles.PositionHandle(wp.position, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(wp, "Move Bird Waypoint");
                    wp.position = newPos;
                }
            }
        }

        // ---------------------------------------------------------------
        // Ground snap
        // ---------------------------------------------------------------

        private void SnapWaypointsToGround(List<Transform> waypoints)
        {
            if (waypoints.Count == 0) return;

            int snapped = 0, missed = 0;

            foreach (Transform wp in waypoints)
            {
                Vector3 origin = new Vector3(
                    wp.position.x,
                    wp.position.y + _snapSearchUp,
                    wp.position.z);

                RaycastHit hit;
                if (Physics.Raycast(origin, Vector3.down, out hit, _snapSearchUp * 2f))
                {
                    Undo.RecordObject(wp, "Snap Bird Waypoint to Terrain");
                    wp.position = hit.point + Vector3.up * _snapOffset;
                    snapped++;
                }
                else
                {
                    Debug.LogWarning(
                        "[BirdFlock] Waypoint '" + wp.name + "' on path '" +
                        _path.name + "' did not hit any collider below it.", wp);
                    missed++;
                }
            }

            SceneView.RepaintAll();

            string msg = "[BirdFlock] Snapped " + snapped + " of " +
                         waypoints.Count + " waypoint(s) to terrain.";
            if (missed > 0)
                msg += "  " + missed + " waypoint(s) missed – see warnings above.";
            Debug.Log(msg, _path);
        }

        // ---------------------------------------------------------------
        // Add / Remove waypoints
        // ---------------------------------------------------------------

        private void AddWaypoint(List<Transform> existing)
        {
            Vector3 newPos;
            if (existing.Count == 0)
            {
                newPos = _path.transform.position + Vector3.forward * 10f + Vector3.up * 30f;
            }
            else if (existing.Count == 1)
            {
                newPos = existing[0].position + Vector3.forward * 20f;
            }
            else
            {
                Vector3 lastDir = (existing[existing.Count - 1].position
                                 - existing[existing.Count - 2].position).normalized;
                float   lastLen = Vector3.Distance(
                                    existing[existing.Count - 1].position,
                                    existing[existing.Count - 2].position);
                newPos = existing[existing.Count - 1].position
                       + lastDir * Mathf.Max(lastLen, 10f);
            }

            var wpGO = new GameObject("Waypoint_" + existing.Count.ToString("D2"));
            Undo.RegisterCreatedObjectUndo(wpGO, "Add Bird Waypoint");
            Undo.SetTransformParent(wpGO.transform, _path.transform, "Add Bird Waypoint");
            wpGO.transform.position = newPos;

            Selection.activeGameObject = wpGO;

            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.pivot = newPos;
        }

        private void RemoveLastWaypoint(List<Transform> existing)
        {
            if (existing.Count == 0) return;
            Undo.DestroyObjectImmediate(existing[existing.Count - 1].gameObject);
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private List<Transform> GetChildren()
        {
            var list = new List<Transform>();
            foreach (Transform child in _path.transform)
                list.Add(child);
            return list;
        }

        private static void DrawSeparator()
        {
            Rect r = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.4f));
            GUILayout.Space(2f);
        }
    }
}
