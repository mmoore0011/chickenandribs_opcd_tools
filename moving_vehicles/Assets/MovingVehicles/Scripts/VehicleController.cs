// VehicleController.cs
// Attached at runtime by VehicleSpawner to each vehicle instance.
// Moves the vehicle along a VehiclePath at a constant speed and notifies
// the spawner when the despawn point is reached.

using UnityEngine;

namespace MovingVehicles
{
    [DisallowMultipleComponent]
    public class VehicleController : MonoBehaviour
    {
        // ---------------------------------------------------------------
        // State (set by VehicleSpawner.SpawnVehicle)
        // ---------------------------------------------------------------
        private VehiclePath    _path;
        private float          _speed;            // world units per second
        private VehicleSpawner _spawner;
        private float          _distanceTraveled;
        private bool           _finished;

        // Stop-at-waypoint state
        private float[] _wpTravelDists;   // travel-space position of each waypoint
        private float[] _wpStopDurs;      // parallel stop durations
        private int     _nextWpIdx;
        private float   _stopTimer;
        private bool    _stopping;

        // ---------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------

        /// <summary>
        /// Called immediately after the component is added.
        /// </summary>
        public void Initialize(VehiclePath path, float speed, VehicleSpawner spawner)
        {
            _path             = path;
            _speed            = Mathf.Max(0.1f, speed);
            _spawner          = spawner;
            _distanceTraveled = 0f;
            _finished         = false;
            _stopping         = false;
            _stopTimer        = 0f;
            _nextWpIdx        = 0;

            // Convert path-local waypoint distances to travel-space
            float   lead   = path.spawnLeadDistance;
            float[] wdists = path.WaypointDists;
            _wpTravelDists = new float[wdists.Length];
            for (int i = 0; i < wdists.Length; i++)
                _wpTravelDists[i] = lead + wdists[i];
            _wpStopDurs = path.StopDurations;
        }

        // ---------------------------------------------------------------
        // Unity messages
        // ---------------------------------------------------------------

        private void Update()
        {
            if (_path == null || _finished) return;

            if (_stopping)
            {
                _stopTimer -= Time.deltaTime;
                if (_stopTimer <= 0f)
                    _stopping = false;
                return;
            }

            _distanceTraveled += _speed * Time.deltaTime;

            // Check for stop waypoints crossed this frame
            while (_nextWpIdx < _wpTravelDists.Length)
            {
                if (_distanceTraveled < _wpTravelDists[_nextWpIdx]) break;

                float dur = _wpStopDurs[_nextWpIdx];
                _nextWpIdx++;

                if (dur > 0f)
                {
                    // Snap to exact waypoint so vehicle doesn't overshoot
                    _distanceTraveled = _wpTravelDists[_nextWpIdx - 1];
                    _stopping  = true;
                    _stopTimer = dur;
                    Vector3    pos; Quaternion rot;
                    _path.Sample(_distanceTraveled, out pos, out rot);
                    transform.position = pos;
                    transform.rotation = rot;
                    return;
                }
            }

            Vector3    p;
            Quaternion r;

            if (_path.Sample(_distanceTraveled, out p, out r))
            {
                transform.position = p;
                transform.rotation = r;
            }

            if (_distanceTraveled >= _path.TotalTravelDistance)
            {
                _finished = true;

                if (_spawner != null)
                    _spawner.OnVehicleReachedEnd(this);
                else
                    Destroy(gameObject);
            }
        }
    }
}
