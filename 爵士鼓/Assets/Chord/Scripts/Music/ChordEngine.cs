using System;
using System.Collections.Generic;
using UnityEngine;
using ChordPlayer.Core;
using ChordPlayer.Kinect;
using ChordPlayer.Visuals;

namespace ChordPlayer.Music
{
    /// <summary>
    /// The brain: each hand is a cursor that points at a wheel. The segment the
    /// cursor points to (its arm direction, matched to that wheel's visual layout)
    /// is the selection — so what you point at is what lights up. Fist = play/stop.
    /// </summary>
    public class ChordEngine : MonoBehaviour
    {
        public enum Trigger { EitherHand, BothHands, LeftHand, RightHand }

        [Header("References")]
        [SerializeField] ChordTable _table;
        [SerializeField] AudioConductor _audio;
        [SerializeField] ChordWheel _leftWheel;   // root
        [SerializeField] ChordWheel _rightWheel;  // quality

        [Header("Pointing")]
        [SerializeField, Range(0f, 1f)] float _selectThreshold = 0.45f; // how far onto the ring the hand must be
        [SerializeField, Min(0f)] float _hysteresisDeg = 8f;            // anti-flicker at wedge borders

        [Header("Trigger")]
        [SerializeField] Trigger _trigger = Trigger.EitherHand;
        [SerializeField, Min(0f)] float _triggerDebounce = 0.09f; // ignore hand-state flicker

        IBodyInputProvider _input;
        readonly List<int> _noteBuffer = new();

        Chord _selection = new(-1, -1);
        int _rootSector = -1, _qualitySector = -1;
        bool _isPlaying;
        Chord _playing;

        public ChordTable Table => _table;
        public Chord Selection => _selection;
        public bool IsPlaying => _isPlaying;
        public Chord Playing => _playing;

        public event Action<Chord> SelectionChanged;
        public event Action<Chord> ChordStarted;
        public event Action ChordStopped;

        void OnEnable() => ServiceLocator.Register(this);

        void OnDisable()
        {
            if (_isPlaying) Stop();
            ServiceLocator.Unregister<ChordEngine>();
        }

        void Update()
        {
            if (_input == null) ServiceLocator.TryGet(out _input);
            if (_input == null || _table == null) return;

            BodyData body = _input.Body;
            if (!body.IsTracked)
            {
                if (_isPlaying) Stop();
                return;
            }

            UpdateSelection(body);
            UpdateTrigger(body);
        }

        void UpdateSelection(BodyData body)
        {
            int root = ResolveSector(body, true, _leftWheel, ref _rootSector, _table.RootCount);
            int quality = ResolveSector(body, false, _rightWheel, ref _qualitySector, _table.QualityCount);

            var next = new Chord(root, quality);
            if (next != _selection)
            {
                _selection = next;
                SelectionChanged?.Invoke(_selection);
            }
        }

        // Selection by overlap: which segment is the hand physically over on the
        // wheel. Uses the hand's offset from the wheel centre (the wheel is moved
        // to the hand's neutral spot by calibration). Holds when the hand is near
        // the centre (inside the deadzone), with hysteresis on wedge borders.
        int ResolveSector(BodyData body, bool left, ChordWheel wheel, ref int current, int count)
        {
            if (count <= 0) return 0;
            if (wheel == null) return Mathf.Clamp(current < 0 ? 0 : current, 0, count - 1);

            Vector3 hand = left ? body.LeftHand : body.RightHand;
            Vector3 c = wheel.transform.position;
            Vector2 offset = new Vector2(hand.x - c.x, hand.y - c.y);

            float reach01 = offset.magnitude / Mathf.Max(0.01f, wheel.OuterRadius);
            if (reach01 < _selectThreshold) return current < 0 ? (current = 0) : current;

            float angle = Mathf.Atan2(offset.x, offset.y) * Mathf.Rad2Deg;
            int candidate = wheel.SectorAtAngle(angle);

            if (current < 0) { current = candidate; }
            else if (candidate != current)
            {
                float fromCurrent = Mathf.Abs(Mathf.DeltaAngle(angle, wheel.SegmentCenterAngle(current)));
                float halfWedge = 180f / count;   // = (360/count)/2
                if (fromCurrent > halfWedge + _hysteresisDeg) current = candidate;
            }
            return Mathf.Clamp(current, 0, count - 1);
        }

        float _fireTimer;

        void UpdateTrigger(BodyData body)
        {
            bool l = body.LeftHandState == HandGesture.Closed;
            bool r = body.RightHandState == HandGesture.Closed;
            bool raw = _trigger switch
            {
                Trigger.EitherHand => l || r,
                Trigger.BothHands => l && r,
                Trigger.LeftHand => l,
                Trigger.RightHand => r,
                _ => false
            };

            // Debounce: only flip after the new state holds long enough, so a
            // one-frame Closed/Unknown flicker doesn't misfire or retrigger.
            if (raw != _isPlaying)
            {
                _fireTimer += Time.deltaTime;
                if (_fireTimer >= _triggerDebounce)
                {
                    if (raw) Play(_selection); else Stop();
                    _fireTimer = 0f;
                }
            }
            else _fireTimer = 0f;
        }

        public void Play(Chord chord)
        {
            _playing = chord;
            _isPlaying = true;
            if (_audio != null && _table != null)
            {
                _table.BuildNotes(chord.RootIndex, chord.QualityIndex, _noteBuffer);
                _audio.PlayChord(_noteBuffer);
            }
            ChordStarted?.Invoke(chord);
        }

        public void Stop()
        {
            _isPlaying = false;
            if (_audio != null) _audio.StopAll();
            ChordStopped?.Invoke();
        }
    }
}
