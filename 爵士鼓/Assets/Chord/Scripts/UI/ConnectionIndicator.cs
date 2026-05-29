using UnityEngine;
using UnityEngine.UI;
using ChordPlayer.Core;
using ChordPlayer.Kinect;

namespace ChordPlayer.UI
{
    /// <summary>Top-right dot: green when a body is tracked, dim grey otherwise.</summary>
    public class ConnectionIndicator : MonoBehaviour
    {
        [SerializeField] Graphic _dot;
        [SerializeField] Color _connected = new(0.06f, 0.72f, 0.50f, 1f);
        [SerializeField] Color _lost = new(0.40f, 0.40f, 0.45f, 0.5f);

        IBodyInputProvider _input;

        void Update()
        {
            if (_dot == null) return;
            if (_input == null) ServiceLocator.TryGet(out _input);
            _dot.color = (_input != null && _input.Body.IsTracked) ? _connected : _lost;
        }
    }
}
