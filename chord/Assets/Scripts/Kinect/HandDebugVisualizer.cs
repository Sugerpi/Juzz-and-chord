using UnityEngine;
using ChordPlayer.Core;

namespace ChordPlayer.Kinect
{
    /// <summary>
    /// Phase 1 sanity check: drives two plain Transforms (e.g. small spheres)
    /// to the tracked hand positions, gives a clear "fist" visual (clench +
    /// brighten) when a hand is CLOSED, and optionally prints positions to the
    /// Console. Resolves whichever <see cref="IBodyInputProvider"/> is live
    /// (real or simulated) via the ServiceLocator.
    /// </summary>
    public class HandDebugVisualizer : MonoBehaviour
    {
        [SerializeField] Transform _leftHand;
        [SerializeField] Transform _rightHand;
        [SerializeField] bool _hideWhenUntracked = true;

        [Header("Closed-hand (fist) feedback")]
        [SerializeField, Range(0.2f, 1.5f)] float _closedScale = 0.6f;
        [SerializeField] Color _closedTint = Color.white;

        [Header("Console logging")]
        [SerializeField] bool _logToConsole = true;
        [SerializeField, Min(0.05f)] float _logInterval = 0.5f;

        IBodyInputProvider _provider;
        float _nextLogTime;
        Vector3 _leftBaseScale = Vector3.one, _rightBaseScale = Vector3.one;
        bool _scalesCaptured;
        MaterialPropertyBlock _mpb;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        IBodyInputProvider Provider
        {
            get
            {
                if (_provider == null) ServiceLocator.TryGet(out _provider);
                return _provider;
            }
        }

        void LateUpdate()   // runs after the provider's Update, so data is fresh
        {
            var provider = Provider;
            if (provider == null) return;

            if (!_scalesCaptured)
            {
                if (_leftHand != null) _leftBaseScale = _leftHand.localScale;
                if (_rightHand != null) _rightBaseScale = _rightHand.localScale;
                _scalesCaptured = true;
            }

            BodyData body = provider.Body;
            Place(_leftHand, _leftBaseScale, body.LeftHand,
                body.IsTracked && body.LeftHandTracked, body.LeftHandState);
            Place(_rightHand, _rightBaseScale, body.RightHand,
                body.IsTracked && body.RightHandTracked, body.RightHandState);
            LogIfDue(body);
        }

        void Place(Transform target, Vector3 baseScale, Vector3 position, bool tracked, HandGesture state)
        {
            if (target == null) return;
            if (tracked) target.position = position;
            if (_hideWhenUntracked && target.gameObject.activeSelf != tracked)
                target.gameObject.SetActive(tracked);
            if (!tracked) return;

            bool closed = state == HandGesture.Closed;
            target.localScale = closed ? baseScale * _closedScale : baseScale;

            if (target.TryGetComponent<Renderer>(out var r))
            {
                _mpb ??= new MaterialPropertyBlock();
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(ColorId, closed ? _closedTint : r.sharedMaterial.color);
                r.SetPropertyBlock(_mpb);
            }
        }

        void LogIfDue(BodyData body)
        {
            if (!_logToConsole || Time.time < _nextLogTime) return;
            _nextLogTime = Time.time + _logInterval;

            if (!body.IsTracked)
            {
                Debug.Log("[Body] no user tracked");
                return;
            }
            Debug.Log(
                $"[Body] L {body.LeftHand:F2} ({body.LeftHandState})  " +
                $"R {body.RightHand:F2} ({body.RightHandState})");
        }
    }
}
