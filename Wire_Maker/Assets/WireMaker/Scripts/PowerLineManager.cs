using System.Collections.Generic;
using UnityEngine;

namespace WireMaker
{
    /// <summary>
    /// Scene component that generates sagging power-line geometry between a set of
    /// pole attachment points.  Add this to any GameObject, populate the Attachment
    /// Points array with the top-of-pole transforms, then click Build Wires in the
    /// Inspector.  The generated cylinders live as children of this GameObject and
    /// are saved with the scene — no runtime code runs in the built game.
    /// </summary>
    public class PowerLineManager : MonoBehaviour
    {
        [Header("Attachment Points")]
        [Tooltip("Top-of-pole Transforms in span order. One wire is created per consecutive pair.")]
        public Transform[] attachmentPoints;

        [Header("Wire Settings")]
        [Tooltip("Maximum downward sag at the midpoint of each span, in meters.")]
        public float sagAmount = 0.5f;

        [Tooltip("Number of cylinder segments used to approximate the curve per span.  More = smoother.")]
        [Range(4, 32)]
        public int segmentsPerSpan = 8;

        [Tooltip("Visual radius of each wire cylinder, in meters.")]
        public float wireRadius = 0.03f;

        [Tooltip("Material applied to all wire cylinders.  A black Standard material is auto-created if left empty.")]
        public Material wireMaterial;

        // Tracks every generated cylinder so ClearWires / BuildWires can clean up reliably.
        [HideInInspector]
        public List<GameObject> generatedWires = new List<GameObject>();

        // ------------------------------------------------------------------
        // Public API — called by PowerLineManagerEditor buttons
        // ------------------------------------------------------------------

        public void BuildWires()
        {
            ClearWires();

            if (attachmentPoints == null || attachmentPoints.Length < 2)
                return;

            if (wireMaterial == null)
                wireMaterial = MakeBlackMaterial();

            var container = new GameObject("__WireSegments");
            container.transform.SetParent(transform, false);

            for (int i = 0; i < attachmentPoints.Length - 1; i++)
            {
                if (attachmentPoints[i] == null || attachmentPoints[i + 1] == null)
                    continue;

                BuildSpan(attachmentPoints[i].position, attachmentPoints[i + 1].position,
                          container.transform, i);
            }
        }

        public void ClearWires()
        {
            for (int i = generatedWires.Count - 1; i >= 0; i--)
            {
                if (generatedWires[i] != null)
                    SafeDestroy(generatedWires[i]);
            }
            generatedWires.Clear();

            Transform container = transform.Find("__WireSegments");
            if (container != null)
                SafeDestroy(container.gameObject);
        }

        // Returns a world-space point on the parabolic wire curve between a and b.
        // t == 0 is point a, t == 1 is point b.  Sag is always downward.
        public Vector3 SampleWirePoint(Vector3 a, Vector3 b, float t)
        {
            Vector3 p = Vector3.LerpUnclamped(a, b, t);
            p.y -= sagAmount * 4f * t * (1f - t);
            return p;
        }

        // ------------------------------------------------------------------
        // Internal helpers
        // ------------------------------------------------------------------

        private void BuildSpan(Vector3 a, Vector3 b, Transform container, int spanIndex)
        {
            for (int s = 0; s < segmentsPerSpan; s++)
            {
                float t0 = (float)s / segmentsPerSpan;
                float t1 = (float)(s + 1) / segmentsPerSpan;

                Vector3 p0 = SampleWirePoint(a, b, t0);
                Vector3 p1 = SampleWirePoint(a, b, t1);

                GameObject seg = MakeCylinder(p0, p1);
                seg.name = "Wire_" + spanIndex + "_" + s;
                seg.transform.SetParent(container, true);
                generatedWires.Add(seg);
            }
        }

        private GameObject MakeCylinder(Vector3 p0, Vector3 p1)
        {
            Vector3 dir    = p1 - p0;
            float   length = Mathf.Max(dir.magnitude, 0.0001f);

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);

            // Collider is unnecessary for a static wire decoration.
            SafeDestroy(go.GetComponent<Collider>());

            go.GetComponent<MeshRenderer>().sharedMaterial = wireMaterial;

            // Unity's default cylinder is 2 units tall on the Y axis with a 0.5 unit radius.
            // Scale X/Z to reach the desired wire radius; scale Y to span the segment length.
            go.transform.position   = (p0 + p1) * 0.5f;
            go.transform.localScale = new Vector3(wireRadius * 2f, length * 0.5f, wireRadius * 2f);
            go.transform.rotation   = Quaternion.FromToRotation(Vector3.up, dir.normalized);

            return go;
        }

        private static Material MakeBlackMaterial()
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.color = Color.black;
            return mat;
        }

        private static void SafeDestroy(Object obj)
        {
            if (obj == null) return;
#if UNITY_EDITOR
            if (!UnityEditor.EditorApplication.isPlaying)
            {
                DestroyImmediate(obj);
                return;
            }
#endif
            Destroy(obj);
        }

        // ------------------------------------------------------------------
        // Gizmos — always-visible catenary preview in the Scene view
        // ------------------------------------------------------------------

        private void OnDrawGizmos()
        {
            if (attachmentPoints == null || attachmentPoints.Length < 2)
                return;

            bool builtAlready = generatedWires != null && generatedWires.Count > 0;

            for (int i = 0; i < attachmentPoints.Length - 1; i++)
            {
                if (attachmentPoints[i] == null || attachmentPoints[i + 1] == null)
                    continue;

                Vector3 a = attachmentPoints[i].position;
                Vector3 b = attachmentPoints[i + 1].position;

                // Yellow guide line when no mesh exists; dim grey overlay when built.
                Gizmos.color = builtAlready
                    ? new Color(0.25f, 0.25f, 0.25f, 0.35f)
                    : new Color(0.95f, 0.85f, 0f, 0.9f);

                int previewSegs = Mathf.Max(segmentsPerSpan, 16);
                for (int s = 0; s < previewSegs; s++)
                {
                    Gizmos.DrawLine(
                        SampleWirePoint(a, b, (float)s / previewSegs),
                        SampleWirePoint(a, b, (float)(s + 1) / previewSegs));
                }
            }

            // Yellow spheres mark each attachment point.
            Gizmos.color = Color.yellow;
            foreach (Transform pt in attachmentPoints)
            {
                if (pt != null)
                    Gizmos.DrawSphere(pt.position, 0.08f);
            }
        }
    }
}
