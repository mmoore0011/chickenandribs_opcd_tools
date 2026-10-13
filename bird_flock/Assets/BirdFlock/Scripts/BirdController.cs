// BirdController.cs
// Attached at runtime by BirdSpawner to each flock root.
// Moves the entire formation along the BirdPath and notifies the
// spawner when the despawn point is reached.

using UnityEngine;

namespace BirdFlock
{
    [DisallowMultipleComponent]
    public class BirdController : MonoBehaviour
    {
        private BirdPath    _path;
        private float       _speed;
        private BirdSpawner _spawner;
        private float       _distanceTraveled;
        private bool        _finished;

        public void Initialize(BirdPath path, float speed, BirdSpawner spawner)
        {
            _path             = path;
            _speed            = Mathf.Max(0.1f, speed);
            _spawner          = spawner;
            _distanceTraveled = 0f;
            _finished         = false;
        }

        private void Update()
        {
            if (_path == null || _finished) return;

            _distanceTraveled += _speed * Time.deltaTime;

            Vector3    pos;
            Quaternion rot;

            if (_path.Sample(_distanceTraveled, out pos, out rot))
            {
                transform.position = pos;
                transform.rotation = rot;
            }

            if (_distanceTraveled >= _path.TotalTravelDistance)
            {
                _finished = true;

                if (_spawner != null)
                    _spawner.OnFlockReachedEnd(this);
                else
                    Destroy(gameObject);
            }
        }
    }
}
