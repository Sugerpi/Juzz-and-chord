using UnityEngine;
using TMPro;
using ChordPlayer.Core;
using ChordPlayer.Kinect;
using ChordPlayer.Music;

namespace ChordPlayer.UI
{
    /// <summary>Bottom HUD line: current state, selected chord, body-tracking status.</summary>
    public class StatusBarUI : MonoBehaviour
    {
        [SerializeField] TMP_Text _text;

        IBodyInputProvider _input;
        ChordEngine _engine;
        GameDirector _director;

        void Update()
        {
            if (_text == null) return;
            if (_input == null) ServiceLocator.TryGet(out _input);
            if (_engine == null) ServiceLocator.TryGet(out _engine);
            if (_director == null) ServiceLocator.TryGet(out _director);

            string state = _director != null ? _director.State.ToString() : "—";
            string chord = (_engine != null && _engine.Table != null)
                ? _engine.Table.GetDisplayName(_engine.Selection.RootIndex, _engine.Selection.QualityIndex)
                : "—";
            bool tracked = _input != null && _input.Body.IsTracked;

            _text.text = $"STATE  {state}      CHORD  {chord}      BODY  {(tracked ? "TRACKED" : "—")}";
        }
    }
}
