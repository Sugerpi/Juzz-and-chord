using UnityEngine;
using UnityEngine.InputSystem;
using ChordPlayer.Core;

namespace ChordPlayer.Kinect
{
    /// <summary>
    /// Drives two virtual hands from mouse + keyboard so the entire game runs
    /// with no Kinect and no Asset Store packages. Implements the same
    /// <see cref="IBodyInputProvider"/> contract as the real wrapper, so wheels,
    /// audio and FX behave identically whether the body is real or simulated.
    ///
    /// Controls:
    ///   Mouse X            -> left-hand angle   (root selection later)
    ///   Mouse Y            -> right-hand angle  (quality selection later)
    ///   Left mouse button  -> left hand CLOSED  (trigger)
    ///   Right mouse button -> right hand CLOSED (trigger)
    /// </summary>
    public class SimulatedInputProvider : MonoBehaviour, IBodyInputProvider
    {
        [Header("Body layout (metres, relative to this transform)")]
        [SerializeField] Vector3 _spineBaseOffset = new(0f, 0.9f, 0f);
        [SerializeField] Vector3 _spineMidOffset = new(0f, 1.2f, 0f);
        [SerializeField] Vector3 _leftShoulderOffset = new(-0.18f, 1.4f, 0f);
        [SerializeField] Vector3 _rightShoulderOffset = new(0.18f, 1.4f, 0f);
        [SerializeField, Min(0.1f)] float _reach = 0.55f;

        [Header("Hand angle mapped from mouse (degrees, 0 = straight up)")]
        [SerializeField] float _minAngle = -120f;
        [SerializeField] float _maxAngle = 120f;

        BodyData _body = BodyData.Empty;
        public BodyData Body => _body;

        void OnEnable() => ServiceLocator.Register<IBodyInputProvider>(this);
        void OnDisable() => ServiceLocator.Unregister<IBodyInputProvider>();

        void Update()
        {
            var mouse = Mouse.current;
            Vector2 screen = mouse != null
                ? mouse.position.ReadValue()
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            float nx = Mathf.Clamp01(screen.x / Mathf.Max(1, Screen.width));
            float ny = Mathf.Clamp01(screen.y / Mathf.Max(1, Screen.height));

            Vector3 origin = transform.position;
            Vector3 leftShoulder = origin + _leftShoulderOffset;
            Vector3 rightShoulder = origin + _rightShoulderOffset;

            _body.IsTracked = true;
            _body.SpineBase = origin + _spineBaseOffset;
            _body.SpineMid = origin + _spineMidOffset;
            _body.ShoulderLeft = leftShoulder;
            _body.ShoulderRight = rightShoulder;

            _body.LeftHand = leftShoulder + HandOffset(Mathf.Lerp(_minAngle, _maxAngle, nx));
            _body.RightHand = rightShoulder + HandOffset(Mathf.Lerp(_minAngle, _maxAngle, ny));
            _body.LeftHandTracked = true;
            _body.RightHandTracked = true;

            bool leftClosed = mouse != null && mouse.leftButton.isPressed;
            bool rightClosed = mouse != null && mouse.rightButton.isPressed;
            _body.LeftHandState = leftClosed ? HandGesture.Closed : HandGesture.Open;
            _body.RightHandState = rightClosed ? HandGesture.Closed : HandGesture.Open;
        }

        // Hand sits on an arc of radius _reach in the frontal (XY) plane.
        Vector3 HandOffset(float angleDeg)
        {
            float r = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), Mathf.Cos(r), 0f) * _reach;
        }
    }
}
