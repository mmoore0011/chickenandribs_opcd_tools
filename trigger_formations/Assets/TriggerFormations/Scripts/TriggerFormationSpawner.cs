// TriggerFormationSpawner.cs
// Place this on an empty GameObject in the scene and configure it, then run
// Tools > chickenandribs > Trigger Formations > Bake Selected.
//
// This is a design-time CONFIGURATION component only — unlike
// MovingVehicles.VehicleSpawner / BirdFlock.BirdSpawner it has no Update()
// and does not run a live spawning simulation in Play mode. The only thing
// that matters is the baked prefab TriggerFormationBaker produces from these
// settings: a GameObject with an Animator (Idle/Move, gated by a bool
// parameter) suitable for GSPro's TriggerScript.MyObjectController.
//
// QUICK START
// -----------
// 1. Build (or reuse) a MovingVehicles.VehiclePath for the route.
// 2. Create an empty GameObject, add TriggerFormationSpawner, assign Path
//    and Character Prefab.
// 3. Tools > chickenandribs > Trigger Formations > Bake Selected.
// 4. Assign the baked prefab's Animator to a GSPro TriggerScript's
//    "My Object Controller" field, and set "Bool Condition Name" to match
//    Bool Parameter Name below.

using UnityEngine;

namespace TriggerFormations
{
    public class TriggerFormationSpawner : MonoBehaviour
    {
        public enum Arrangement { SingleObject, GroupedObjects }

        [Header("Path")]
        [Tooltip("The route the whole formation moves along while triggered.")]
        public MovingVehicles.VehiclePath path;

        [Header("Character")]
        [Tooltip("Source prefab. Its own Animator/animation (e.g. a running " +
                 "cycle from blender_animation_creator) is left untouched — " +
                 "only the group's shared movement is baked separately.")]
        public GameObject characterPrefab;

        [Header("Arrangement")]
        [Tooltip("Single Object: one instance follows the path.\n" +
                 "Grouped Objects: up to 200 instances in a formation block " +
                 "follow the path together.")]
        public Arrangement arrangement = Arrangement.SingleObject;

        [Tooltip("Total formation size. Only used when Arrangement is Grouped Objects.")]
        [Range(1, 200)]
        public int count = 30;

        [Tooltip("How many of the front-most members get a full, individually " +
                 "animated instance. The rest are baked into a single static " +
                 "combined mesh (frozen mid-stride poses) to keep large formations " +
                 "cheap — see the Trigger Formations tool notes for why. Clamped " +
                 "to Count.")]
        [Range(1, 200)]
        public int animatedCount = 12;

        [Tooltip("Distance in meters between adjacent formation slots — the " +
                 "\"how tightly packed\" control.")]
        [Range(0.5f, 5f)]
        public float spacing = 1.2f;

        [Header("Movement")]
        [Tooltip("World units per second while moving.")]
        [Range(1f, 20f)]
        public float speed = 6f;

        [Header("Trigger")]
        [Tooltip("Name of the Animator bool parameter that gates movement. " +
                 "Copy this exact name into the GSPro TriggerScript's " +
                 "\"Bool Condition Name\" field.")]
        public string boolParameterName = "Go";

        [Tooltip("How many full runs of the path the formation plays before " +
                 "permanently stopping (holding its final position) — once " +
                 "reached, it can never be re-triggered again, even if the " +
                 "bool goes true a second time. 0 = loop for as long as the " +
                 "bool stays true (old default, useful for an ambient patrol " +
                 "rather than a one-time triggered event).")]
        [Range(0, 10)]
        public int playCount = 1;

        [Header("Audio")]
        [Tooltip("Optional sound for the whole formation (e.g. marching footsteps, " +
                 "drums, war cries) — one shared AudioSource on the baked Mover, not " +
                 "per-soldier. Leave empty for a silent formation.\n\n" +
                 "Note: like MovingVehicles' engine sound, this plays from the moment " +
                 "the baked formation exists in the scene (Play On Awake), independent " +
                 "of the Idle/Move trigger state — it is NOT gated to only play while " +
                 "actually charging.")]
        public AudioClip audioClip;

        [Tooltip("Loop for as long as the formation exists. Uncheck for a one-shot " +
                 "sound (e.g. a single war cry) that plays once and stops.")]
        public bool loopAudio = true;

        [Tooltip("Volume for the formation's sound (0 = silent, 1 = full).")]
        [Range(0f, 1f)]
        public float audioVolume = 0.4f;

        [Tooltip("Distance (world units) at which the sound fades to silence. " +
                 "Tune to match your scene scale.")]
        public float audioMaxDistance = 50f;

        private void OnValidate()
        {
            if (arrangement == Arrangement.SingleObject)
            {
                count = 1;
                animatedCount = 1;
            }
            else
            {
                animatedCount = Mathf.Clamp(animatedCount, 1, count);
            }

            if (string.IsNullOrEmpty(boolParameterName))
                boolParameterName = "Go";
        }
    }
}
