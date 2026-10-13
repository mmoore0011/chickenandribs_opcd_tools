// BakedFormationTag.cs
// Lightweight marker placed on a baked formation's root GameObject (both the
// scene instance and the saved prefab asset carry it). Used by
// TriggerFormationBaker to find and clear baked results and re-point the
// source spawner for re-baking.
//
// Unlike BakedVehicleTag / BakedFlockTag, this does NOT need to enforce a
// world-origin transform: the baked movement clip stores positions/rotations
// RELATIVE to the formation root's own transform (see TriggerFormationBaker),
// specifically so the result is a normal, freely-placeable prefab rather than
// something pinned to world origin.

using UnityEngine;

namespace TriggerFormations
{
    public class BakedFormationTag : MonoBehaviour
    {
        /// <summary>
        /// The spawner this formation was baked from. Used by
        /// TriggerFormationBaker's "Clear Baked Formation" to remove the
        /// scene instance and leave the source spawner ready to re-bake.
        /// </summary>
        public TriggerFormationSpawner sourceSpawner;
    }
}
