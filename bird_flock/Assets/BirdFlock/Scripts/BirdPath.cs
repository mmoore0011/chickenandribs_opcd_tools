// BirdPath.cs
// Defines the flight path that bird flocks follow.
//
// HOW TO SET UP
// 1. Create an empty GameObject and add this component.
// 2. Add child GameObjects as waypoints – they are followed in Hierarchy order.
// 3. Place the first and last waypoints somewhere NOT visible to the camera
//    (off-screen, behind terrain, etc.) so flocks appear and disappear naturally.
// 4. Set waypoints at the altitude you want the birds to fly at.
//
// GIZMO COLOURS
// Cyan  line   = flight path between waypoints
// Green sphere = spawn point  (flock appears here)
// Red   sphere = despawn point (flock disappears here)
// Yellow arrow = travel direction

using System.Collections.Generic;
using UnityEngine;

namespace BirdFlock
{
    public class BirdPath : MonoBehaviour
    {
        [Header("Spawn / Despawn Lead")]
        [Tooltip("How far before the first waypoint flocks appear (should be off-screen).")]
        public float spawnLeadDistance = 10f;

        [Tooltip("How far after the last waypoint flocks disappear (should be off-screen).")]
        public float despawnLeadDistance = 10f;

        [Header("Gizmos")]
        public Color pathColor  = new Color(0.2f, 0.9f, 0.9f);
        public float  nodeRadius = 0.8f;

        // ---------------------------------------------------------------
        // Runtime cache
        // ---------------------------------------------------------------
        private Transform[] _waypoints;
        private float[]     _segLengths;
        private float       _pathLength;
        private bool        _ready;

        // ---------------------------------------------------------------
        // Public interface
        // ---------------------------------------------------------------

        public float PathLength
        {
            get { EnsureReady(); return _pathLength; }
        }

        public float TotalTravelDistance
        {
            get { return spawnLeadDistance + PathLength + despawnLeadDistance; }
        }

        public int WaypointCount
        {
            get { EnsureReady(); return _waypoints != null ? _waypoints.Length : 0; }
        }

        public bool Sample(float distance, out Vector3 position, out Quaternion rotation)
        {
            EnsureReady();

            if (!_ready)
            {
                position = transform.position;
                rotation = transform.rotation;
                return false;
            }

            float d = distance - spawnLeadDistance;

            Vector3 startDir = (_waypoints[1].position - _waypoints[0].position).normalized;
            Vector3 endDir   = (_waypoints[_waypoints.Length - 1].position
                               - _waypoints[_waypoints.Length - 2].position).normalized;

            if (d < 0f)
            {
                position = _waypoints[0].position - startDir * (-d);
                rotation = Quaternion.LookRotation(startDir);
                return true;
            }

            if (d >= _pathLength)
            {
                float overrun = d - _pathLength;
                position = _waypoints[_waypoints.Length - 1].position + endDir * overrun;
                rotation = Quaternion.LookRotation(endDir);
                return true;
            }

            float remaining = d;
            for (int i = 0; i < _segLengths.Length; i++)
            {
                if (remaining <= _segLengths[i])
                {
                    float   t   = (_segLengths[i] < 0.0001f) ? 0f : remaining / _segLengths[i];
                    Vector3 dir = (_waypoints[i + 1].position - _waypoints[i].position).normalized;
                    position = Vector3.LerpUnclamped(_waypoints[i].position, _waypoints[i + 1].position, t);
                    rotation = Quaternion.LookRotation(dir);
                    return true;
                }
                remaining -= _segLengths[i];
            }

            position = _waypoints[_waypoints.Length - 1].position;
            rotation = Quaternion.LookRotation(endDir);
            return true;
        }

        // ---------------------------------------------------------------
        // Unity messages
        // ---------------------------------------------------------------

        private void Awake() { Build(); }

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

            var list = new List<Transform>();
            foreach (Transform child in transform)
                list.Add(child);

            if (list.Count < 2)
            {
                Debug.LogWarning(
                    "[BirdFlock] BirdPath on '" + gameObject.name +
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

            _ready = true;
        }

        // ---------------------------------------------------------------
        // Gizmos
        // ---------------------------------------------------------------

        private void OnDrawGizmos()
        {
            var children = new List<Transform>();
            foreach (Transform child in transform)
                children.Add(child);

            if (children.Count == 0) return;

            Gizmos.color = pathColor;

            for (int i = 0; i < children.Count; i++)
            {
                Gizmos.DrawSphere(children[i].position, nodeRadius);
                if (i < children.Count - 1)
                    Gizmos.DrawLine(children[i].position, children[i + 1].position);
            }

            if (children.Count < 2) return;

            // Spawn lead (green)
            Vector3 sDir     = (children[1].position - children[0].position).normalized;
            Vector3 spawnPt  = children[0].position - sDir * spawnLeadDistance;
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(spawnPt, nodeRadius * 0.6f);
            Gizmos.DrawLine(spawnPt, children[0].position);

            // Despawn lead (red)
            int     n        = children.Count;
            Vector3 eDir     = (children[n - 1].position - children[n - 2].position).normalized;
            Vector3 despawnPt = children[n - 1].position + eDir * despawnLeadDistance;
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(despawnPt, nodeRadius * 0.6f);
            Gizmos.DrawLine(children[n - 1].position, despawnPt);

            // Direction arrow at midpoint
            int mid = children.Count / 2;
            if (mid < children.Count - 1)
            {
                Vector3 arrowDir = (children[mid + 1].position - children[mid].position).normalized;
                DrawGizmoArrow(children[mid].position, arrowDir, 3.0f);
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
