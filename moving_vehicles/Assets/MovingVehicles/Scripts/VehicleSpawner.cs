// VehicleSpawner.cs
// Place this on a GameObject in the scene alongside a VehiclePath.
// It manages the full vehicle lifecycle: creation, movement, and removal.
//
// QUICK START
// -----------
// 1. Create a VehiclePath (see VehiclePath.cs header for setup).
// 2. Create an empty GameObject, add VehicleSpawner, and assign the VehiclePath field.
// 3. Hit Play – vehicles start appearing automatically.

using System.Collections.Generic;
using UnityEngine;

namespace MovingVehicles
{
    // ------------------------------------------------------------------
    // Small serializable wrapper so the color pool shows as a tidy
    // list in the Inspector
    // ------------------------------------------------------------------
    [System.Serializable]
    public class ColorPool
    {
        [Tooltip("Vehicle body colors chosen at random for each spawn.")]
        public Color[] colors = new Color[]
        {
            new Color(0.80f, 0.10f, 0.10f),    // Red
            new Color(0.12f, 0.22f, 0.80f),    // Blue
            new Color(0.10f, 0.58f, 0.12f),    // Green
            new Color(0.90f, 0.75f, 0.10f),    // Yellow
            new Color(0.82f, 0.82f, 0.82f),    // Silver/White
            new Color(0.12f, 0.12f, 0.12f),    // Black
            new Color(0.55f, 0.25f, 0.08f),    // Brown
            new Color(0.90f, 0.48f, 0.10f),    // Orange
        };
    }

    // ------------------------------------------------------------------
    // Main spawner component
    // ------------------------------------------------------------------
    public class VehicleSpawner : MonoBehaviour
    {
        // ── Path reference ──────────────────────────────────────────────
        [Header("Path")]
        [Tooltip("The VehiclePath this spawner drives vehicles along.")]
        public VehiclePath vehiclePath;

        // ── Vehicle source ──────────────────────────────────────────────
        [Header("Vehicle Source")]
        [Tooltip("Checked  → use the built-in low-poly vehicle generator.\n" +
                 "Unchecked → choose randomly from your own Custom Prefabs list.")]
        public bool useBuiltInVehicles = true;

        [Tooltip("Your own vehicle prefab models.\n" +
                 "Only used when 'Use Built-in Vehicles' is OFF.\n" +
                 "One prefab is picked at random each spawn.\n" +
                 "Prefabs should face +Z (forward). " +
                 "A VehicleController is added automatically unless one is already on the prefab.")]
        public GameObject[] customVehiclePrefabs;

        // ── Traffic density ─────────────────────────────────────────────
        [Header("Traffic Density")]
        [Tooltip("Maximum vehicles allowed on the road simultaneously.")]
        [Range(1, 30)]
        public int maxVehicles = 6;

        [Tooltip("Minimum seconds between successive spawns.")]
        [Range(0.5f, 30f)]
        public float minSpawnInterval = 2.0f;

        [Tooltip("Maximum seconds between successive spawns.")]
        [Range(0.5f, 30f)]
        public float maxSpawnInterval = 6.0f;

        // ── Speed ───────────────────────────────────────────────────────
        [Header("Vehicle Speed")]
        [Tooltip("Slowest a vehicle can travel (world units / second). ~8 ≈ 30 km/h.")]
        [Range(1f, 50f)]
        public float minSpeed = 8f;

        [Tooltip("Fastest a vehicle can travel (world units / second). ~14 ≈ 50 km/h.")]
        [Range(1f, 50f)]
        public float maxSpeed = 14f;

        // ── Sound ───────────────────────────────────────────────────────
        [Header("Vehicle Sound")]
        [Tooltip("Engine sound looped on every vehicle while it moves.\n" +
                 "Leave empty for silent vehicles.\n\n" +
                 "Priority rule for custom prefabs:\n" +
                 "  If the prefab already has an AudioSource WITH a clip → that clip is used.\n" +
                 "  Otherwise this clip is applied as a fallback.")]
        public AudioClip vehicleAudioClip;

        [Tooltip("Master volume for the engine sound (0 = silent, 1 = full).")]
        [Range(0f, 1f)]
        public float audioVolume = 0.4f;

        [Tooltip("Distance (world units) at which the engine sound fades to silence.\n" +
                 "Tune this to match your scene scale.  ~50 m is a good starting point.")]
        public float audioMaxDistance = 50f;

        // ── Appearance ──────────────────────────────────────────────────
        [Header("Appearance")]
        public ColorPool colorPool = new ColorPool();

        // ── Vehicle type weights ────────────────────────────────────────
        [Header("Vehicle Type Mix  (0 = never spawns)")]
        [Tooltip("Relative frequency for sedan cars.")]
        [Range(0, 10)] public int sedanWeight    = 3;

        [Tooltip("Relative frequency for hatchback / crossover cars.")]
        [Range(0, 10)] public int hatchbackWeight = 3;

        [Tooltip("Relative frequency for pickup trucks.")]
        [Range(0, 10)] public int pickupWeight    = 2;

        [Tooltip("Relative frequency for tractor/trailer semis.")]
        [Range(0, 10)] public int tractorWeight   = 1;

        [Tooltip("Relative frequency for BBQ food trucks.")]
        [Range(0, 10)] public int foodTruckWeight = 1;

        // ── Optional parent for keeping the hierarchy tidy ──────────────
        [Header("Scene Organization (optional)")]
        [Tooltip("If assigned, spawned vehicles are parented here. " +
                 "Leave empty to parent them at scene root.")]
        public Transform vehicleParent;

        // ---------------------------------------------------------------
        // Private state
        // ---------------------------------------------------------------
        private List<VehicleController> _active   = new List<VehicleController>();
        private int[]                   _typePool;   // weighted lookup table
        private float                   _nextSpawn;

        // ---------------------------------------------------------------
        // Unity messages
        // ---------------------------------------------------------------

        private void Start()
        {
            if (vehiclePath == null)
            {
                Debug.LogError("[MovingVehicles] VehicleSpawner: no VehiclePath assigned.", this);
                enabled = false;
                return;
            }

            if (minSpawnInterval > maxSpawnInterval)
                maxSpawnInterval = minSpawnInterval;

            BuildTypePool();
            _nextSpawn = Time.time + Random.Range(0.2f, minSpawnInterval);
        }

        private void Update()
        {
            // Remove references to vehicles that were destroyed externally
            _active.RemoveAll(v => v == null);

            if (_active.Count < maxVehicles && Time.time >= _nextSpawn)
            {
                SpawnVehicle();
                _nextSpawn = Time.time + Random.Range(minSpawnInterval, maxSpawnInterval);
            }
        }

        // ---------------------------------------------------------------
        // Called by VehicleController when the vehicle reaches the end
        // ---------------------------------------------------------------
        public void OnVehicleReachedEnd(VehicleController ctrl)
        {
            _active.Remove(ctrl);
            Destroy(ctrl.gameObject);
        }

        // ---------------------------------------------------------------
        // Internal helpers
        // ---------------------------------------------------------------

        private void SpawnVehicle()
        {
            GameObject vehicle;

            if (useBuiltInVehicles)
            {
                // --- Pick type ------------------------------------------------
                var type = VehicleBuilder.VehicleType.Sedan;
                if (_typePool != null && _typePool.Length > 0)
                    type = (VehicleBuilder.VehicleType)_typePool[Random.Range(0, _typePool.Length)];

                // --- Pick color -----------------------------------------------
                Color bodyColor = Color.gray;
                if (colorPool.colors != null && colorPool.colors.Length > 0)
                    bodyColor = colorPool.colors[Random.Range(0, colorPool.colors.Length)];

                // --- Build procedural vehicle ---------------------------------
                vehicle = VehicleBuilder.Build(type, bodyColor);
            }
            else
            {
                // --- Validate custom prefab list ------------------------------
                if (customVehiclePrefabs == null || customVehiclePrefabs.Length == 0)
                {
                    Debug.LogWarning(
                        "[MovingVehicles] VehicleSpawner '" + name +
                        "': Use Built-in Vehicles is OFF but no Custom Prefabs are assigned.", this);
                    return;
                }

                // Collect non-null entries
                var valid = new List<GameObject>();
                foreach (var p in customVehiclePrefabs)
                    if (p != null) valid.Add(p);

                if (valid.Count == 0)
                {
                    Debug.LogWarning(
                        "[MovingVehicles] VehicleSpawner '" + name +
                        "': all Custom Prefab slots are empty (null).", this);
                    return;
                }

                // --- Instantiate a random prefab ------------------------------
                vehicle = Object.Instantiate(valid[Random.Range(0, valid.Count)]);
            }

            if (vehicleParent != null)
                vehicle.transform.SetParent(vehicleParent, false);

            // --- Position at spawn point ---------------------------------
            Vector3    spawnPos;
            Quaternion spawnRot;
            vehiclePath.Sample(0f, out spawnPos, out spawnRot);
            vehicle.transform.position = spawnPos;
            vehicle.transform.rotation = spawnRot;

            // --- Attach or reuse VehicleController -----------------------
            // GetComponent first so a VehicleController already on a custom
            // prefab is reused rather than creating a duplicate.
            VehicleController ctrl = vehicle.GetComponent<VehicleController>();
            if (ctrl == null) ctrl = vehicle.AddComponent<VehicleController>();
            ctrl.Initialize(vehiclePath, Random.Range(minSpeed, maxSpeed), this);

            // --- Apply audio ---------------------------------------------
            ApplyAudio(vehicle);

            _active.Add(ctrl);
        }

        /// <summary>
        /// Attaches or configures a looping AudioSource on the vehicle.
        /// Priority: existing AudioSource.clip on the GO/prefab > vehicleAudioClip on spawner.
        /// </summary>
        private void ApplyAudio(GameObject vehicle)
        {
            // Look for an AudioSource that was already on the prefab
            AudioSource src = vehicle.GetComponent<AudioSource>();

            if (src != null && src.clip != null)
            {
                // Prefab owns its audio – only enforce the spatial/loop settings
                src.loop         = true;
                src.spatialBlend = 1.0f;
                src.dopplerLevel = 0.0f;   // doppler sounds wrong on slow background traffic
                if (!src.isPlaying) src.Play();
                return;
            }

            // No prefab clip – use the spawner's fallback clip
            if (vehicleAudioClip == null) return;

            if (src == null) src = vehicle.AddComponent<AudioSource>();

            src.clip         = vehicleAudioClip;
            src.loop         = true;
            src.playOnAwake  = true;
            src.volume       = audioVolume;
            src.spatialBlend = 1.0f;               // full 3-D positional audio
            src.minDistance  = 2.0f;
            src.maxDistance  = audioMaxDistance;
            src.rolloffMode  = AudioRolloffMode.Linear;
            src.dopplerLevel = 0.0f;
            src.Play();
        }

        private void BuildTypePool()
        {
            var pool = new List<int>();
            for (int i = 0; i < sedanWeight;     i++) pool.Add(0);
            for (int i = 0; i < hatchbackWeight;  i++) pool.Add(1);
            for (int i = 0; i < pickupWeight;     i++) pool.Add(2);
            for (int i = 0; i < tractorWeight;    i++) pool.Add(3);
            for (int i = 0; i < foodTruckWeight;  i++) pool.Add(4);

            if (pool.Count == 0)
            {
                Debug.LogWarning("[MovingVehicles] All vehicle weights are 0 – defaulting to Sedan.", this);
                pool.Add(0);
            }

            _typePool = pool.ToArray();
        }

        // ---------------------------------------------------------------
        // Convenience: restart traffic (e.g. after scene reload)
        // ---------------------------------------------------------------
        public void ClearAll()
        {
            foreach (var v in _active)
                if (v != null) Destroy(v.gameObject);
            _active.Clear();
            _nextSpawn = Time.time + Random.Range(0.2f, minSpawnInterval);
        }
    }
}
