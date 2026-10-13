// TriggerEffectSpawner.cs
// Place on a GameObject at the spot an effect should fire from, assign
// effectPrefab, tune the fields below, then:
//   Tools > chickenandribs > Trigger Effects > Bake Selected Trigger Effect Spawner
//
// Pure data holder — no Awake/Start/Update, nothing that needs to run at
// GSPro's actual runtime. Baking reads these fields and builds the
// script-free result (Animator + AnimationClip + colliders/rigidbodies)
// under this same GameObject. Safe to leave in a shipped scene even though
// GSPro doesn't know this component's type: its job is done once baking has
// run, same reasoning that already lets TriggerFormations' BakedFormationTag
// linger harmlessly as an inert "missing script" placeholder.
//
// Re-tuning: change any field and re-run Bake — it rebuilds the invisible
// Effect/Shockwave children in place without touching an already-placed
// Trigger child, so the collider you sized to the tee/blast spot stays put.

using UnityEngine;

namespace TriggerEffects
{
    public class TriggerEffectSpawner : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("The prefab to wrap and trigger — e.g. Explosion.prefab.")]
        public GameObject effectPrefab;

        [Header("Shockwave")]
        [Tooltip("Adds an invisible ring of colliders that physically push nearby " +
                 "Rigidbodies outward. Use this instead of relying on a wrapped " +
                 "prefab's own force-application script (e.g. Standard Assets' " +
                 "ExplosionPhysicsForce) — that specifically doesn't take effect " +
                 "in GSPro, even though direct collider-vs-collider pushes do.")]
        public bool addShockwave = true;

        [Tooltip("How far the shockwave reaches (\"size\").")]
        public float shockwaveRadius = 10f;

        [Tooltip("How long the pushers take to travel out to shockwaveRadius. " +
                 "Shorter = faster = a harder-feeling push (\"force\").")]
        public float shockwaveDuration = 0.4f;

        [Tooltip("How many pushers form the ring. More closes gaps a target could slip through.")]
        public int pusherCount = 16;

        [Tooltip("Collider radius of each individual pusher.")]
        public float pusherRadius = 1.5f;

        [Tooltip("Upward tilt of the push direction, so it lifts things rather than only shoving sideways.")]
        public float pusherPitchDegrees = 20f;

        [Tooltip("Starting distance of each pusher from center before it moves outward.")]
        public float pusherStartRadius = 0.5f;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, shockwaveRadius);
        }
    }
}
