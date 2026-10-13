// BakedFlockTag.cs
// Lightweight marker placed on baked-flock container GameObjects.
// Used by BirdBaker (Editor) to locate and clear baked results.
//
// The container MUST remain at world origin (0,0,0 / identity / scale 1)
// because the animation clips encode world-space positions as localPosition
// values relative to this parent.  This component enforces that constraint.

using UnityEngine;

namespace BirdFlock
{
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
#endif
    public class BakedFlockTag : MonoBehaviour
    {
        /// <summary>
        /// The spawner this container was baked from.
        /// BirdBaker uses this reference to re-enable the spawner on "Clear".
        /// </summary>
        public BirdSpawner sourceSpawner;

#if UNITY_EDITOR
        private void Update()
        {
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
                    "[BirdFlock] BakedFlockTag: '" + gameObject.name +
                    "' must stay at world origin (0,0,0) – transform has been reset.", this);
        }
    }
}
