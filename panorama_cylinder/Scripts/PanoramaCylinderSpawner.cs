// PanoramaCylinderSpawner.cs
// Place this on an empty GameObject in the scene (or use
// Tools > chickenandribs > Panorama Cylinder > Create Spawner), assign your
// tile images in order, then adjust the fields below — the segment meshes
// rebuild LIVE in the Scene view as you edit them, including dragging
// Overlap Degrees, so you can visually dial in the seam blend instead of
// guessing and re-baking each time.
//
// This is a design-time AUTHORING component, same idea as
// TriggerFormations.TriggerFormationSpawner — its child segment objects are
// editor-only preview geometry (HideFlags.DontSave, regenerated on load),
// never saved with the scene and never doing anything in Play mode or a
// build. Once you're happy with the result, run
// Tools > chickenandribs > Panorama Cylinder > Bake Selected to save it as
// permanent mesh/material assets and a prefab.

using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PanoramaCylinder
{
    public class PanoramaCylinderSpawner : MonoBehaviour
    {
        [Tooltip("Panorama tiles, in stitching order around the ring. The " +
                 "number of tiles here IS the segment count — 360° divided by " +
                 "however many you add.")]
        public List<Texture2D> tiles = new List<Texture2D>();

        [Header("Geometry")]
        public float radius = 500f;
        public float height = 300f;

        [Tooltip("Vertical position of the panel's bottom edge, relative to " +
                 "this object's own position. Slightly negative helps hide " +
                 "any gap if the ground underneath isn't perfectly flat.")]
        public float bottomY = -20f;

        [Range(1, 128)]
        [Tooltip("Horizontal facets per tile. Higher gives a smoother curve. " +
                 "With only 1 tile, this is the facet count for the ENTIRE " +
                 "360° ring, not just one slice — a low value here can leave " +
                 "the mesh's top edge faceted enough to show as a faint " +
                 "visible line against the sky (an anti-aliasing artifact on " +
                 "the otherwise-transparent silhouette), so a single-tile " +
                 "setup usually needs a much higher value than an 8-tile one.")]
        public int subdivisionsPerTile = 8;

        [Range(0f, 360f)]
        [Tooltip("Rotates the whole ring so tile 0 begins wherever you need it.")]
        public float startAngleDegrees = 0f;

        [Header("Seam Blend")]
        [Range(0f, 20f)]
        [Tooltip("How many degrees each tile's mesh extends into its " +
                 "neighbor, fading out over that span, so a seam that doesn't " +
                 "quite line up blends instead of showing a hard edge. 0 = no " +
                 "blend (a plain hard edge at the exact tile boundary).")]
        public float overlapDegrees = 0f;

#if UNITY_EDITOR
        private bool _rebuildQueued;

        private void OnEnable()  { QueueRebuild(); }
        private void OnValidate() { QueueRebuild(); }

        // Editor guidance is to avoid creating/destroying objects directly
        // inside OnValidate (reentrancy risk during serialization) — defer
        // one tick via delayCall instead. Still feels instant to drag a slider.
        private void QueueRebuild()
        {
            if (_rebuildQueued) return;
            _rebuildQueued = true;
            EditorApplication.delayCall += DeferredRebuild;
        }

        private void DeferredRebuild()
        {
            _rebuildQueued = false;
            if (this == null) return; // destroyed before this ran
            RebuildPreview();
        }

        // Rebuilds every child segment from scratch each time — simplest
        // reliable way to stay in sync with the tile list (additions,
        // removals, reordering), and cheap enough (a handful of small
        // meshes) to redo on every slider tick without any hitching.
        private void RebuildPreview()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);

            List<Texture2D> valid = tiles.FindAll(t => t != null);
            if (valid.Count == 0) return;

            float topY = bottomY + height;

            for (int i = 0; i < valid.Count; i++)
            {
                Mesh mesh = PanoramaCylinderBuilder.BuildSegmentMesh(
                    i, valid.Count, startAngleDegrees, radius, bottomY, topY,
                    subdivisionsPerTile, overlapDegrees);
                mesh.hideFlags = HideFlags.DontSave;

                Material mat = PanoramaCylinderBuilder.CreateSegmentMaterial(valid[i], i);
                if (mat != null) mat.hideFlags = HideFlags.DontSave;

                var segGo = new GameObject("Segment_" + i.ToString("00"));
                segGo.hideFlags = HideFlags.DontSave;
                segGo.transform.SetParent(transform, false);

                var mf = segGo.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;

                var mr = segGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial    = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows    = false;
            }
        }
#endif
    }
}
