// VehiclePathEditor.cs
// Custom Inspector + Scene-view handles for VehiclePath.
// Lets you add/remove waypoints, drag them in the Scene view,
// and snap them to the road/terrain surface with one click.
//
// SNAP TO GROUND
// Uses Physics.Raycast downward from above each waypoint.
// Requirements: the road / terrain mesh must have a Collider component
// (MeshCollider, TerrainCollider, etc.).  If a waypoint misses all
// colliders the console will tell you which one(s) were skipped.
//
// This file MUST stay inside an Editor folder – it is stripped from builds.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MovingVehicles
{
    [CustomEditor(typeof(VehiclePath))]
    public class VehiclePathEditor : Editor
    {
        private VehiclePath _path;

        // Snap settings – persist for the current editor session only
        // (fine; user sets it once, clicks button, done)
        private float _snapOffset    = 0.05f;   // metres above the hit surface
        private float _snapSearchUp  = 500f;    // how far above waypoint to start the ray

        // ---------------------------------------------------------------
        // Inspector GUI
        // ---------------------------------------------------------------

        public override void OnInspectorGUI()
        {
            _path = (VehiclePath)target;

            // Draw the default inspector fields (spawnLeadDistance etc.)
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Waypoints", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Tip: hover over the road/terrain in the Scene view and press G to place a waypoint there.",
                MessageType.None);

            // List all current waypoints
            var children = GetChildren();
            if (children.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No waypoints yet. Click '+ Add Waypoint' to start.",
                    MessageType.Info);
            }
            else
            {
                for (int i = 0; i < children.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();

                    var   stopComp = children[i].GetComponent<WaypointStop>();
                    string stopStr = (stopComp != null && stopComp.stopDuration > 0f)
                        ? "  [STOP " + stopComp.stopDuration.ToString("F1") + "s]"
                        : "";
                    string label = "  " + i.ToString("D2") + "  " + children[i].name + stopStr;
                    EditorGUILayout.LabelField(label, GUILayout.ExpandWidth(true));

                    // Select in Hierarchy
                    if (GUILayout.Button("Select", GUILayout.Width(60)))
                        Selection.activeGameObject = children[i].gameObject;

                    // Remove this individual waypoint
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

            // ── Add / Remove ──────────────────────────────────────────────
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

            // ── Status summary ────────────────────────────────────────────
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

            // ── Ground Snapping ───────────────────────────────────────────
            EditorGUILayout.Space();
            DrawSeparator();
            EditorGUILayout.LabelField("Ground Snapping", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Casts a ray straight down from each waypoint and lands it on the " +
                "first collider below it (road mesh, terrain, etc.).\n" +
                "Make sure your road / terrain has a Collider component attached.",
                MessageType.Info);

            // Surface offset field
            _snapOffset = EditorGUILayout.FloatField(
                new GUIContent(
                    "Surface Offset  (m)",
                    "How far above the hit surface to place the waypoint.\n" +
                    "A small value (0.05 m) stops it from sinking into the road mesh."),
                _snapOffset);

            _snapOffset = Mathf.Max(0f, _snapOffset);  // clamp to non-negative

            // The big green button
            GUI.enabled = children.Count > 0;
            GUI.color   = new Color(0.5f, 1f, 0.6f);

            if (GUILayout.Button("⬇  Snap All Waypoints to Ground", GUILayout.Height(30)))
                SnapWaypointsToGround(children);

            GUI.color   = Color.white;
            GUI.enabled = true;
        }

        // ---------------------------------------------------------------
        // Scene GUI – draggable position handles + labels
        // ---------------------------------------------------------------

        private void OnSceneGUI()
        {
            _path = (VehiclePath)target;
            var children = GetChildren();

            for (int i = 0; i < children.Count; i++)
            {
                Transform wp = children[i];

                // Waypoint number label (+ stop duration if present)
                var    stopComp = wp.GetComponent<WaypointStop>();
                string wpLabel  = "WP " + i;
                if (stopComp != null && stopComp.stopDuration > 0f)
                    wpLabel += "  STOP " + stopComp.stopDuration.ToString("F1") + "s";
                Handles.Label(wp.position + Vector3.up * 1.2f, wpLabel, EditorStyles.boldLabel);

                // Draggable position handle
                EditorGUI.BeginChangeCheck();
                Vector3 newPos = Handles.PositionHandle(wp.position, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(wp, "Move Waypoint");
                    wp.position = newPos;
                }
            }

            // ── G key: place new waypoint at mouse surface position ────────
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.G)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit))
                {
                    PlaceWaypointAtPosition(children, hit.point + Vector3.up * _snapOffset);
                }
                else
                {
                    Debug.LogWarning(
                        "[MovingVehicles] G pressed but mouse did not hit any collider. " +
                        "Make sure your road/terrain has a Collider component.");
                }
                e.Use();
            }
        }

        // ---------------------------------------------------------------
        // Place waypoint at an explicit world position (used by G key)
        // ---------------------------------------------------------------

        private void PlaceWaypointAtPosition(List<Transform> existing, Vector3 position)
        {
            var wpGO = new GameObject("Waypoint_" + existing.Count.ToString("D2"));
            Undo.RegisterCreatedObjectUndo(wpGO, "Place Waypoint");
            Undo.SetTransformParent(wpGO.transform, _path.transform, "Place Waypoint");
            wpGO.transform.position = position;
            // Keep VehiclePath selected so G can be pressed again immediately.
            SceneView.RepaintAll();
        }

        // ---------------------------------------------------------------
        // Ground snap logic
        // ---------------------------------------------------------------

        private void SnapWaypointsToGround(List<Transform> waypoints)
        {
            if (waypoints.Count == 0) return;

            int snapped = 0;
            int missed  = 0;

            foreach (Transform wp in waypoints)
            {
                // Start the ray well above the waypoint's current Y so it works
                // whether the waypoint was placed in the air, at grade, or
                // accidentally underground.
                Vector3 origin = new Vector3(
                    wp.position.x,
                    wp.position.y + _snapSearchUp,
                    wp.position.z);

                RaycastHit hit;

                // Cast downward.  maxDistance = searchUp * 2 so we cover the
                // same distance below the waypoint as above it.
                if (Physics.Raycast(origin, Vector3.down, out hit, _snapSearchUp * 2f))
                {
                    Undo.RecordObject(wp, "Snap Waypoint to Ground");
                    wp.position = hit.point + Vector3.up * _snapOffset;
                    snapped++;
                }
                else
                {
                    Debug.LogWarning(
                        "[MovingVehicles] Waypoint '" + wp.name + "' on path '" +
                        _path.name + "' did not hit any collider below it.  " +
                        "Check that the road/terrain has a Collider component.", wp);
                    missed++;
                }
            }

            // Repaint the scene so updated positions show immediately
            SceneView.RepaintAll();

            string msg = "[MovingVehicles] Snapped " + snapped + " of " +
                         waypoints.Count + " waypoint(s) to ground.";

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
                newPos = _path.transform.position + Vector3.forward * 5f;
            }
            else if (existing.Count == 1)
            {
                newPos = existing[0].position + Vector3.forward * 10f;
            }
            else
            {
                Vector3 lastDir = (existing[existing.Count - 1].position
                                 - existing[existing.Count - 2].position).normalized;
                float   lastLen = Vector3.Distance(
                                    existing[existing.Count - 1].position,
                                    existing[existing.Count - 2].position);
                newPos = existing[existing.Count - 1].position
                       + lastDir * Mathf.Max(lastLen, 5f);
            }

            var wpGO = new GameObject("Waypoint_" + existing.Count.ToString("D2"));
            Undo.RegisterCreatedObjectUndo(wpGO, "Add Waypoint");
            Undo.SetTransformParent(wpGO.transform, _path.transform, "Add Waypoint");
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
        // Misc helpers
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
