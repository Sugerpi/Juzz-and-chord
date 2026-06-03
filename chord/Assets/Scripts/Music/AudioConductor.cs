using System.Collections.Generic;
using UnityEngine;
using ChordPlayer.Core;

namespace ChordPlayer.Music
{
    /// <summary>
    /// Polyphonic detuned super-saw synth pad. Each voice = 3 sawtooth oscillators
    /// fanned out by ±_detuneCents for a thick analog chorus, passed through a
    /// one-pole low-pass and an ADSR amp envelope. Held while the gate is on,
    /// fades out on NoteOff / StopAll. Same PlayChord / NoteOn / NoteOff / StopAll
    /// surface, so ChordEngine doesn't change.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioConductor : MonoBehaviour
    {
        [Header("Mix")]
        [SerializeField, Range(0f, 1f)] float _masterGain = 0.45f;
        [SerializeField, Min(1)] int _maxVoices = 12;

        [Header("Amp envelope (seconds)")]
        [SerializeField, Range(0.001f, 1f)] float _attack = 0.03f;
        [SerializeField, Range(0.05f, 4f)] float _decay = 0.8f;
        [SerializeField, Range(0f, 1f)] float _sustain = 0.75f;
        [SerializeField, Range(0.02f, 4f)] float _release = 0.7f;

        [Header("Super-saw")]
        [SerializeField, Range(0f, 40f)] float _detuneCents = 9f;     // spread of the 3 saws
        [SerializeField, Range(200f, 12000f)] float _cutoffHz = 2400f; // low-pass cutoff
        [SerializeField, Range(0f, 1f)] float _mixSpread = 0.7f;       // 0=mono, 1=full detune mix

        [Header("Movement")]
        [SerializeField, Range(0f, 8f)] float _vibratoRateHz = 5f;
        [SerializeField, Range(0f, 0.01f)] float _vibratoDepth = 0.003f;

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
            // one-pole LP: cutoff = -ln(1-coef) * fs / (2π). Solve for coef.
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
            // Randomised start phases — keeps stacked voices from phase-cancelling.
            v.p0 = Random.value;
            v.p1 = Random.value;
            v.p2 = Random.value;
            v.i0 = f / _sampleRate;
            v.i1 = (f * up) / _sampleRate;
            v.i2 = (f * dn) / _sampleRate;
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
                float centerGain = Mathf.Lerp(1f, 0.55f, _mixSpread);
                float sideGain   = Mathf.Lerp(0f, 0.55f, _mixSpread);

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

                        // Three naive saws, antialias is fine for chord-range freqs.
                        float s0 = (float)(v.p0 * 2.0 - 1.0);
                        float s1 = (float)(v.p1 * 2.0 - 1.0);
                        float s2 = (float)(v.p2 * 2.0 - 1.0);
                        float raw = s0 * centerGain + (s1 + s2) * sideGain;

                        // One-pole LP per voice — tames the buzz, gives that analog warmth.
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
