// BakedVehicleTag.cs
// Lightweight marker placed on baked-vehicle container GameObjects.
// Used by VehicleBaker (Editor) to locate and clear baked results.
//
// The container holding baked vehicles MUST remain at world origin
// (position 0,0,0 / rotation identity / scale 1,1,1) because
// the animation clips encode world-space positions as localPosition
// values.  This component enforces that constraint in the editor.

using UnityEngine;

namespace MovingVehicles
{
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
#endif
    public class BakedVehicleTag : MonoBehaviour
    {
        /// <summary>
        /// The spawner this container was baked from.
        /// VehicleBaker uses this reference to re-enable the spawner on "Clear".
        /// </summary>
        public VehicleSpawner sourceSpawner;

        // ---------------------------------------------------------------
        // Lock the container transform so accidental moves don't break
        // the animation clips (which encode world-space coordinates as
        // local-space values relative to this parent).
        // ---------------------------------------------------------------
#if UNITY_EDITOR
        private void Update()
        {
            // Only run in edit mode (not during play)
            if (!Application.isPlaying)
                EnforceOrigin();
        }
#endif

        private void EnforceOrigin()
        {
            bool dirty = false;

            if (transform.position != Vector3.zero)
            {
                transform.position  = Vector3.zero;
                dirty = true;
            }
            if (transform.rotation != Quaternion.identity)
            {
                transform.rotation  = Quaternion.identity;
                dirty = true;
            }
            if (transform.localScale != Vector3.one)
            {
                transform.localScale = Vector3.one;
                dirty = true;
            }

            if (dirty)
                Debug.LogWarning(
                    "[MovingVehicles] BakedVehicleTag: '" + gameObject.name +
                    "' must stay at world origin (0,0,0) – transform has been reset.", this);
        }
    }
}
