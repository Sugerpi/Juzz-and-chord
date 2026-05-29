using System.Collections;
using UnityEngine;

namespace ChordPlayer.Visuals
{
    /// <summary>
    /// One wheel segment. Owns no material instance — all per-segment state
    /// (colour, selection, hover) is pushed through a MaterialPropertyBlock so
    /// every segment shares one material (spec requirement). Selection animates
    /// via a coroutine (the agreed free-tween fallback for DOTween).
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class WheelSegment : MonoBehaviour
    {
        static readonly int SelectionId = Shader.PropertyToID("_Selection");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int HoverId = Shader.PropertyToID("_HoverIntensity");

        MeshRenderer _renderer;
        MaterialPropertyBlock _mpb;

        int _index;
        Color _color = Color.white;
        float _selection;
        Coroutine _tween;

        public int Index => _index;
        public float Selection => _selection;

        public void Init(int index, Color color)
        {
            _index = index;
            _color = color;
            EnsureRefs();
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, _color);
            _mpb.SetColor(EmissionColorId, _color);
            _mpb.SetFloat(SelectionId, _selection);
            _renderer.SetPropertyBlock(_mpb);
        }

        void EnsureRefs()
        {
            if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
            _mpb ??= new MaterialPropertyBlock();
        }

        /// <summary>Set selection instantly (used by the Editor preview slider).</summary>
        public void ApplySelection(float value)
        {
            _selection = Mathf.Clamp01(value);
            EnsureRefs();
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(SelectionId, _selection);
            _renderer.SetPropertyBlock(_mpb);
        }

        /// <summary>Animate selection at runtime; snaps instantly in edit mode.</summary>
        public void AnimateSelection(float target, float duration)
        {
            if (!Application.isPlaying || duration <= 0f) { ApplySelection(target); return; }
            if (_tween != null) StopCoroutine(_tween);
            _tween = StartCoroutine(TweenSelection(target, duration));
        }

        public void SetHover(float value)
        {
            EnsureRefs();
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(HoverId, Mathf.Clamp01(value));
            _renderer.SetPropertyBlock(_mpb);
        }

        IEnumerator TweenSelection(float target, float duration)
        {
            float start = _selection, t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                ApplySelection(Mathf.SmoothStep(start, target, t / duration));
                yield return null;
            }
            ApplySelection(target);
            _tween = null;
        }
    }
}
