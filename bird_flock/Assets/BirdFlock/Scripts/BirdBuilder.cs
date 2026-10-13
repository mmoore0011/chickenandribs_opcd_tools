// BirdBuilder.cs
// Procedurally creates low-poly bird GameObjects and arranges them into
// flight formations.  No external assets required.
//
// Bird forward = +Z  (matches Quaternion.LookRotation on the path direction)
// Bird right   = +X
// Bird up      = +Y
// Y = 0 is the flock root's altitude (set by waypoint height).

using System.Collections.Generic;
using UnityEngine;

namespace BirdFlock
{
    public static class BirdBuilder
    {
        // ---------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------

        /// <summary>
        /// Creates a flock root with procedural low-poly bird children in formation.
        /// </summary>
        public static GameObject BuildFlock(FlightPattern pattern, int numBirds)
        {
            var root = new GameObject("BirdFlock");

            switch (pattern)
            {
                case FlightPattern.SingleObject:
                    // No procedural birds — this pattern is only meaningful with a custom prefab.
                    Debug.LogWarning("[BirdFlock] SingleObject pattern requires a custom prefab. " +
                                     "Enable 'Use Built-In Birds' is disabled when using this pattern.");
                    break;

                case FlightPattern.VFormation:
                default:
                    PlaceVFormation(root, Mathf.Max(3, numBirds));
                    break;
            }

            return root;
        }

        /// <summary>
        /// Creates a flock root by instantiating <paramref name="birdPrefab"/> once per bird,
        /// arranged in the same formation as BuildFlock. Use this when supplying a custom bird model.
        /// </summary>
        public static GameObject BuildFlockFromPrefabs(FlightPattern pattern, int numBirds,
                                                       GameObject[] birdPrefabs)
        {
            var root = new GameObject("BirdFlock");
            Vector3[] offsets = GetFormationOffsets(pattern, numBirds);

            for (int i = 0; i < offsets.Length; i++)
            {
                GameObject prefab = birdPrefabs[Random.Range(0, birdPrefabs.Length)];
                GameObject bird = (GameObject)Object.Instantiate(prefab);
                bird.name = "Bird_" + i;
                bird.transform.SetParent(root.transform, false);
                bird.transform.localPosition = offsets[i];

                // Randomize animation start time so wing flaps are out of sync across the flock
                Animator animator = bird.GetComponent<Animator>();
                if (animator != null)
                {
                    animator.Play(0, -1, Random.value);
                }
                else
                {
                    Animation legacyAnim = bird.GetComponent<Animation>();
                    if (legacyAnim != null && legacyAnim.clip != null)
                        legacyAnim[legacyAnim.clip.name].normalizedTime = Random.value;
                }
            }

            return root;
        }

        /// <summary>
        /// Returns an array of local-space offsets for the requested formation,
        /// without creating any GameObjects. Used by BirdBaker for custom-prefab flocks.
        /// </summary>
        public static Vector3[] GetFormationOffsets(FlightPattern pattern, int numBirds)
        {
            switch (pattern)
            {
                case FlightPattern.SingleObject:
                    return new Vector3[] { Vector3.zero };

                case FlightPattern.VFormation:
                default:
                    return CalcVFormationOffsets(Mathf.Max(3, numBirds));
            }
        }

        // ---------------------------------------------------------------
        // Shared mesh cache
        // ---------------------------------------------------------------
        private static Mesh _cubeMesh;
        private static Mesh CubeMesh
        {
            get { return _cubeMesh ?? (_cubeMesh = CreateCubeMesh()); }
        }

        // ---------------------------------------------------------------
        // Shared material cache
        // ---------------------------------------------------------------
        private static Material _birdMat;
        private static Material BirdMat
        {
            get
            {
                if (_birdMat == null)
                {
                    _birdMat = new Material(Shader.Find("Standard"));
                    _birdMat.name = "BF_Bird";
                    _birdMat.color = new Color(0.14f, 0.14f, 0.14f);   // near-black silhouette
                    _birdMat.SetFloat("_Metallic",   0f);
                    _birdMat.SetFloat("_Glossiness", 0.05f);
                }
                return _birdMat;
            }
        }

        // ---------------------------------------------------------------
        // V Formation
        //
        // Layout in flock local space (+Z = direction of travel):
        //
        //               [Leader]
        //         [L2] [L1]     [R1] [R2] [R3]   ← asymmetric wings
        //                  [Straggler]             ← behind the main V
        // ---------------------------------------------------------------
        private static void PlaceVFormation(GameObject flockRoot, int numBirds)
        {
            Vector3[] offsets = CalcVFormationOffsets(numBirds);
            for (int i = 0; i < offsets.Length; i++)
                AddBird(flockRoot, i, offsets[i]);
        }

        // Shared calculation used by PlaceVFormation and GetFormationOffsets.
        private static Vector3[] CalcVFormationOffsets(int numBirds)
        {
            var positions = new List<Vector3>();

            // Reserve 1-2 stragglers (birds that lag behind the V)
            int stragglerCount = (numBirds >= 8) ? Random.Range(1, 3) : 1;
            int formationBirds = Mathf.Max(0, numBirds - 1 - stragglerCount);

            // Asymmetric split: left wing gets 35-52% of formation birds so
            // one side is noticeably longer than the other.
            float leftFraction = Random.Range(0.35f, 0.52f);
            int   leftCount    = Mathf.RoundToInt(formationBirds * leftFraction);
            int   rightCount   = formationBirds - leftCount;

            float spacing   = 2.2f;
            float halfAngle = 32f;
            float sinA = Mathf.Sin(halfAngle * Mathf.Deg2Rad);
            float cosA = Mathf.Cos(halfAngle * Mathf.Deg2Rad);

            // Leader – at the apex of the V
            positions.Add(FlockOffset(0.30f));

            // Left wing – extends back and left (-X, -Z)
            for (int i = 0; i < leftCount; i++)
            {
                float x = -(i + 1) * spacing * sinA;
                float z = -(i + 1) * spacing * cosA;
                positions.Add(new Vector3(x, 0f, z) + FlockOffset(0.45f));
            }

            // Right wing – extends back and right (+X, -Z)
            for (int i = 0; i < rightCount; i++)
            {
                float x = (i + 1) * spacing * sinA;
                float z = (i + 1) * spacing * cosA;
                positions.Add(new Vector3(x, 0f, -z) + FlockOffset(0.45f));
            }

            // Stragglers – behind the deepest wing birds, loosely centered
            int   maxWing  = Mathf.Max(leftCount, rightCount);
            float maxDepth = maxWing * spacing * cosA;
            for (int i = 0; i < stragglerCount; i++)
            {
                float x = Random.Range(-spacing * 0.7f, spacing * 0.7f);
                float z = -(maxDepth + Random.Range(2.0f, 4.5f));
                positions.Add(new Vector3(x, 0f, z) + FlockOffset(0.55f));
            }

            return positions.ToArray();
        }

        // Small random positional jitter so the formation looks natural, not mechanical.
        // The Y range is intentionally smaller so birds stay roughly at the same altitude.
        private static Vector3 FlockOffset(float scale)
        {
            return new Vector3(
                Random.Range(-scale,        scale),
                Random.Range(-scale * 0.4f, scale * 0.4f),
                Random.Range(-scale,        scale));
        }

        // ---------------------------------------------------------------
        // Build one low-poly bird as a child of flockRoot
        // ---------------------------------------------------------------
        private static void AddBird(GameObject flockRoot, int index, Vector3 localPos)
        {
            var birdRoot = new GameObject("Bird_" + index);
            birdRoot.transform.SetParent(flockRoot.transform, false);
            birdRoot.transform.localPosition = localPos;

            var mat = BirdMat;

            // Body – slender elongated box along +Z
            AddBox(birdRoot, "Body",
                new Vector3(0.13f, 0.10f, 0.46f),
                Vector3.zero, Quaternion.identity, mat);

            // Left wing – flat, tilted upward ~10° (classic soaring bird dihedral)
            AddBox(birdRoot, "Wing_L",
                new Vector3(0.52f, 0.04f, 0.28f),
                new Vector3(-0.32f, 0.02f, 0f),
                Quaternion.Euler(0f, 0f, 10f), mat);

            // Right wing – mirror of left
            AddBox(birdRoot, "Wing_R",
                new Vector3(0.52f, 0.04f, 0.28f),
                new Vector3( 0.32f, 0.02f, 0f),
                Quaternion.Euler(0f, 0f, -10f), mat);

            // Head – small box at the front
            AddBox(birdRoot, "Head",
                new Vector3(0.10f, 0.10f, 0.14f),
                new Vector3(0f, 0.02f, 0.26f),
                Quaternion.identity, mat);

            // Tail – thin wedge suggestion at the rear
            AddBox(birdRoot, "Tail",
                new Vector3(0.10f, 0.03f, 0.18f),
                new Vector3(0f, -0.02f, -0.28f),
                Quaternion.identity, mat);
        }

        // ---------------------------------------------------------------
        // Helper: add a scaled, rotated cube child
        // ---------------------------------------------------------------
        private static void AddBox(GameObject root, string childName,
                                   Vector3 size, Vector3 localPos,
                                   Quaternion localRot, Material mat)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale    = size;

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh     = CubeMesh;
            mr.sharedMaterial = mat;
        }

        // ---------------------------------------------------------------
        // Procedural flat-normal cube (identical logic to VehicleBuilder)
        // ---------------------------------------------------------------
        private static Mesh CreateCubeMesh()
        {
            var verts = new Vector3[24];
            var norms = new Vector3[24];
            var tris  = new int[36];

            SetFace(verts, norms, tris, 0, Vector3.forward,
                new Vector3(-0.5f,-0.5f, 0.5f), new Vector3( 0.5f,-0.5f, 0.5f),
                new Vector3( 0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f));
            SetFace(verts, norms, tris, 1, Vector3.back,
                new Vector3( 0.5f,-0.5f,-0.5f), new Vector3(-0.5f,-0.5f,-0.5f),
                new Vector3(-0.5f, 0.5f,-0.5f), new Vector3( 0.5f, 0.5f,-0.5f));
            SetFace(verts, norms, tris, 2, Vector3.left,
                new Vector3(-0.5f,-0.5f,-0.5f), new Vector3(-0.5f,-0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f,-0.5f));
            SetFace(verts, norms, tris, 3, Vector3.right,
                new Vector3( 0.5f,-0.5f, 0.5f), new Vector3( 0.5f,-0.5f,-0.5f),
                new Vector3( 0.5f, 0.5f,-0.5f), new Vector3( 0.5f, 0.5f, 0.5f));
            SetFace(verts, norms, tris, 4, Vector3.up,
                new Vector3(-0.5f, 0.5f, 0.5f), new Vector3( 0.5f, 0.5f, 0.5f),
                new Vector3( 0.5f, 0.5f,-0.5f), new Vector3(-0.5f, 0.5f,-0.5f));
            SetFace(verts, norms, tris, 5, Vector3.down,
                new Vector3(-0.5f,-0.5f,-0.5f), new Vector3( 0.5f,-0.5f,-0.5f),
                new Vector3( 0.5f,-0.5f, 0.5f), new Vector3(-0.5f,-0.5f, 0.5f));

            var mesh = new Mesh();
            mesh.name      = "BF_Cube";
            mesh.vertices  = verts;
            mesh.normals   = norms;
            mesh.triangles = tris;
            return mesh;
        }

        private static void SetFace(
            Vector3[] verts, Vector3[] norms, int[] tris,
            int face, Vector3 normal,
            Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3)
        {
            int vi = face * 4;
            verts[vi] = v0; verts[vi+1] = v1; verts[vi+2] = v2; verts[vi+3] = v3;
            norms[vi] = norms[vi+1] = norms[vi+2] = norms[vi+3] = normal;

            int ti = face * 6;
            tris[ti]   = vi;   tris[ti+1] = vi+1; tris[ti+2] = vi+2;
            tris[ti+3] = vi;   tris[ti+4] = vi+2; tris[ti+5] = vi+3;
        }
    }
}
