// PanoramaCylinderBaker.cs — Editor only
// Turns a scene PanoramaCylinderSpawner's current live-preview settings into
// permanent mesh/material assets and a prefab — the spawner's own child
// segments are editor-only preview geometry (HideFlags.DontSave) and are
// never what actually gets used in a build.
//
// Uses the exact same PanoramaCylinderBuilder methods as the spawner's live
// preview, so what you baked always matches what you saw while tuning it.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PanoramaCylinder
{
    public static class PanoramaCylinderBaker
    {
        private const string RootFolder = "Assets/PanoramaCylinder";

        [MenuItem("Tools/chickenandribs/Panorama Cylinder/Create Spawner", false, 210)]
        public static void CreateSpawner()
        {
            var go = new GameObject("Panorama Cylinder");
            go.AddComponent<PanoramaCylinderSpawner>();
            Undo.RegisterCreatedObjectUndo(go, "Create Panorama Cylinder Spawner");
            Selection.activeGameObject = go;
        }

        [MenuItem("Tools/chickenandribs/Panorama Cylinder/Bake Selected", false, 211)]
        public static void BakeSelected()
        {
            PanoramaCylinderSpawner spawner = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<PanoramaCylinderSpawner>()
                : null;

            if (spawner == null)
            {
                EditorUtility.DisplayDialog("Panorama Cylinder – Bake",
                    "Select a GameObject with a PanoramaCylinderSpawner first.", "OK");
                return;
            }

            List<Texture2D> tiles = spawner.tiles.FindAll(t => t != null);
            if (tiles.Count == 0)
            {
                EditorUtility.DisplayDialog("Panorama Cylinder – Bake",
                    "Assign at least one tile on the spawner first.", "OK");
                return;
            }

            EnsureFolder(RootFolder);
            EnsureFolder(RootFolder + "/Meshes");
            EnsureFolder(RootFolder + "/Materials");
            EnsureFolder(RootFolder + "/Prefabs");

            float topY = spawner.bottomY + spawner.height;

            var root = new GameObject(spawner.name + "_Baked");
            root.transform.position = spawner.transform.position;
            root.transform.rotation = spawner.transform.rotation;

            for (int i = 0; i < tiles.Count; i++)
            {
                Mesh mesh = PanoramaCylinderBuilder.BuildSegmentMesh(
                    i, tiles.Count, spawner.startAngleDegrees, spawner.radius, spawner.bottomY, topY,
                    spawner.subdivisionsPerTile, spawner.overlapDegrees);
                mesh.name = "Segment_" + i.ToString("00");
                string meshPath = RootFolder + "/Meshes/" + mesh.name + ".asset";
                AssetDatabase.DeleteAsset(meshPath);
                AssetDatabase.CreateAsset(mesh, meshPath);

                Material mat = PanoramaCylinderBuilder.CreateSegmentMaterial(tiles[i], i);
                if (mat == null)
                {
                    Object.DestroyImmediate(root);
                    return;
                }
                mat.name = "Segment_" + i.ToString("00") + "_" + tiles[i].name;
                string matPath = RootFolder + "/Materials/" + mat.name + ".mat";
                AssetDatabase.DeleteAsset(matPath);
                AssetDatabase.CreateAsset(mat, matPath);

                var segGo = new GameObject(mesh.name);
                segGo.transform.SetParent(root.transform, false);

                var mf = segGo.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;

                var mr = segGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial    = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows    = false;
            }

            string prefabPath = RootFolder + "/Prefabs/" + spawner.name + ".prefab";
            AssetDatabase.DeleteAsset(prefabPath);
#pragma warning disable 618
            GameObject prefab = PrefabUtility.CreatePrefab(prefabPath, root);
#pragma warning restore 618

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Object.DestroyImmediate(root);

            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            Debug.Log("[PanoramaCylinder] Baked '" + spawner.name + "' → " + prefabPath +
                       "  |  " + tiles.Count + " tile(s)  |  Overlap: " + spawner.overlapDegrees + "°");
        }

        [MenuItem("Tools/chickenandribs/Panorama Cylinder/Bake Selected", true)]
        private static bool BakeValidate()
        {
            return Selection.activeGameObject != null &&
                   Selection.activeGameObject.GetComponent<PanoramaCylinderSpawner>() != null;
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            int    slash  = assetPath.LastIndexOf('/');
            string parent = assetPath.Substring(0, slash);
            string name   = assetPath.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
