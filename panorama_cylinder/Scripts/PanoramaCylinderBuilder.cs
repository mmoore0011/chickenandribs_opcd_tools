// PanoramaCylinderBuilder.cs
// Shared geometry/material building for the Panorama Cylinder tool, used by
// both PanoramaCylinderSpawner (live in-memory preview, rebuilt as you edit
// fields) and PanoramaCylinderBaker (permanent saved assets) so the two
// never drift apart.

using UnityEngine;

namespace PanoramaCylinder
{
    public static class PanoramaCylinderBuilder
    {
        // One curved vertical strip for tile index 'tileIndex' of 'tileCount'
        // total, covering its own [a0,a1) angular slice (360/tileCount wide)
        // PLUS 'overlapDegrees' of extra mesh on each side for the neighbor
        // seam cross-fade.
        //
        // ALPHA IS DELIBERATELY ASYMMETRIC, NOT a symmetric 1→0/0→1 fade on
        // both sides — that was tried first and produced a washed-out,
        // "see-through" overlap instead of a clean blend. The reason: when
        // two independently alpha-blended layers both have partial alpha in
        // the same spot, standard "over" compositing leaves some of
        // whatever's behind BOTH of them showing through — the background
        // only fully disappears where at least one layer is completely
        // opaque. A fade of 0.5/0.5 in the middle leaves 25% of the terrain
        // behind both tiles visible, which is exactly the "ghosted" look.
        //
        // The fix used here: only the LEFT edge fades in (0→1 across the
        // overlap); the CORE and the RIGHT overlap extension both stay fully
        // opaque (alpha 1). Combined with rendering tiles in strictly
        // ascending index order (CreateSegmentMaterial sets an explicit
        // per-tile renderQueue to force this regardless of camera distance),
        // each tile's fading left edge draws ON TOP of the previous tile's
        // fully-opaque right extension — a proper opaque-background crossfade
        // with zero terrain bleed-through.
        //
        // Known limitation: this only works in ascending draw order, so the
        // one WRAPAROUND seam (the last tile's right edge meeting tile 0's
        // left edge) ends up in the wrong order — tile 0 draws first, so the
        // last tile's opaque extension would draw on top and hide tile 0's
        // fade instead of blending with it. That one seam falls back to a
        // hard edge (same as Overlap Degrees = 0) rather than a soft blend;
        // every other seam blends correctly.
        //
        // UV.x runs past this tile's own [0,1] range within the overlap
        // (negative on the left, >1 on the right) — PanoramaCylinderBlend.shader
        // clamps UV via saturate() when sampling, so the texture's own edge
        // pixel just stretches into the overlap instead of wrapping.
        //
        // Same inside-out technique as SkyDomeCreator's sphere: inverted
        // normals + reversed winding so it renders correctly for a camera
        // positioned inside the ring under Unity's default Cull Back.
        public static Mesh BuildSegmentMesh(
            int tileIndex, int tileCount, float startAngleDegrees,
            float radius, float bottomY, float topY,
            int subdivisionsPerTile, float overlapDegrees)
        {
            tileCount = Mathf.Max(1, tileCount);
            float anglePerSegment = 360f / tileCount;
            float a0 = startAngleDegrees + tileIndex * anglePerSegment;
            float a1 = a0 + anglePerSegment;

            overlapDegrees = Mathf.Clamp(overlapDegrees, 0f, anglePerSegment * 0.49f);
            float overlapFraction = overlapDegrees / anglePerSegment;

            float extStart = a0 - overlapDegrees;
            float extEnd   = a1 + overlapDegrees;

            // Scale subdivisions so the wider, overlap-extended mesh keeps
            // roughly the same curve smoothness per degree as the core slice.
            int subdivisions = Mathf.Max(1, Mathf.RoundToInt(
                subdivisionsPerTile * (extEnd - extStart) / anglePerSegment));
            int cols = subdivisions + 1;

            var verts  = new Vector3[cols * 2];
            var norms  = new Vector3[cols * 2];
            var uvs    = new Vector2[cols * 2];
            var colors = new Color[cols * 2];

            for (int i = 0; i <= subdivisions; i++)
            {
                float t        = i / (float)subdivisions;
                float angle    = Mathf.Lerp(extStart, extEnd, t);
                float angleRad = angle * Mathf.Deg2Rad;
                float x        = Mathf.Sin(angleRad) * radius;
                float z        = Mathf.Cos(angleRad) * radius;
                Vector3 dirInward = -(new Vector3(x, 0f, z).normalized);

                // This tile's own U coordinate — can be <0 or >1 inside the
                // overlap, which is exactly what lets the shader's saturate()
                // stretch the edge pixel there.
                float localT = (angle - a0) / anglePerSegment;

                // Only the left edge fades in — the core and the right
                // overlap extension stay fully opaque. See the method
                // comment above for why a symmetric fade on both sides
                // doesn't work.
                float alpha = (localT < 0f && overlapFraction > 0f)
                    ? Mathf.Clamp01(1f + localT / overlapFraction)
                    : 1f;

                int bIdx = i;
                verts[bIdx]  = new Vector3(x, bottomY, z);
                norms[bIdx]  = dirInward;
                uvs[bIdx]    = new Vector2(localT, 0f);
                colors[bIdx] = new Color(1f, 1f, 1f, alpha);

                int tIdx = cols + i;
                verts[tIdx]  = new Vector3(x, topY, z);
                norms[tIdx]  = dirInward;
                uvs[tIdx]    = new Vector2(localT, 1f);
                colors[tIdx] = new Color(1f, 1f, 1f, alpha);
            }

            var tris = new int[subdivisions * 6];
            int ti = 0;
            for (int i = 0; i < subdivisions; i++)
            {
                int bl = i;
                int br = i + 1;
                int tl = cols + i;
                int tr = cols + i + 1;

                tris[ti++] = bl; tris[ti++] = tl; tris[ti++] = br;
                tris[ti++] = br; tris[ti++] = tl; tris[ti++] = tr;
            }

            var mesh = new Mesh();
            mesh.vertices  = verts;
            mesh.normals   = norms;
            mesh.uv        = uvs;
            mesh.colors    = colors;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        // 'tileIndex' is unused now that the shader is alpha-tested (cutout)
        // rather than alpha-blended — draw-order-dependent compositing was
        // only ever needed to make the old blended overlap fade look right
        // (see BuildSegmentMesh's left-edge-fade comment), and a cutout
        // pixel is either fully drawn or fully discarded, so there's nothing
        // for draw order to affect. Kept as a parameter for call-site
        // compatibility and in case blending needs to come back later.
        public static Material CreateSegmentMaterial(Texture2D tex, int tileIndex)
        {
            Shader shader = Shader.Find("Custom/PanoramaCylinderBlend");
            if (shader == null)
            {
                Debug.LogError("[PanoramaCylinder] Custom/PanoramaCylinderBlend shader not found. " +
                                "Make sure PanoramaCylinderBlend.shader is in your project.");
                return null;
            }

            var mat = new Material(shader);
            mat.mainTexture = tex;
            return mat;
        }
    }
}
