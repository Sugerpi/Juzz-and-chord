namespace ChordPlayer.Music
{
    /// <summary>
    /// A selected chord: an index into a <see cref="ChordTable"/>'s roots and
    /// qualities. Resolving to actual MIDI notes / display names is the table's
    /// job, so this stays a tiny value type.
    /// </summary>
    public readonly struct Chord : System.IEquatable<Chord>
    {
        public readonly int RootIndex;
        public readonly int QualityIndex;

        public Chord(int rootIndex, int qualityIndex)
        {
            RootIndex = rootIndex;
            QualityIndex = qualityIndex;
        }

        public bool Equals(Chord other) =>
            RootIndex == other.RootIndex && QualityIndex == other.QualityIndex;

        public override bool Equals(object obj) => obj is Chord c && Equals(c);
        public override int GetHashCode() => (RootIndex * 397) ^ QualityIndex;
        public static bool operator ==(Chord a, Chord b) => a.Equals(b);
        public static bool operator !=(Chord a, Chord b) => !a.Equals(b);
        public override string ToString() => $"Chord(r{RootIndex}, q{QualityIndex})";
    }
}
