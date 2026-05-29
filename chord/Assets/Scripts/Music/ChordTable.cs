using System.Collections.Generic;
using UnityEngine;

namespace ChordPlayer.Music
{
    /// <summary>
    /// Editable definition of every root and quality. Roots map to a base MIDI
    /// note; qualities map to a set of semitone intervals from the root. Nothing
    /// here is hard-coded in script — tweak voicings/octaves in the Inspector.
    /// Create via: Assets ▸ Create ▸ Chord Player ▸ Chord Table.
    /// </summary>
    [CreateAssetMenu(fileName = "ChordTable", menuName = "Chord Player/Chord Table")]
    public class ChordTable : ScriptableObject
    {
        [System.Serializable]
        public struct RootDef
        {
            public string name;     // display name, e.g. "C"
            public int midiNote;    // base MIDI note (60 = middle C)
        }

        [System.Serializable]
        public struct QualityDef
        {
            public string name;     // selection name, e.g. "min7"
            public string symbol;   // suffix appended to root for display, e.g. "m7"
            public int[] intervals; // semitone offsets from root, e.g. 0,3,7,10
        }

        [SerializeField] RootDef[] _roots = DefaultRoots();
        [SerializeField] QualityDef[] _qualities = DefaultQualities();

        public int RootCount => _roots?.Length ?? 0;
        public int QualityCount => _qualities?.Length ?? 0;

        public string GetRootName(int i) => InRange(i, RootCount) ? _roots[i].name : "?";
        public string GetQualityName(int i) => InRange(i, QualityCount) ? _qualities[i].name : "?";
        public string GetQualitySymbol(int i) => InRange(i, QualityCount) ? _qualities[i].symbol : "?";
        public int[] GetIntervals(int i) => InRange(i, QualityCount) ? _qualities[i].intervals : System.Array.Empty<int>();

        /// <summary>Display name, e.g. "Cmaj7".</summary>
        public string GetDisplayName(int rootIndex, int qualityIndex) =>
            GetRootName(rootIndex) + GetQualitySymbol(qualityIndex);

        /// <summary>Fills <paramref name="notes"/> with MIDI note numbers. Returns the count.</summary>
        public int BuildNotes(int rootIndex, int qualityIndex, List<int> notes)
        {
            notes.Clear();
            if (!InRange(rootIndex, RootCount) || !InRange(qualityIndex, QualityCount)) return 0;
            int root = _roots[rootIndex].midiNote;
            int[] intervals = _qualities[qualityIndex].intervals;
            if (intervals == null) return 0;
            foreach (int semis in intervals) notes.Add(root + semis);
            return notes.Count;
        }

        static bool InRange(int i, int count) => i >= 0 && i < count;

        void Reset()
        {
            _roots = DefaultRoots();
            _qualities = DefaultQualities();
        }

        // ---- defaults (A–G roots in octave 3/4; the 8 qualities from the spec) ----
        static RootDef[] DefaultRoots() => new[]
        {
            new RootDef { name = "A", midiNote = 57 },
            new RootDef { name = "B", midiNote = 59 },
            new RootDef { name = "C", midiNote = 60 },
            new RootDef { name = "D", midiNote = 62 },
            new RootDef { name = "E", midiNote = 64 },
            new RootDef { name = "F", midiNote = 65 },
            new RootDef { name = "G", midiNote = 67 },
        };

        static QualityDef[] DefaultQualities() => new[]
        {
            new QualityDef { name = "maj",  symbol = "",     intervals = new[] { 0, 4, 7 } },
            new QualityDef { name = "min",  symbol = "m",    intervals = new[] { 0, 3, 7 } },
            new QualityDef { name = "sus2", symbol = "sus2", intervals = new[] { 0, 2, 7 } },
            new QualityDef { name = "sus4", symbol = "sus4", intervals = new[] { 0, 5, 7 } },
            new QualityDef { name = "7",    symbol = "7",    intervals = new[] { 0, 4, 7, 10 } },
            new QualityDef { name = "maj7", symbol = "maj7", intervals = new[] { 0, 4, 7, 11 } },
            new QualityDef { name = "min7", symbol = "m7",   intervals = new[] { 0, 3, 7, 10 } },
            new QualityDef { name = "dim",  symbol = "dim",  intervals = new[] { 0, 3, 6 } },
        };
    }
}
