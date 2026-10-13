// VehiclePath.cs
// Defines the road path that vehicles follow.
//
// HOW TO SET UP IN THE EDITOR
// ----------------------------
// 1. Create an empty GameObject and add this component.
// 2. Create child GameObjects as waypoints (e.g. name them Waypoint_01, 02 …).
//    The order in Unity's Hierarchy panel is the travel order.
// 3. Position the first and last waypoints in parts of the scene that are
//    NOT visible to the player camera (they are the spawn/despawn zones).
// 4. Adjust spawnLeadDistance / despawnLeadDistance to push the actual
//    appear/disappear points even further off-screen if needed.
//
// GIZMO COLOURS
// Green sphere  = spawn point    (vehicle appears here)
// Red   sphere  = despawn point  (vehicle disappears here)
// Cyan  line    = path between waypoints
// Yellow arrow  = travel direction

using System.Collections.Generic;
using UnityEngine;

namespace MovingVehicles
{
    public class VehiclePath : MonoBehaviour
    {
        // ---------------------------------------------------------------
        // Inspector fields
        // ---------------------------------------------------------------

        [Header("Spawn / Despawn Lead")]
        [Tooltip("How far before the first waypoint vehicles are created (should be off-screen).")]
        public float spawnLeadDistance = 6f;

        [Tooltip("How far after the last waypoint vehicles are destroyed (should be off-screen).")]
        public float despawnLeadDistance = 6f;

        [Header("Gizmos")]
        public Color pathColor  = new Color(0.2f, 0.9f, 0.9f);
        public float  nodeRadius = 0.5f;

        // ---------------------------------------------------------------
        // Runtime cache (built in Awake)
        // ---------------------------------------------------------------
        private Transform[] _waypoints;
        private float[]     _segLengths;      // length of each segment
        private float       _pathLength;      // sum of segment lengths
        private float[]     _waypointDists;   // path-local cumulative distance at each waypoint
        private float[]     _stopDurations;   // parallel to _waypoints; 0 if no WaypointStop
        private float       _totalStopTime;
        private bool        _ready;

        // ---------------------------------------------------------------
        // Public interface
        // ---------------------------------------------------------------

        /// <summary>Total path length (waypoint 0 to last waypoint).</summary>
        public float PathLength
        {
            get { EnsureReady(); return _pathLength; }
        }

        /// <summary>Full distance a vehicle travels from first spawn to final despawn.</summary>
        public float TotalTravelDistance
        {
            get { return spawnLeadDistance + PathLength + despawnLeadDistance; }
        }

        /// <summary>Number of waypoints found.</summary>
        public int WaypointCount
        {
            get { EnsureReady(); return _waypoints != null ? _waypoints.Length : 0; }
        }

        /// <summary>
        /// Path-local cumulative distance at each waypoint (index 0 = 0, index N = PathLength).
        /// Parallel to StopDurations.
        /// </summary>
        public float[] WaypointDists
        {
            get { EnsureReady(); return _waypointDists; }
        }

        /// <summary>
        /// Stop duration (seconds) at each waypoint; 0 means no stop.
        /// Parallel to WaypointDists.
        /// </summary>
        public float[] StopDurations
        {
            get { EnsureReady(); return _stopDurations; }
        }

        /// <summary>Sum of all per-waypoint stop durations on this path.</summary>
        public float TotalStopTime
        {
            get { EnsureReady(); return _totalStopTime; }
        }

        /// <summary>
        /// Samples world position and rotation at 'distance' along the full travel range.
        ///   distance == 0                    → spawn point (off-screen, before first waypoint)
        ///   distance == TotalTravelDistance  → despawn point (off-screen, after last waypoint)
        /// Returns false if the path has fewer than 2 waypoints.
        /// </summary>
        public bool Sample(float distance, out Vector3 position, out Quaternion rotation)
        {
            EnsureReady();

            if (!_ready)
            {
                position = transform.position;
                rotation = transform.rotation;
                return false;
            }

            // Convert to path-local distance (0 = first waypoint)
            float d = distance - spawnLeadDistance;

            Vector3 startDir = (_waypoints[1].position - _waypoints[0].position).normalized;
            Vector3 endDir   = (_waypoints[_waypoints.Length - 1].position
                               - _waypoints[_waypoints.Length - 2].position).normalized;

            // Before path start → extend backward along start direction
            if (d < 0f)
            {
                position = _waypoints[0].position - startDir * (-d);
                rotation = Quaternion.LookRotation(startDir);
                return true;
            }

            // After path end → extend forward along end direction
            if (d >= _pathLength)
            {
                float overrun = d - _pathLength;
                position = _waypoints[_waypoints.Length - 1].position + endDir * overrun;
                rotation = Quaternion.LookRotation(endDir);
                return true;
            }

            // On the path – walk through segments
            float remaining = d;
            for (int i = 0; i < _segLengths.Length; i++)
            {
                if (remaining <= _segLengths[i])
                {
                    float t   = (i == 0 && _segLengths[i] < 0.0001f) ? 0f : remaining / _segLengths[i];
                    Vector3 dir = (_waypoints[i + 1].position - _waypoints[i].position).normalized;
                    position = Vector3.LerpUnclamped(_waypoints[i].position, _waypoints[i + 1].position, t);
                    rotation = Quaternion.LookRotation(dir);
                    return true;
                }
                remaining -= _segLengths[i];
            }

            // Fallback (floating-point overshoot)
            position = _waypoints[_waypoints.Length - 1].position;
            rotation = Quaternion.LookRotation(endDir);
            return true;
        }

        // ---------------------------------------------------------------
        // Unity messages
        // ---------------------------------------------------------------

        private void Awake()
        {
            Build();
        }

        // ---------------------------------------------------------------
        // Internal helpers
        // ---------------------------------------------------------------

        private void EnsureReady()
        {
            if (!_ready) Build();
        }

        private void Build()
        {
            _ready = false;

            // Collect children in sibling-index order
            var list = new List<Transform>();
            foreach (Transform child in transform)
                list.Add(child);

            if (list.Count < 2)
            {
                Debug.LogWarning(
                    "[MovingVehicles] VehiclePath on '" + gameObject.name +
                    "' needs at least 2 child waypoints.", this);
                return;
            }

            _waypoints  = list.ToArray();
            _segLengths = new float[_waypoints.Length - 1];
            _pathLength = 0f;

            for (int i = 0; i < _segLengths.Length; i++)
            {
                _segLengths[i] = Vector3.Distance(_waypoints[i].position,
                                                  _waypoints[i + 1].position);
                _pathLength   += _segLengths[i];
            }

            // Build per-waypoint cumulative distances and stop durations
            _waypointDists = new float[_waypoints.Length];
            _stopDurations = new float[_waypoints.Length];
            _totalStopTime = 0f;
            _waypointDists[0] = 0f;
            for (int i = 0; i < _segLengths.Length; i++)
                _waypointDists[i + 1] = _waypointDists[i] + _segLengths[i];
            for (int i = 0; i < _waypoints.Length; i++)
            {
                var stop = _waypoints[i].GetComponent<WaypointStop>();
                _stopDurations[i] = (stop != null) ? Mathf.Max(0f, stop.stopDuration) : 0f;
                _totalStopTime   += _stopDurations[i];
            }

            _ready = true;
        }

        // ---------------------------------------------------------------
        // Gizmos (visible in Scene view at all times)
        // ---------------------------------------------------------------

        private void OnDrawGizmos()
        {
            // Collect children without allocating a managed list every frame would
            // be ideal, but OnDrawGizmos only runs in editor – perf is not a concern.
            var children = new List<Transform>();
            foreach (Transform child in transform)
                children.Add(child);

            if (children.Count == 0) return;

            // Waypoint nodes + path lines
            for (int i = 0; i < children.Count; i++)
            {
                var stop = children[i].GetComponent<WaypointStop>();
                Gizmos.color = (stop != null && stop.stopDuration > 0f)
                    ? new Color(1f, 0.55f, 0f)   // orange = stop waypoint
                    : pathColor;
                Gizmos.DrawSphere(children[i].position, nodeRadius);
                if (i < children.Count - 1)
                {
                    Gizmos.color = pathColor;
                    Gizmos.DrawLine(children[i].position, children[i + 1].position);
                }
            }

            if (children.Count < 2) return;

            // Spawn lead (green)
            Vector3 s0  = children[0].position;
            Vector3 s1  = children[1].position;
            Vector3 sDir = (s1 - s0).normalized;
            Vector3 spawnPt = s0 - sDir * spawnLeadDistance;

            Gizmos.color = Color.green;
            Gizmos.DrawSphere(spawnPt, nodeRadius * 0.6f);
            Gizmos.DrawLine(spawnPt, s0);

            // Despawn lead (red)
            int     n   = children.Count;
            Vector3 e0  = children[n - 2].position;
            Vector3 e1  = children[n - 1].position;
            Vector3 eDir = (e1 - e0).normalized;
            Vector3 despawnPt = e1 + eDir * despawnLeadDistance;

            Gizmos.color = Color.red;
            Gizmos.DrawSphere(despawnPt, nodeRadius * 0.6f);
            Gizmos.DrawLine(e1, despawnPt);

            // Direction arrow at the mid-point
            int mid = children.Count / 2;
            if (mid < children.Count - 1)
            {
                Vector3 arrowPos = children[mid].position;
                Vector3 arrowDir = (children[mid + 1].position - children[mid].position).normalized;
                DrawGizmoArrow(arrowPos, arrowDir, 2.0f);
            }
        }

        private static void DrawGizmoArrow(Vector3 origin, Vector3 dir, float size)
        {
            Gizmos.color = Color.yellow;
            Vector3 tip   = origin + dir * size;
            Vector3 right = Vector3.Cross(dir, Vector3.up).normalized;

            Gizmos.DrawLine(origin, tip);
            Gizmos.DrawLine(tip, tip - dir * size * 0.35f + right * size * 0.25f);
            Gizmos.DrawLine(tip, tip - dir * size * 0.35f - right * size * 0.25f);
        }

    }
}
