using System.Collections.Generic;
using UnityEngine;
using ChordPlayer.Core;

namespace ChordPlayer.Music
{
    /// <summary>
    /// Polyphonic Juno-style poly synth. Each voice = 2 detuned saws + a pulse
    /// wave, summed through a one-pole low-pass and an ADSR amp envelope. Tuned
    /// for an 80s synth-pop / city-pop / synthwave chord feel — snappier and
    /// brighter than a wash pad. Same PlayChord / NoteOn / NoteOff / StopAll
    /// surface, so ChordEngine doesn't change.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioConductor : MonoBehaviour
    {
        [Header("Mix")]
        [SerializeField, Range(0f, 1f)] float _masterGain = 0.45f;
        [SerializeField, Min(1)] int _maxVoices = 12;

        [Header("Amp envelope (seconds)")]
        [SerializeField, Range(0.001f, 1f)] float _attack = 0.008f;
        [SerializeField, Range(0.05f, 4f)] float _decay = 0.35f;
        [SerializeField, Range(0f, 1f)] float _sustain = 0.55f;
        [SerializeField, Range(0.02f, 4f)] float _release = 0.32f;

        [Header("Oscillators")]
        [SerializeField, Range(0f, 30f)] float _detuneCents = 4.5f;     // saw spread (tighter = poppier)
        [SerializeField, Range(0f, 1f)] float _sawLevel = 0.55f;        // mix level for the two saws
        [SerializeField, Range(0f, 1f)] float _pulseLevel = 0.65f;      // mix level for the pulse
        [SerializeField, Range(0.05f, 0.95f)] float _pulseWidth = 0.32f; // duty cycle for the pulse
        [SerializeField, Range(200f, 12000f)] float _cutoffHz = 4200f;   // low-pass cutoff (higher = brighter)

        [Header("Movement")]
        [SerializeField, Range(0f, 8f)] float _vibratoRateHz = 5.5f;
        [SerializeField, Range(0f, 0.01f)] float _vibratoDepth = 0.0018f;

        enum Stage { Idle, Attack, Decay, Sustain, Release }

        class Voice
        {
            public bool used;
            public int midi;
            public double p0, p1, p2;   // three saw phases, 0..1
            public double i0, i1, i2;   // per-sample phase increments
            public float amp;
            public float lp;
            public Stage stage;
        }

        Voice[] _voices;
        readonly object _lock = new();
        int _sampleRate;
        float _attCoef, _decCoef, _relCoef, _lpCoef;
        double _vibPhase, _vibInc;

        const double TAU = 2.0 * System.Math.PI;

        void Awake()
        {
            _sampleRate = AudioSettings.outputSampleRate;
            _voices = new Voice[Mathf.Max(1, _maxVoices)];
            for (int i = 0; i < _voices.Length; i++) _voices[i] = new Voice();
            RecomputeCoefs();

            var src = GetComponent<AudioSource>();
            src.clip = AudioClip.Create("ConductorSynth", _sampleRate, 1, _sampleRate, false);
            src.loop = true;
            src.spatialBlend = 0f;
            src.playOnAwake = true;
            src.Play();
        }

        void OnValidate() { if (_sampleRate > 0) RecomputeCoefs(); }

        void RecomputeCoefs()
        {
            _attCoef = CoefForTime(_attack);
            _decCoef = CoefForTime(_decay);
            _relCoef = CoefForTime(_release);
            float wc = 2f * Mathf.PI * _cutoffHz / _sampleRate;
            _lpCoef = Mathf.Clamp01(1f - Mathf.Exp(-wc));
            _vibInc = TAU * _vibratoRateHz / _sampleRate;
        }

        float CoefForTime(float seconds)
        {
            float t = Mathf.Max(0.0005f, seconds);
            return 1f - Mathf.Exp(-1f / (t * _sampleRate));
        }

        void OnEnable() => ServiceLocator.Register(this);
        void OnDisable() => ServiceLocator.Unregister<AudioConductor>();

        public void PlayChord(List<int> midiNotes)
        {
            if (midiNotes == null) return;
            lock (_lock)
                foreach (int n in midiNotes) NoteOnInternal(n);
        }

        public void NoteOn(int midi) { lock (_lock) NoteOnInternal(midi); }

        public void NoteOff(int midi)
        {
            lock (_lock)
                foreach (var v in _voices)
                    if (v.used && v.midi == midi && v.stage != Stage.Release)
                        v.stage = Stage.Release;
        }

        public void StopAll()
        {
            lock (_lock)
                foreach (var v in _voices)
                    if (v.used && v.stage != Stage.Release) v.stage = Stage.Release;
        }

        void NoteOnInternal(int midi)
        {
            Voice v = null;
            float quietest = float.MaxValue;
            foreach (var c in _voices)
            {
                if (!c.used) { v = c; break; }
                float w = c.stage == Stage.Release ? c.amp * 0.5f : c.amp;
                if (w < quietest) { quietest = w; v = c; }
            }

            double f = 440.0 * System.Math.Pow(2.0, (midi - 69) / 12.0);
            double semis = _detuneCents / 100.0;
            double up = System.Math.Pow(2.0, semis / 12.0);
            double dn = 1.0 / up;
            v.midi = midi;
            v.p0 = Random.value;
            v.p1 = Random.value;
            v.p2 = Random.value;
            v.i0 = (f * up) / _sampleRate;   // saw, detuned up
            v.i1 = (f * dn) / _sampleRate;   // saw, detuned down
            v.i2 = f / _sampleRate;          // pulse, on pitch (anchors the chord)
            v.amp = 0f;
            v.lp = 0f;
            v.stage = Stage.Attack;
            v.used = true;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (_voices == null) return;

            lock (_lock)
            {
                for (int s = 0; s < data.Length; s += channels)
                {
                    float vib = 1f + _vibratoDepth * Mathf.Sin((float)_vibPhase);
                    _vibPhase += _vibInc;
                    if (_vibPhase > TAU) _vibPhase -= TAU;

                    float mix = 0f;
                    foreach (var v in _voices)
                    {
                        if (!v.used) continue;

                        switch (v.stage)
                        {
                            case Stage.Attack:
                                v.amp += (1f - v.amp) * _attCoef;
                                if (v.amp >= 0.999f) { v.amp = 1f; v.stage = Stage.Decay; }
                                break;
                            case Stage.Decay:
                                v.amp += (_sustain - v.amp) * _decCoef;
                                if (Mathf.Abs(v.amp - _sustain) < 0.001f) { v.amp = _sustain; v.stage = Stage.Sustain; }
                                break;
                            case Stage.Sustain:
                                v.amp = _sustain;
                                break;
                            case Stage.Release:
                                v.amp += -v.amp * _relCoef;
                                if (v.amp < 0.0008f) { v.used = false; continue; }
                                break;
                        }

                        // Two detuned saws (body + chorus) + a pulse (the poly-synth bite).
                        float saw0 = (float)(v.p0 * 2.0 - 1.0);
                        float saw1 = (float)(v.p1 * 2.0 - 1.0);
                        float pulse = v.p2 < _pulseWidth ? 1f : -1f;
                        float raw = (saw0 + saw1) * (_sawLevel * 0.5f) + pulse * _pulseLevel;

                        v.lp += (raw - v.lp) * _lpCoef;

                        v.p0 += v.i0 * vib; if (v.p0 >= 1.0) v.p0 -= 1.0;
                        v.p1 += v.i1 * vib; if (v.p1 >= 1.0) v.p1 -= 1.0;
                        v.p2 += v.i2 * vib; if (v.p2 >= 1.0) v.p2 -= 1.0;

                        mix += v.lp * v.amp;
                    }

                    mix *= _masterGain * 0.25f;
                    mix = (float)System.Math.Tanh(mix);
                    for (int c = 0; c < channels; c++) data[s + c] = mix;
                }
            }
        }
    }
}
