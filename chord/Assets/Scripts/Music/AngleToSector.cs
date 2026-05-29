using UnityEngine;

namespace ChordPlayer.Music
{
    /// <summary>
    /// Maps an angle (degrees) within [minAngle, maxAngle] to one of N sectors,
    /// with hysteresis so the selection doesn't flicker when a hand hovers on a
    /// sector boundary. Stateful — use one instance per tracked hand.
    /// </summary>
    [System.Serializable]
    public class AngleToSector
    {
        [SerializeField, Min(1)] int _sectorCount = 7;
        [SerializeField] float _minAngle = -120f;
        [SerializeField] float _maxAngle = 120f;
        [SerializeField, Min(0f),
         Tooltip("Degrees the angle must move PAST a boundary before the sector switches. " +
                 "Higher = stickier / less flicker.")]
        float _hysteresis = 5f;

        int _current = -1;

        public int SectorCount => _sectorCount;
        public int Current => _current;

        /// <summary>Force the sector count (e.g. to match the ChordTable). Resets state.</summary>
        public void Configure(int sectorCount)
        {
            _sectorCount = Mathf.Max(1, sectorCount);
            _current = -1;
        }

        public void Reset() => _current = -1;

        public int Evaluate(float angle)
        {
            float range = Mathf.Max(0.0001f, _maxAngle - _minAngle);
            float width = range / _sectorCount;
            float a = Mathf.Clamp(angle, _minAngle, _maxAngle - 0.0001f);

            // First sample: snap straight to the raw sector.
            if (_current < 0)
                return _current = RawSector(a, width);

            float low = _minAngle + _current * width;
            float high = low + width;

            // Stay put until the angle crosses the current sector's edge by _hysteresis.
            if (a >= low - _hysteresis && a < high + _hysteresis)
                return _current;

            return _current = RawSector(a, width);
        }

        int RawSector(float a, float width) =>
            Mathf.Clamp((int)((a - _minAngle) / width), 0, _sectorCount - 1);
    }
}
