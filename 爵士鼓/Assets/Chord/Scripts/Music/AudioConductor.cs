using System.Collections.Generic;
using UnityEngine;
using ChordPlayer.Core;

namespace ChordPlayer.Music
{
    /// <summary>
    /// Polyphonic plucked-string synth (Karplus-Strong with fractional-delay
    /// tuning, so chords are actually in tune). Each NoteOn plucks a string that
    /// rings and decays; releasing damps it fast. Generated in OnAudioFilterRead,
    /// so no MIDI/audio assets and low latency. Same PlayChord / NoteOn / NoteOff
    /// / StopAll surface, so ChordEngine doesn't change.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioConductor : MonoBehaviour
    {
        [Header("Mix")]
        [SerializeField, Range(0f, 1f)] float _masterGain = 0.5f;
        [SerializeField, Min(1)] int _maxVoices = 12;

        [Header("String tone")]
        [SerializeField, Range(0.90f, 0.9999f)] float _sustain = 0.996f;  // ring length
        [SerializeField, Range(0.5f, 0.99f)] float _muteDamp = 0.86f;     // damping after release
        [SerializeField, Range(0f, 0.95f)] float _pluckSoftness = 0.6f;   // 0 = bright, high = warm

        class Voice
        {
            public bool used, gate;
            public int midi, len, w;
            public float delay, lastLP, peak;
            public float[] buf;
        }

        Voice[] _voices;
        readonly object _lock = new();
        readonly System.Random _rng = new();
        int _sampleRate;
        int _maxLen;

        void Awake()
        {
            _sampleRate = AudioSettings.outputSampleRate;
            _maxLen = _sampleRate / 40 + 4;
            _voices = new Voice[Mathf.Max(1, _maxVoices)];
            for (int i = 0; i < _voices.Length; i++) _voices[i] = new Voice { buf = new float[_maxLen] };

            var src = GetComponent<AudioSource>();
            src.clip = AudioClip.Create("ConductorSynth", _sampleRate, 1, _sampleRate, false);
            src.loop = true;
            src.spatialBlend = 0f;
            src.playOnAwake = true;
            src.Play();
        }

        void OnEnable() => ServiceLocator.Register(this);
        void OnDisable() => ServiceLocator.Unregister<AudioConductor>();

        public void PlayChord(List<int> midiNotes)
        {
            if (midiNotes == null) return;
            lock (_lock)
                foreach (int n in midiNotes) Pluck(n);
        }

        public void NoteOn(int midi) { lock (_lock) Pluck(midi); }

        public void NoteOff(int midi)
        {
            lock (_lock)
                foreach (var v in _voices)
                    if (v.used && v.midi == midi) v.gate = false;
        }

        public void StopAll()
        {
            lock (_lock)
                foreach (var v in _voices) v.gate = false;
        }

        void Pluck(int midi)
        {
            Voice v = null;
            float lowest = float.MaxValue;
            foreach (var c in _voices)
            {
                if (!c.used) { v = c; break; }
                if (c.peak < lowest) { lowest = c.peak; v = c; }
            }

            double freq = 440.0 * System.Math.Pow(2.0, (midi - 69) / 12.0);
            v.delay = Mathf.Clamp((float)(_sampleRate / freq), 2f, _maxLen - 2);
            v.len = Mathf.Clamp(Mathf.CeilToInt(v.delay) + 1, 4, _maxLen);
            v.w = 0;
            v.lastLP = 0f;
            v.midi = midi;
            v.gate = true;
            v.used = true;
            v.peak = 1f;

            float prev = 0f;
            for (int i = 0; i < v.len; i++)
            {
                float n = (float)(_rng.NextDouble() * 2.0 - 1.0);
                prev = Mathf.Lerp(n, prev, _pluckSoftness);   // soften the pluck
                v.buf[i] = prev;
            }
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (_voices == null) return;

            lock (_lock)
            {
                foreach (var v in _voices) if (v.used) v.peak = 0f;

                for (int s = 0; s < data.Length; s += channels)
                {
                    float mix = 0f;
                    foreach (var v in _voices)
                    {
                        if (!v.used) continue;

                        // Fractional-delay read = correct pitch.
                        float pos = v.w - v.delay;
                        if (pos < 0f) pos += v.len;
                        int i0 = (int)pos;
                        float frac = pos - i0;
                        int i1 = i0 + 1; if (i1 >= v.len) i1 = 0;
                        float outp = v.buf[i0] * (1f - frac) + v.buf[i1] * frac;

                        float damp = v.gate ? _sustain : _sustain * _muteDamp;
                        float lp = 0.5f * (outp + v.lastLP);   // loop low-pass
                        v.lastLP = outp;
                        v.buf[v.w] = lp * damp;
                        v.w++; if (v.w >= v.len) v.w = 0;

                        float a = outp < 0f ? -outp : outp;
                        if (a > v.peak) v.peak = a;
                        mix += outp;
                    }

                    mix *= _masterGain * 0.35f;
                    mix = (float)System.Math.Tanh(mix);
                    for (int c = 0; c < channels; c++) data[s + c] = mix;
                }

                foreach (var v in _voices)
                    if (v.used && v.peak < 0.0008f) v.used = false;
            }
        }
    }
}
