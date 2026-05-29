using UnityEngine;

namespace ChordPlayer.Kinect
{
    /// <summary>
    /// Frame-rate-independent exponential moving-average filter for a single
    /// Vector3 signal (e.g. one tracked joint). A small deadband suppresses
    /// micro-tremor so a still hand reads as still.
    ///
    /// This is signal filtering, not animation — using a filtered follow here
    /// is correct. (Animation tweens go through DOTween per the project spec.)
    /// </summary>
    [System.Serializable]
    public class JointSmoother
    {
        [SerializeField, Range(0.01f, 0.5f),
         Tooltip("Seconds for the value to move ~63% toward a new target. " +
                 "Lower = snappier, higher = smoother but laggier.")]
        float _timeConstant = 0.08f;

        [SerializeField, Min(0f),
         Tooltip("Movement below this distance (metres) is ignored as jitter.")]
        float _deadband = 0.004f;

        Vector3 _value;
        bool _initialised;

        public Vector3 Value => _value;

        public void Reset() => _initialised = false;

        public Vector3 Update(Vector3 target, float deltaTime)
        {
            if (!_initialised)
            {
                _value = target;
                _initialised = true;
                return _value;
            }

            if ((target - _value).sqrMagnitude < _deadband * _deadband)
                return _value;

            // alpha = 1 - e^(-dt / tau): exact EMA response regardless of frame rate.
            float alpha = 1f - Mathf.Exp(-deltaTime / Mathf.Max(_timeConstant, 1e-4f));
            _value = Vector3.Lerp(_value, target, alpha);
            return _value;
        }
    }
}
