using UnityEngine;
using TMPro;
using ChordPlayer.Core;
using ChordPlayer.Music;

namespace ChordPlayer.Visuals
{
    /// <summary>
    /// World-space chord readout in the middle of the rig (Synthesia-style):
    /// big chord name + a smaller "root · quality" sub-line. Polls the
    /// ChordEngine so there are no event-ordering surprises.
    /// </summary>
    public class CenterDisplay : MonoBehaviour
    {
        [SerializeField] TMP_Text _main;
        [SerializeField] TMP_Text _sub;

        ChordEngine _engine;
        Chord _last = new(-2, -2);

        void Update()
        {
            if (_engine == null) ServiceLocator.TryGet(out _engine);
            if (_engine == null || _engine.Table == null) return;

            Chord sel = _engine.Selection;
            if (sel == _last) return;
            _last = sel;

            if (_main != null) _main.text = _engine.Table.GetDisplayName(sel.RootIndex, sel.QualityIndex);
            if (_sub != null)
                _sub.text = $"{_engine.Table.GetRootName(sel.RootIndex)}  -  {_engine.Table.GetQualityName(sel.QualityIndex)}";
        }
    }
}
