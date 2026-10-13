// Rock Scatter Tool
// Open via:  Tools > chickenandribs > Rock Scatter
//
// Assign one or more rock prefabs (e.g. the FTR_snow_rough_rock1-4 models),
// click "Enable Placement", then click — or click-drag — on any collider in
// the Scene view. Each rock is instantiated at the hit point and rotated so
// its local up axis (the direction away from its flat base) aligns to the
// surface normal at that point, not world-up. That's what makes this work
// on a steep cliff face: the rock's flat side ends up flush against the
// slope instead of assuming it's sitting on flat ground.
//
// Click-drag lays down a whole line of rocks at once, spaced by "Drag
// Spacing" apart — meant for covering a lot of cliff wall quickly instead
// of one click per rock.

using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class RockScatterWindow : EditorWindow
{
    // Which local axis of the source prefab points "away from the flat face" —
    // i.e. the axis that should end up aligned to the surface normal. Meshy AI
    // exports don't reliably use +Y for this, so it's exposed instead of assumed:
    // if rocks come out tipped on their side on a cliff, cycle this until one
    // lays flat, rather than guessing blind.
    private enum FlatFaceAxis { PosY, NegY, PosX, NegX, PosZ, NegZ }

    // Random: a new random twist per rock. Fixed: every rock gets the same
    // Spin Angle, which stays editable between placements so you can plant a
    // run of rocks at one angle, nudge the slider, then keep going. Off: no
    // twist at all beyond the flat-face alignment itself.
    private enum SpinMode { Random, Fixed, Off }

    private List<GameObject> _prefabs = new List<GameObject>();
    private Transform _parent;
    private FlatFaceAxis _flatFaceAxis = FlatFaceAxis.PosY;
    private float _minScale    = 0.85f;
    private float _maxScale    = 1.15f;
    private float _spacing     = 1.5f;
    private float _embedDepth  = 0.05f;
    private SpinMode _spinMode = SpinMode.Random;
    private float _fixedSpinDegrees = 0f;
    private bool  _active      = false;

    private Vector3? _lastPlacedPos;

    [MenuItem("Tools/chickenandribs/Rock Scatter")]
    public static void Open()
    {
        GetWindow<RockScatterWindow>("Rock Scatter");
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
        GUILayout.Label("Rock Scatter", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Assign rock prefabs below, enable placement, then click (or " +
            "click-drag) on any collider in the Scene view. Rocks orient " +
            "to the surface normal at the hit point, so this works on " +
            "cliff faces as well as flat ground.",
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
        _parent = (Transform)EditorGUILayout.ObjectField(
            "Parent (optional)", _parent, typeof(Transform), true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Placement", EditorStyles.boldLabel);
        _flatFaceAxis = (FlatFaceAxis)EditorGUILayout.EnumPopup("Flat-Face Away Axis", _flatFaceAxis);
        EditorGUILayout.HelpBox(
            "If rocks land tipped on their side instead of lying flat against " +
            "the surface, try a different axis here.",
            MessageType.None);
        EditorGUILayout.MinMaxSlider("Scale Range", ref _minScale, ref _maxScale, 0.1f, 3f);
        EditorGUILayout.LabelField("  " + _minScale.ToString("0.00") + " – " + _maxScale.ToString("0.00"));
        _spacing    = EditorGUILayout.FloatField("Drag Spacing (m)", _spacing);
        _embedDepth = EditorGUILayout.FloatField("Embed Depth (m)", _embedDepth);
        _spinMode = (SpinMode)EditorGUILayout.EnumPopup("Spin Mode", _spinMode);
        if (_spinMode == SpinMode.Fixed)
        {
            _fixedSpinDegrees = EditorGUILayout.Slider("Spin Angle", _fixedSpinDegrees, 0f, 360f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            _fixedSpinDegrees = DrawAngleDial(_fixedSpinDegrees, 80f);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space();
        Color prevColor = GUI.backgroundColor;
        GUI.backgroundColor = _active ? new Color(0.6f, 1f, 0.6f) : prevColor;
        if (GUILayout.Button(_active ? "Placement ON — click in Scene view" : "Enable Placement",
                GUILayout.Height(32)))
            _active = !_active;
        GUI.backgroundColor = prevColor;

        bool hasPrefab = _prefabs.Exists(p => p != null);
        if (!hasPrefab)
            EditorGUILayout.HelpBox("Assign at least one prefab above.", MessageType.Warning);
    }

    // ── Scene view click handling ────────────────────────────────────────────
    private void OnSceneGUI(SceneView sceneView)
    {
        if (!_active) return;

        Event e = Event.current;
        int controlID = GUIUtility.GetControlID(FocusType.Passive);

        if (e.type == EventType.Layout || e.type == EventType.Repaint)
            HandleUtility.AddDefaultControl(controlID);

        bool isPlaceEvent =
            e.button == 0 && !e.alt &&
            (e.type == EventType.MouseDown || e.type == EventType.MouseDrag);

        if (!isPlaceEvent)
        {
            if (e.type == EventType.MouseUp)
                _lastPlacedPos = null;
            return;
        }

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 2000f))
        {
            bool shouldPlace = true;
            if (e.type == EventType.MouseDrag && _lastPlacedPos.HasValue)
                shouldPlace = Vector3.Distance(hit.point, _lastPlacedPos.Value) >= _spacing;

            if (shouldPlace)
            {
                PlaceRock(hit);
                _lastPlacedPos = hit.point;
            }
        }

        e.Use();
        sceneView.Repaint();
    }

    private void PlaceRock(RaycastHit hit)
    {
        var candidates = _prefabs.FindAll(p => p != null);
        if (candidates.Count == 0) return;

        GameObject prefab = candidates[Random.Range(0, candidates.Count)];
        var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (instance == null) return;

        Undo.RegisterCreatedObjectUndo(instance, "Place Rock");
        instance.transform.SetParent(_parent != null ? _parent : GetOrCreateDefaultParent(), true);

        Vector3 flatFaceAway = instance.transform.rotation * AxisToVector(_flatFaceAxis);
        Quaternion rot = Quaternion.FromToRotation(flatFaceAway, hit.normal) * instance.transform.rotation;
        switch (_spinMode)
        {
            case SpinMode.Random:
                rot = Quaternion.AngleAxis(Random.Range(0f, 360f), hit.normal) * rot;
                break;
            case SpinMode.Fixed:
                rot = Quaternion.AngleAxis(_fixedSpinDegrees, hit.normal) * rot;
                break;
        }
        instance.transform.rotation = rot;

        instance.transform.position = hit.point - hit.normal * _embedDepth;
        // Multiply the prefab's own baked scale rather than overwrite it — Meshy AI
        // exports commonly bake a large root scale (seen at 100/100/100) into the
        // prefab itself, and replacing localScale outright silently discarded that.
        instance.transform.localScale *= Random.Range(_minScale, _maxScale);
    }

    // Compass-style dial: 0° points up, increasing clockwise, matching the numeric
    // Spin Angle field it sits next to. Click or drag anywhere in the circle to set
    // the angle; Handles.BeginGUI/EndGUI is the standard way to draw arbitrary lines
    // in an otherwise-IMGUI EditorWindow panel.
    private float DrawAngleDial(float angleDegrees, float size)
    {
        Rect rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
        Vector3 center = new Vector3(rect.center.x, rect.center.y, 0f);
        float radius = size * 0.5f - 4f;

        int controlID = GUIUtility.GetControlID(FocusType.Passive);
        Event e = Event.current;

        if (e.type == EventType.Repaint)
        {
            Handles.BeginGUI();

            Handles.color = new Color(0.5f, 0.5f, 0.5f, 1f);
            Handles.DrawWireDisc(center, Vector3.forward, radius);

            for (int i = 0; i < 4; i++)
            {
                Vector3 dir = CompassDirVector(i * 90f);
                Handles.DrawLine(center + dir * (radius - 6f), center + dir * radius);
            }

            Handles.color = Color.yellow;
            Handles.DrawLine(center, center + CompassDirVector(angleDegrees) * radius);
            Handles.DrawSolidDisc(center, Vector3.forward, 3f);

            Handles.EndGUI();
        }

        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            GUIUtility.hotControl = controlID;
            angleDegrees = AngleFromMouse(center, e.mousePosition);
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == controlID)
        {
            angleDegrees = AngleFromMouse(center, e.mousePosition);
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.MouseUp && GUIUtility.hotControl == controlID)
        {
            GUIUtility.hotControl = 0;
            e.Use();
        }

        return angleDegrees;
    }

    private static Vector3 CompassDirVector(float compassDegrees)
    {
        float rad = compassDegrees * Mathf.Deg2Rad;
        // Screen space: x right, y down. 0° = up (-Y), clockwise positive.
        return new Vector3(Mathf.Sin(rad), -Mathf.Cos(rad), 0f);
    }

    private static float AngleFromMouse(Vector3 center, Vector2 mousePos)
    {
        Vector2 d = mousePos - (Vector2)center;
        float deg = Mathf.Atan2(d.x, -d.y) * Mathf.Rad2Deg;
        if (deg < 0f) deg += 360f;
        return deg;
    }

    private static Vector3 AxisToVector(FlatFaceAxis axis)
    {
        switch (axis)
        {
            case FlatFaceAxis.PosY: return Vector3.up;
            case FlatFaceAxis.NegY: return Vector3.down;
            case FlatFaceAxis.PosX: return Vector3.right;
            case FlatFaceAxis.NegX: return Vector3.left;
            case FlatFaceAxis.PosZ: return Vector3.forward;
            case FlatFaceAxis.NegZ: return Vector3.back;
            default:                return Vector3.up;
        }
    }

    private static Transform GetOrCreateDefaultParent()
    {
        var go = GameObject.Find("Scattered Rocks");
        if (go == null)
        {
            go = new GameObject("Scattered Rocks");
            Undo.RegisterCreatedObjectUndo(go, "Create Scattered Rocks Parent");
        }
        return go.transform;
    }
}
