// FormationBuilder.cs
// Local-space offset math for arranging N formation members into a
// rectangular block (ranks), analogous to BirdFlock's
// BirdBuilder.GetFormationOffsets but for a marching/charging group rather
// than a V-formation.
//
// Formation forward = +Z (matches the path-forward convention used by
// MovingVehicles.VehiclePath / VehicleBuilder). Row 0 is the front rank,
// closest to the direction of travel — callers that need a "front N members"
// subset (e.g. which ones get a full Animator vs. a static combined mesh)
// can just take the first N entries of the returned array.

using UnityEngine;

namespace TriggerFormations
{
    public static class FormationBuilder
    {
        /// <summary>
        /// Returns 'count' local-space offsets arranged as a centered
        /// rectangular block of ranks. 'spacing' is the distance in meters
        /// between adjacent slots, both across a rank and between ranks.
        /// </summary>
        public static Vector3[] GetGridOffsets(int count, float spacing)
        {
            count = Mathf.Max(1, count);
            spacing = Mathf.Max(0.01f, spacing);

            int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
            Vector3[] offsets = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                int row = i / cols;
                int col = i % cols;

                float x = (col - (cols - 1) * 0.5f) * spacing;
                float z = -row * spacing;

                offsets[i] = new Vector3(x, 0f, z);
            }

            return offsets;
        }
    }
}
