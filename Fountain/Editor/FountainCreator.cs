// FountainCreator.cs
// Editor window that procedurally generates a particle-based fountain prefab.
// Each stream is a child ParticleSystem – a built-in Unity component that
// requires no custom scripts, following the same bake-everything pattern as
// the Sky Dome and Moving Vehicles tools.
//
// Open via:  Tools > chickenandribs > Fountain > Create Fountain

using UnityEditor;
using UnityEngine;

namespace Fountain
{
    public class FountainCreator : EditorWindow
    {
        // ── Configurable fields ───────────────────────────────────────────────
        private string   _fountainName     = "Fountain";
        private bool     _roundFountain    = true;
        private int      _streamCount      = 8;
        private float    _tiltDegrees      = 35f;   // 0 = straight up, 90 = horizontal
        private bool     _centralStream    = true;
        private float    _streamSpeed      = 6f;
        private float    _streamLifetime   = 2.5f;
        private float    _emissionRate     = 60f;
        private Color    _waterColor       = new Color(0.75f, 0.92f, 1.0f, 0.70f);
        private Material _particleMaterial = null;
        private bool     _addBase          = true;
        private float    _basePosY         = -0.348f;
        private float    _baseScaleX       = 0.516f;
        private float    _baseScaleY       = 0.413f;
        private float    _baseScaleZ       = 0.945f;

        private GUIStyle _header;

        private const string DefaultMatPath =
            "Assets/Standard Assets/ParticleSystems/Materials/ParticleSplashes.mat";

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void OnEnable()
        {
            if (_particleMaterial == null)
                _particleMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultMatPath);
        }

        // ── Menu item ─────────────────────────────────────────────────────────
        [MenuItem("Tools/chickenandribs/Fountain/Create Fountain", false, 300)]
        private static void OpenWindow()
        {
            var win = GetWindow<FountainCreator>("Fountain Creator");
            win.minSize = new Vector2(340, 560);
        }

        // ── Inspector GUI ─────────────────────────────────────────────────────
        private void OnGUI()
        {
            if (_header == null)
            {
                _header        = new GUIStyle(EditorStyles.boldLabel);
                _header.margin = new RectOffset(0, 0, 8, 2);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Fountain Creator", _header);
            EditorGUILayout.HelpBox(
                "Generates a particle-based fountain prefab using built-in Unity " +
                "ParticleSystem components — no runtime scripts required.\n" +
                "Test in GSPro to confirm particle support in your build.",
                MessageType.Info);

            EditorGUILayout.Space();

            // ── Name ──────────────────────────────────────────────────────────
            _fountainName = EditorGUILayout.TextField(
                new GUIContent("Name",
                    "Name used for the prefab file and the root GameObject. " +
                    "Each name creates a separate prefab, so you can have " +
                    "multiple fountains in the same project."),
                _fountainName);
            if (string.IsNullOrWhiteSpace(_fountainName))
                _fountainName = "Fountain";

            EditorGUILayout.Space();

            // ── Fountain Type ─────────────────────────────────────────────────
            EditorGUILayout.LabelField("Fountain Type", _header);
            _roundFountain = EditorGUILayout.Toggle(
                new GUIContent("Round Fountain",
                    "Evenly spaces streams in a full 360° circle, all tilted outward " +
                    "at the same angle — the classic umbrella/dome fountain shape."),
                _roundFountain);

            EditorGUILayout.Space();

            // ── Streams (round fountain only) ─────────────────────────────────
            if (_roundFountain)
            {
                EditorGUILayout.LabelField("Streams", _header);

                _streamCount = EditorGUILayout.IntSlider(
                    new GUIContent("Stream Count",
                        "Number of radial streams arranged evenly around the centre."),
                    _streamCount, 2, 24);

                _tiltDegrees = EditorGUILayout.Slider(
                    new GUIContent("Outward Tilt  (°)",
                        "How far each stream leans away from vertical.\n" +
                        "0° = all streams shoot straight up.\n" +
                        "35° = gentle dome shape like a classic umbrella fountain.\n" +
                        "70° = wide low spray."),
                    _tiltDegrees, 0f, 85f);

                _centralStream = EditorGUILayout.Toggle(
                    new GUIContent("Central Stream",
                        "Add one stream shooting straight up from the centre, " +
                        "slightly faster than the radial streams."),
                    _centralStream);

                EditorGUILayout.Space();
            }

            // ── Physics ───────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Physics", _header);

            _streamSpeed = EditorGUILayout.Slider(
                new GUIContent("Stream Speed",
                    "Initial particle velocity in metres/second. " +
                    "Higher = taller arcs. Central stream runs at 1.3× this value."),
                _streamSpeed, 1f, 20f);

            _streamLifetime = EditorGUILayout.Slider(
                new GUIContent("Particle Lifetime  (s)",
                    "How long each water particle lives. " +
                    "Tune so particles fade just before reaching the water surface."),
                _streamLifetime, 0.5f, 6f);

            _emissionRate = EditorGUILayout.Slider(
                new GUIContent("Emission Rate",
                    "Particles emitted per second per stream. " +
                    "Higher = denser, more opaque streams."),
                _emissionRate, 10f, 200f);

            EditorGUILayout.Space();

            // ── Appearance ────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Appearance", _header);

            _waterColor = EditorGUILayout.ColorField(
                new GUIContent("Water Color",
                    "Tint colour applied on top of the material texture. " +
                    "Light blue at ~70% alpha works well for outdoor fountains."),
                _waterColor);

            _particleMaterial = (Material)EditorGUILayout.ObjectField(
                new GUIContent("Particle Material",
                    "Material used for every stream particle. " +
                    "Leave empty to use Unity's built-in Default-Particle material.\n\n" +
                    "For best results use a soft additive or alpha-blended particle " +
                    "material with a round or teardrop texture."),
                _particleMaterial, typeof(Material), false);

            if (_particleMaterial == null)
                EditorGUILayout.HelpBox(
                    "ParticleSplashes not found at the Standard Assets path. " +
                    "Will fall back to Default-Particle — assign a material manually for best results.",
                    MessageType.Warning);

            EditorGUILayout.Space();

            // ── Base Disk ─────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Base", _header);

            _addBase = EditorGUILayout.Toggle(
                new GUIContent("Add Base Disk",
                    "Adds a flat cylinder at the fountain origin to hide where " +
                    "the streams cross. Assign a material to it after creation " +
                    "(stone, metal grate, etc.)."),
                _addBase);

            if (_addBase)
            {
                _basePosY  = EditorGUILayout.FloatField(
                    new GUIContent("Position Y",
                        "Local Y offset of the disk from the fountain origin."),
                    _basePosY);
                _baseScaleX = EditorGUILayout.FloatField(
                    new GUIContent("Scale X",
                        "Width of the disk along the local X axis."),
                    _baseScaleX);
                _baseScaleY = EditorGUILayout.FloatField(
                    new GUIContent("Scale Y  (thickness)",
                        "Vertical thickness of the disk."),
                    _baseScaleY);
                _baseScaleZ = EditorGUILayout.FloatField(
                    new GUIContent("Scale Z",
                        "Width of the disk along the local Z axis."),
                    _baseScaleZ);
            }

            GUILayout.Space(12f);

            GUI.backgroundColor = new Color(0.55f, 0.9f, 0.55f);
            if (GUILayout.Button("Create Fountain", GUILayout.Height(36)))
                CreateFountain();
            GUI.backgroundColor = Color.white;
        }

        // ── Creation ──────────────────────────────────────────────────────────
        private void CreateFountain()
        {
            EnsureFolder("Assets/Fountain");
            EnsureFolder("Assets/Fountain/Prefabs");

            var root = new GameObject(_fountainName);

            if (_roundFountain)
            {
                if (_centralStream)
                    AddStream(root, 0f, 0f,
                              _streamSpeed * 1.3f, _streamLifetime * 1.1f, "Stream_Center");

                for (int i = 0; i < _streamCount; i++)
                {
                    float azimuth = i * 360f / _streamCount;
                    AddStream(root, _tiltDegrees, azimuth,
                              _streamSpeed, _streamLifetime,
                              "Stream_" + i.ToString("D2"));
                }
            }
            else
            {
                AddStream(root, 0f, 0f, _streamSpeed, _streamLifetime, "Stream_Single");
            }

            if (_addBase)
                AddBaseDisk(root);

            string prefabPath = "Assets/Fountain/Prefabs/" + _fountainName + ".prefab";
            AssetDatabase.DeleteAsset(prefabPath);
#pragma warning disable 618
            GameObject prefab = PrefabUtility.CreatePrefab(prefabPath, root);
#pragma warning restore 618

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            DestroyImmediate(root);

            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            Debug.Log(
                "[Fountain] Prefab saved to " + prefabPath + "\n" +
                (_roundFountain
                    ? "Round: " + _streamCount + " streams" +
                      (_centralStream ? " + 1 central" : "") +
                      "  |  Tilt: " + _tiltDegrees + "°"
                    : "Single stream") +
                "  |  Speed: " + _streamSpeed + " m/s");
        }

        // ── Stream builder ────────────────────────────────────────────────────
        //
        // Rotation:
        //   The cone shape emits along local +Z. LookRotation maps local +Z to
        //   the desired world emission direction, so bounds are computed correctly
        //   for frustum culling. tilt=0 → straight up; tilt=35 → 35° outward.

        private void AddStream(GameObject parent,
                               float tiltDeg, float azimuthDeg,
                               float speed,   float lifetime,
                               string streamName)
        {
            var go = new GameObject(streamName);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = Vector3.zero;

            float   tiltRad = tiltDeg * Mathf.Deg2Rad;
            float   azRad   = azimuthDeg * Mathf.Deg2Rad;
            Vector3 emitDir = new Vector3(
                Mathf.Sin(tiltRad) * Mathf.Sin(azRad),
                Mathf.Cos(tiltRad),
                Mathf.Sin(tiltRad) * Mathf.Cos(azRad));
            // Guard against degenerate LookRotation when emitDir is nearly world-up
            Vector3 refUp = (Mathf.Abs(emitDir.y) > 0.99f) ? Vector3.forward : Vector3.up;
            go.transform.localRotation = Quaternion.LookRotation(emitDir, refUp);

            var ps   = go.AddComponent<ParticleSystem>();
            var main = ps.main;

            main.startSpeed      = new ParticleSystem.MinMaxCurve(speed * 0.95f, speed * 1.05f);
            main.startLifetime   = new ParticleSystem.MinMaxCurve(lifetime * 0.9f, lifetime * 1.1f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.10f);
            main.startColor      = new ParticleSystem.MinMaxGradient(_waterColor);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(1.0f);
            main.maxParticles    = 500;
            main.loop            = true;
            main.playOnAwake     = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = _emissionRate;

            var shape = ps.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle     = 2f;
            shape.radius    = 0.02f;

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode    = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.08f;
            rend.lengthScale   = 1.5f;

            Material mat = _particleMaterial
                        ?? AssetDatabase.LoadAssetAtPath<Material>(DefaultMatPath)
                        ?? AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
            if (mat != null)
                rend.sharedMaterial = mat;
        }

        // ── Base disk builder ─────────────────────────────────────────────────
        //
        // A flat cylinder (scale Y = 0.02) centred on the fountain origin.
        // No material is pre-assigned so the user can drag their own onto it
        // after the prefab is generated (stone, metal grate, etc.).

        private void AddBaseDisk(GameObject parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "FountainBase";
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = new Vector3(0f, _basePosY, 0f);
            go.transform.localScale    = new Vector3(_baseScaleX, _baseScaleY, _baseScaleZ);

            // Remove the auto-added collider — not needed for a decorative base
            Object.DestroyImmediate(go.GetComponent<Collider>());

            // Leave material unassigned so the user can set their own.
            // Assign the default diffuse as a visible placeholder (not pink).
            var mr  = go.GetComponent<MeshRenderer>();
            var mat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat");
            if (mat != null)
                mr.sharedMaterial = mat;
        }

        // ── Folder utility ────────────────────────────────────────────────────

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
