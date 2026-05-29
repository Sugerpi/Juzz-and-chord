using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using ChordPlayer.Core;
using ChordPlayer.Visuals;

namespace ChordPlayer.Kinect
{
    /// <summary>
    /// Calibration + fault-tolerance prompt. Hold both hands where comfortable
    /// for a couple of seconds and each wheel snaps to that hand, so your hand
    /// sits right on the wheel and small movements cover the segments. Press C to
    /// redo. Also shows step-in / disconnected messages.
    ///
    /// Prompt strings are serialized so you can type any language in the Inspector
    /// without source-encoding issues.
    /// </summary>
    public class CalibrationController : MonoBehaviour
    {
        [SerializeField] TMP_Text _prompt;
        [SerializeField] ChordWheel _leftWheel;
        [SerializeField] ChordWheel _rightWheel;
        [SerializeField, Min(0.5f)] float _holdSeconds = 2.5f;

        [Header("Messages ({0} = countdown)")]
        [SerializeField] string _msgDisconnected = "Kinect not connected - check the cable";
        [SerializeField] string _msgStepIn = "Step in front of the camera (1.5 - 2.5 m)";
        [SerializeField] string _msgCalibrating = "Hold both hands where comfortable... {0}";
        [SerializeField] string _msgDone = "Set!";

        IBodyInputProvider _input;
        MicrosoftKinectInputProvider _kinect;
        float _timer;
        bool _done;
        float _doneFlash;

        void Update()
        {
            if (_input == null) ServiceLocator.TryGet(out _input);
            _kinect = _input as MicrosoftKinectInputProvider;

            bool isKinect = _kinect != null;
            bool tracked = _input != null && _input.Body.IsTracked;

            if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
            {
                _done = false; _timer = 0f;
            }

            if (_doneFlash > 0f) { _doneFlash -= Time.deltaTime; Show(_msgDone); return; }
            if (isKinect && !_kinect.SensorAvailable) { Show(_msgDisconnected); return; }

            if (!tracked) { Show(isKinect ? _msgStepIn : ""); _timer = 0f; return; }

            if (!_done)
            {
                _timer += Time.deltaTime;
                int remain = Mathf.Max(0, Mathf.CeilToInt(_holdSeconds - _timer));
                Show(string.Format(_msgCalibrating, remain));
                if (_timer >= _holdSeconds)
                {
                    CaptureCenters(_input.Body);
                    _done = true;
                    _doneFlash = 1.2f;
                }
                return;
            }

            Show("");
        }

        void CaptureCenters(BodyData body)
        {
            if (_leftWheel != null)
            {
                var p = _leftWheel.transform.position;
                _leftWheel.transform.position = new Vector3(body.LeftHand.x, body.LeftHand.y, p.z);
            }
            if (_rightWheel != null)
            {
                var p = _rightWheel.transform.position;
                _rightWheel.transform.position = new Vector3(body.RightHand.x, body.RightHand.y, p.z);
            }
        }

        void Show(string text)
        {
            if (_prompt == null) return;
            if (_prompt.text != text) _prompt.text = text;
            bool on = !string.IsNullOrEmpty(text);
            if (_prompt.gameObject.activeSelf != on) _prompt.gameObject.SetActive(on);
        }
    }
}
