using UnityEngine;
using ChordPlayer.Core;
using ChordPlayer.Kinect;
using ChordPlayer.Music;

namespace ChordPlayer.Visuals
{
    /// <summary>
    /// Glowing cursor that sits exactly on the tracked hand (same position as the
    /// skeleton hand). What it covers on the wheel is what gets selected. Colour
    /// matches the selected sector and it brightens on a fist.
    /// </summary>
    public class HandIndicator : MonoBehaviour
    {
        public enum Side { Left, Right }

        [SerializeField] Side _side = Side.Left;
        [SerializeField] Renderer _orbRenderer;
        [SerializeField] VisualStyleSO _style;
        [SerializeField] float _zOffset = -0.05f;   // sit just in front of the wheel plane
        [SerializeField, Range(0f, 1f)] float _idleProximity = 0.45f;
        [SerializeField, Range(0f, 1f)] float _fistProximity = 1f;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int ProximityId = Shader.PropertyToID("_Proximity");

        IBodyInputProvider _input;
        ChordEngine _engine;
        MaterialPropertyBlock _mpb;

        void Awake()
        {
            if (_orbRenderer == null) _orbRenderer = GetComponent<Renderer>();
            _mpb = new MaterialPropertyBlock();
        }

        void LateUpdate()
        {
            if (_input == null) ServiceLocator.TryGet(out _input);
            if (_engine == null) ServiceLocator.TryGet(out _engine);
            if (_input == null) return;

            BodyData body = _input.Body;
            bool tracked = body.IsTracked &&
                           (_side == Side.Left ? body.LeftHandTracked : body.RightHandTracked);

            if (_orbRenderer != null && _orbRenderer.enabled != tracked) _orbRenderer.enabled = tracked;
            if (!tracked) return;

            Vector3 hand = _side == Side.Left ? body.LeftHand : body.RightHand;
            transform.position = new Vector3(hand.x, hand.y, _zOffset);

            if (_orbRenderer == null) return;
            bool fist = (_side == Side.Left ? body.LeftHandState : body.RightHandState) == HandGesture.Closed;
            _orbRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(ColorId, SelectedColor());
            _mpb.SetFloat(ProximityId, fist ? _fistProximity : _idleProximity);
            _orbRenderer.SetPropertyBlock(_mpb);
        }

        Color SelectedColor()
        {
            if (_style == null || _engine == null || _engine.Table == null) return Color.white;
            Chord sel = _engine.Selection;
            return _side == Side.Left
                ? _style.GetSegmentColor(Mathf.Max(0, sel.RootIndex), _engine.Table.RootCount)
                : _style.GetSegmentColor(Mathf.Max(0, sel.QualityIndex), _engine.Table.QualityCount);
        }
    }
}
