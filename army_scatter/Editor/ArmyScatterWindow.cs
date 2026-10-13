// Army Scatter Tool
// Open via: Tools > chickenandribs > Army Scatter
//
// Batch-places a set number of randomly-mixed prefabs inside a defined area,
// spaced apart to avoid overlap/collision, raycast down onto the ground so
// nothing floats or clips, with one shared facing rotation for the whole
// group. Built for scattering a "messy" battlefield crowd (soldiers, etc.)
// without hand-placing each one or having them collide into each other.
//
// This is a one-click batch generator, not a click-drag Scene tool — set the
// area and options, click Place All, review the result. Re-running deletes
// nothing on its own; if a batch looks wrong, select and delete it yourself
// (Undo also works per-instance, same as any other placement).

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

public class ArmyScatterWindow : EditorWindow
{
    private const int MaxAttemptsPerPlacement = 30;

    private List<GameObject> _prefabs = new List<GameObject>();
    private Transform _areaCenter;
    private Vector2 _areaSize = new Vector2(20f, 20f);
    private bool _organizedFormation = false;
    private int _count = 20;
    private float _minSpacing = 2f;
    private float _groupFacingY = 0f;
    private bool _randomizeAnimationOffset = true;
    private float _raycastHeight = 50f;
    private float _maxRaycastDistance = 200f;
    private Transform _parent;

    [MenuItem("Tools/chickenandribs/Army Scatter")]
    public static void Open()
    {
        GetWindow<ArmyScatterWindow>("Army Scatter");
    }

    private void OnEnable()
    {
        SceneView.onSceneGUIDelegate += OnSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.onSceneGUIDelegate -= OnSceneGUI;
    }

    // ── GUI ───────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        GUILayout.Label("Army Scatter", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Assign prefabs, position an Area Center, set a count, and click " +
            "Place All. Candidates are randomly sampled inside the area, " +
            "rejected and retried if too close to something already placed, " +
            "then raycast down to the ground.",
            MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Prefabs", EditorStyles.boldLabel);
        int removeAt = -1;
        for (int i = 0; i < _prefabs.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            _prefabs[i] = (GameObject)EditorGUILayout.ObjectField(_prefabs[i], typeof(GameObject), false);
            if (GUILayout.Button("X", GUILayout.Width(22))) removeAt = i;
            EditorGUILayout.EndHorizontal();
        }
        if (removeAt >= 0) _prefabs.RemoveAt(removeAt);
        if (GUILayout.Button("Add Prefab Slot"))
            _prefabs.Add(null);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Area", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        _areaCenter = (Transform)EditorGUILayout.ObjectField("Area Center", _areaCenter, typeof(Transform), true);
        if (GUILayout.Button("Create", GUILayout.Width(60)))
            _areaCenter = CreateAreaCenter();
        EditorGUILayout.EndHorizontal();
        _areaSize = EditorGUILayout.Vector2Field("Size (X / Z)", _areaSize);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Placement", EditorStyles.boldLabel);
        _organizedFormation = EditorGUILayout.Toggle("Organized Formation (grid)", _organizedFormation);
        if (_organizedFormation)
            EditorGUILayout.HelpBox(
                "Places units in neat rows/columns instead of randomly — for " +
                "standing-at-attention units, not a battle scatter.",
                MessageType.None);
        _count = EditorGUILayout.IntSlider("Count", _count, 1, 100);
        _minSpacing = EditorGUILayout.FloatField(
            _organizedFormation ? "Row/Column Spacing (m)" : "Min Spacing (m)", _minSpacing);
        _groupFacingY = EditorGUILayout.Slider("Facing (Y Rotation)", _groupFacingY, 0f, 360f);
        _randomizeAnimationOffset = EditorGUILayout.Toggle("Randomize Animation Offset", _randomizeAnimationOffset);
        _raycastHeight = EditorGUILayout.FloatField("Raycast Start Height (m)", _raycastHeight);
        _maxRaycastDistance = EditorGUILayout.FloatField("Raycast Max Distance (m)", _maxRaycastDistance);
        _parent = (Transform)EditorGUILayout.ObjectField("Parent (optional)", _parent, typeof(Transform), true);

        EditorGUILayout.Space();
        bool ready = _areaCenter != null && _prefabs.Exists(p => p != null);
        using (new EditorGUI.DisabledScope(!ready))
        {
            if (GUILayout.Button("Place All", GUILayout.Height(32)))
                PlaceAll();
        }

        if (_areaCenter == null)
            EditorGUILayout.HelpBox("Assign or create an Area Center first.", MessageType.Warning);
        else if (!_prefabs.Exists(p => p != null))
            EditorGUILayout.HelpBox("Assign at least one prefab above.", MessageType.Warning);

        SceneView.RepaintAll();
    }

    private static Transform CreateAreaCenter()
    {
        var go = new GameObject("Army Scatter Area");
        Undo.RegisterCreatedObjectUndo(go, "Create Army Scatter Area");
        return go.transform;
    }

    // ── Scene view preview ───────────────────────────────────────────────────
    private void OnSceneGUI(SceneView sceneView)
    {
        if (_areaCenter == null) return;

        Handles.color = new Color(1f, 0.6f, 0f, 0.9f);
        Vector3 center = _areaCenter.position;
        Vector3 size = new Vector3(_areaSize.x, 0.01f, _areaSize.y);
        Handles.matrix = Matrix4x4.TRS(center, _areaCenter.rotation, Vector3.one);
        Handles.DrawWireCube(Vector3.zero, size);
        Handles.matrix = Matrix4x4.identity;
    }

    // ── Placement ─────────────────────────────────────────────────────────────
    private void PlaceAll()
    {
        var candidates = _prefabs.FindAll(p => p != null);
        if (candidates.Count == 0 || _areaCenter == null) return;

        var placedPositions = new List<Vector3>();
        Transform parent = _parent != null ? _parent : GetOrCreateDefaultParent();
        Quaternion facing = Quaternion.Euler(0f, _groupFacingY, 0f);
        // Area Center's own rotation orients the placement region (and, in
        // Organized Formation mode, which way rows/columns run) — separate
        // from Facing above, which is which way each unit itself faces.
        Vector3 right = _areaCenter.right;
        Vector3 forward = _areaCenter.forward;

        int placed = _organizedFormation
            ? PlaceGrid(candidates, parent, facing, right, forward)
            : PlaceScatter(candidates, parent, facing, right, forward, placedPositions);

        if (placed < _count)
        {
            Debug.LogWarning($"[Army Scatter] Only placed {placed}/{_count} — the area may be too small/dense " +
                              $"for the current spacing, or the raycast isn't finding ground everywhere. " +
                              $"Try a larger area, fewer units, or smaller spacing.");
        }
        else
        {
            Debug.Log($"[Army Scatter] Placed {placed} units under \"{parent.name}\".");
        }
    }

    private int PlaceScatter(List<GameObject> candidates, Transform parent, Quaternion facing,
        Vector3 right, Vector3 forward, List<Vector3> placedPositions)
    {
        int placed = 0;
        int totalAttempts = 0;
        int maxTotalAttempts = _count * MaxAttemptsPerPlacement;

        while (placed < _count && totalAttempts < maxTotalAttempts)
        {
            totalAttempts++;

            float x = Random.Range(-_areaSize.x * 0.5f, _areaSize.x * 0.5f);
            float z = Random.Range(-_areaSize.y * 0.5f, _areaSize.y * 0.5f);
            Vector3 samplePoint = _areaCenter.position + right * x + forward * z;
            Vector3 rayStart = new Vector3(samplePoint.x, _areaCenter.position.y + _raycastHeight, samplePoint.z);

            RaycastHit hit;
            if (!Physics.Raycast(rayStart, Vector3.down, out hit, _maxRaycastDistance))
                continue; // no ground under this sample — try another spot

            bool tooClose = false;
            for (int i = 0; i < placedPositions.Count; i++)
            {
                if (Vector3.Distance(placedPositions[i], hit.point) < _minSpacing)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            if (SpawnUnitAt(hit.point, candidates, parent, facing))
            {
                placedPositions.Add(hit.point);
                placed++;
            }
        }

        return placed;
    }

    private int PlaceGrid(List<GameObject> candidates, Transform parent, Quaternion facing,
        Vector3 right, Vector3 forward)
    {
        int columns = Mathf.CeilToInt(Mathf.Sqrt(_count));
        int rows = Mathf.CeilToInt(_count / (float)columns);
        float startX = -(columns - 1) * _minSpacing * 0.5f;
        float startZ = -(rows - 1) * _minSpacing * 0.5f;

        int placed = 0;
        for (int row = 0; row < rows && placed < _count; row++)
        {
            for (int col = 0; col < columns && placed < _count; col++)
            {
                float x = startX + col * _minSpacing;
                float z = startZ + row * _minSpacing;
                Vector3 samplePoint = _areaCenter.position + right * x + forward * z;
                Vector3 rayStart = new Vector3(samplePoint.x, _areaCenter.position.y + _raycastHeight, samplePoint.z);

                RaycastHit hit;
                if (!Physics.Raycast(rayStart, Vector3.down, out hit, _maxRaycastDistance))
                    continue; // no ground under this grid slot — skip it, don't shift the rest of the grid

                if (SpawnUnitAt(hit.point, candidates, parent, facing))
                    placed++;
            }
        }

        return placed;
    }

    private bool SpawnUnitAt(Vector3 position, List<GameObject> candidates, Transform parent, Quaternion facing)
    {
        GameObject prefab = candidates[Random.Range(0, candidates.Count)];
        var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (instance == null) return false;

        Undo.RegisterCreatedObjectUndo(instance, "Place Army Unit");
        instance.transform.SetParent(parent, true);
        instance.transform.position = position;
        instance.transform.rotation = facing;
        if (_randomizeAnimationOffset)
            RandomizeCycleOffsetVariant(instance);
        return true;
    }

    // If the placed instance's Animator Controller has sibling "(offset N)"
    // variants next to it (created by tools like BatchSwordSetup — same clip,
    // different baked Cycle Offset), randomly swap in one of them instead of
    // always using the same one. This is what actually desyncs many units
    // playing the same looping animation — no runtime script involved, since
    // GSPro won't run one; the offset is baked into whichever Controller
    // asset ends up assigned to this instance's Animator component.
    private static void RandomizeCycleOffsetVariant(GameObject instance)
    {
        Animator animator = instance.GetComponentInChildren<Animator>(true);
        if (animator == null || animator.runtimeAnimatorController == null) return;

        string currentPath = AssetDatabase.GetAssetPath(animator.runtimeAnimatorController);
        if (string.IsNullOrEmpty(currentPath)) return;

        string folder = Path.GetDirectoryName(currentPath).Replace("\\", "/");
        string currentName = Path.GetFileNameWithoutExtension(currentPath);

        // Strip a "(offset N)" suffix if the prefab already points at a
        // variant, so we search for siblings of the base animation, not
        // siblings of one specific offset.
        string baseName = currentName;
        int offsetTagIndex = baseName.IndexOf(" (offset ");
        if (offsetTagIndex >= 0)
            baseName = baseName.Substring(0, offsetTagIndex);

        var candidatePaths = new List<string> { $"{folder}/{baseName}.controller" };
        string[] files = Directory.Exists(folder) ? Directory.GetFiles(folder, baseName + " (offset *).controller") : new string[0];
        for (int i = 0; i < files.Length; i++)
            candidatePaths.Add(files[i].Replace("\\", "/"));

        if (candidatePaths.Count <= 1) return; // no variants found — leave the original assigned

        string chosenPath = candidatePaths[Random.Range(0, candidatePaths.Count)];
        var chosenController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(chosenPath);
        if (chosenController != null)
            animator.runtimeAnimatorController = chosenController;
    }

    private static Transform GetOrCreateDefaultParent()
    {
        var go = GameObject.Find("Scattered Army");
        if (go == null)
        {
            go = new GameObject("Scattered Army");
            Undo.RegisterCreatedObjectUndo(go, "Create Scattered Army Parent");
        }
        return go.transform;
    }
}
