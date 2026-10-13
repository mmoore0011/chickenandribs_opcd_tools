// WaypointStop.cs
// Add this component to any child waypoint of a VehiclePath to make vehicles
// pause at that waypoint for stopDuration seconds before continuing.
//
// USAGE
//   1. Select a waypoint GameObject under your VehiclePath.
//   2. Add Component → MovingVehicles → WaypointStop.
//   3. Set Stop Duration (seconds).
//   4. Works in Play mode (runtime) and after baking (GSPro-compatible).
//      After changing stop values, re-bake to update the AnimationClips.

using UnityEngine;

namespace MovingVehicles
{
    public class WaypointStop : MonoBehaviour
    {
        [Tooltip("How long vehicles pause at this waypoint (seconds). 0 = no stop.")]
        [Range(0f, 900f)]
        public float stopDuration = 3f;
    }
}
