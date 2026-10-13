// BirdSpawner.cs
// Spawns bird flocks that fly along a BirdPath at timed intervals.
//
// QUICK START
// 1. Create a BirdPath (see BirdPath.cs header).
// 2. Create an empty GameObject, add BirdSpawner, assign the BirdPath field.
// 3. Hit Play – flocks appear at the interval you set and fly across the scene.

using System.Collections.Generic;
using UnityEngine;

namespace BirdFlock
{
    public enum FlightPattern
    {
        VFormation   = 0,
        SingleObject = 1   // one custom prefab placed at the path spawn point — use for murmuration, etc.
    }

    public class BirdSpawner : MonoBehaviour
    {
        // ── Path ──────────────────────────────────────────────────────────
        [Header("Path")]
        [Tooltip("The BirdPath this spawner sends flocks along.")]
        public BirdPath birdPath;

        // ── Flock composition ─────────────────────────────────────────────
        [Header("Flock")]
        [Tooltip("Formation shape for each flock.")]
        public FlightPattern flightPattern = FlightPattern.VFormation;

        [Tooltip("Number of birds in each flock.")]
        [Range(3, 50)]
        public int numberOfBirds = 14;

        // ── Bird Source ───────────────────────────────────────────────────
        [Header("Bird Source")]
        [Tooltip("When enabled, birds are built from low-poly procedural geometry. " +
                 "Disable to supply your own bird prefab.")]
        public bool useBuiltInBirds = true;

        [Tooltip("1–6 bird prefabs. Each formation slot picks one at random. " +
                 "Has no effect when Use Built-In Birds is enabled.")]
        public GameObject[] customBirdPrefabs = new GameObject[0];

        // ── Timing ────────────────────────────────────────────────────────
        [Header("Timing  (seconds — 1 = 1 s, 3600 = 1 hour)")]
        [Tooltip("Minimum wait between flock appearances.")]
        [Range(1f, 3600f)]
        public float minInterval = 240f;    // 4 minutes default

        [Tooltip("Maximum wait between flock appearances.")]
        [Range(1f, 3600f)]
        public float maxInterval = 720f;    // 12 minutes default

        // ── Speed ─────────────────────────────────────────────────────────
        [Header("Speed  (world units / second)")]
        [Tooltip("Minimum flock speed.  ~8 m/s ≈ 29 km/h, typical cruising speed for geese.")]
        [Range(2f, 40f)]
        public float minSpeed = 8f;

        [Tooltip("Maximum flock speed.")]
        [Range(2f, 40f)]
        public float maxSpeed = 14f;

        [Tooltip("How much the speed varies along the path. " +
                 "0 = constant speed. 0.4 = speeds up and slows down by ±40%. " +
                 "Has no effect at runtime — only applies when baking.")]
        [Range(0f, 0.6f)]
        public float speedVariation = 0.3f;

        // ── Scene organization ────────────────────────────────────────────
        [Header("Scene Organization  (optional)")]
        [Tooltip("If assigned, spawned flocks are parented here to keep the Hierarchy tidy.")]
        public Transform flockParent;

        // ── Sound ─────────────────────────────────────────────────────────
        [Header("Bird Sound  (optional)")]
        [Tooltip("Optional audio clip played on every spawned flock (wing beats, calls, etc.).\n" +
                 "If the prefab already has an AudioSource with a clip assigned, that clip takes priority.")]
        public AudioClip birdAudioClip;

        [Range(0f, 1f)]
        [Tooltip("Volume of the flock audio source.")]
        public float audioVolume = 0.4f;

        [Tooltip("Distance (world units) at which the flock sound is inaudible.")]
        public float audioMaxDistance = 80f;

        // ---------------------------------------------------------------
        // Private state
        // ---------------------------------------------------------------
        private List<BirdController> _active = new List<BirdController>();
        private float                _nextSpawn;

        // ---------------------------------------------------------------
        // Unity messages
        // ---------------------------------------------------------------

        private void Start()
        {
            if (birdPath == null)
            {
                Debug.LogError("[BirdFlock] BirdSpawner: no BirdPath assigned.", this);
                enabled = false;
                return;
            }

            if (minInterval > maxInterval)
                maxInterval = minInterval;

            if (minSpeed > maxSpeed)
                maxSpeed = minSpeed;

            // Stagger first spawn so multiple spawners don't all fire at once
            _nextSpawn = Time.time + Random.Range(2f, minInterval);
        }

        private void Update()
        {
            _active.RemoveAll(b => b == null);

            if (Time.time >= _nextSpawn)
            {
                SpawnFlock();
                _nextSpawn = Time.time + Random.Range(minInterval, maxInterval);
            }
        }

        // ---------------------------------------------------------------
        // Called by BirdController when the flock reaches the despawn point
        // ---------------------------------------------------------------
        public void OnFlockReachedEnd(BirdController ctrl)
        {
            _active.Remove(ctrl);
            Destroy(ctrl.gameObject);
        }

        // ---------------------------------------------------------------
        // Internal helpers
        // ---------------------------------------------------------------

        private void SpawnFlock()
        {
            GameObject flock;

            if (useBuiltInBirds)
            {
                flock = BirdBuilder.BuildFlock(flightPattern, numberOfBirds);
            }
            else
            {
                GameObject[] valid = ValidPrefabs();
                if (valid.Length == 0)
                {
                    Debug.LogWarning(
                        "[BirdFlock] BirdSpawner: no bird prefabs assigned – flock skipped.", this);
                    return;
                }
                flock = BirdBuilder.BuildFlockFromPrefabs(flightPattern, numberOfBirds, valid);
            }

            if (flockParent != null)
                flock.transform.SetParent(flockParent, false);

            // Position the flock root at the path spawn point
            Vector3    spawnPos;
            Quaternion spawnRot;
            birdPath.Sample(0f, out spawnPos, out spawnRot);
            flock.transform.position = spawnPos;
            flock.transform.rotation = spawnRot;

            ApplyAudio(flock);

            // Attach the movement controller
            BirdController ctrl = flock.AddComponent<BirdController>();
            ctrl.Initialize(birdPath, Random.Range(minSpeed, maxSpeed), this);

            _active.Add(ctrl);
        }

        private void ApplyAudio(GameObject flock)
        {
            if (birdAudioClip == null) return;

            // If the flock root already has an AudioSource with its own clip, leave it alone.
            AudioSource existing = flock.GetComponent<AudioSource>();
            if (existing != null && existing.clip != null) return;

            AudioSource src = (existing != null) ? existing : flock.AddComponent<AudioSource>();
            src.clip         = birdAudioClip;
            src.volume       = audioVolume;
            src.loop         = true;
            src.playOnAwake  = true;
            src.spatialBlend = 1f;
            src.dopplerLevel = 0f;
            src.maxDistance  = audioMaxDistance;
            src.rolloffMode  = AudioRolloffMode.Linear;
            src.Play();
        }

        // ---------------------------------------------------------------
        // Convenience: remove all active flocks (e.g. on scene reload)
        // ---------------------------------------------------------------
        public GameObject[] ValidPrefabs()
        {
            if (customBirdPrefabs == null) return new GameObject[0];
            return System.Array.FindAll(customBirdPrefabs, p => p != null);
        }

        // ---------------------------------------------------------------
        public void ClearAll()
        {
            foreach (var b in _active)
                if (b != null) Destroy(b.gameObject);
            _active.Clear();
            _nextSpawn = Time.time + Random.Range(2f, minInterval);
        }
    }
}
