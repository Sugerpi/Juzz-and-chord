using UnityEngine;
using ChordPlayer.Core;

namespace ChordPlayer.Kinect
{
    /// <summary>
    /// Wraps RFilkov's <c>KinectManager</c> and exposes a clean, SDK-agnostic
    /// <see cref="BodyData"/> snapshot every frame. This is the ONLY file in the
    /// project that references the Kinect SDK.
    ///
    /// IMPORTANT: all SDK calls are behind the <c>RFILKOV_KINECT</c> scripting
    /// define so the project compiles and runs WITHOUT the asset. After you
    /// import "Kinect v2 Examples with MS-SDK", add <c>RFILKOV_KINECT</c> to
    /// Project Settings ▸ Player ▸ Other Settings ▸ Scripting Define Symbols.
    ///
    /// API NOTE (verify against your installed RFilkov version):
    ///   KinectManager.Instance                            -> singleton
    ///   .IsInitialized()                                  -> bool
    ///   .GetPrimaryUserID()                               -> long (0 = none)
    ///   .IsJointTracked(id, jInt)                         -> bool
    ///   .GetJointPosition(id, jInt)                       -> Vector3 (Unity world space)
    ///   .GetLeftHandState(id) / .GetRightHandState(id)    -> KinectInterop.HandState
    /// If your version differs, only this file needs touching.
    /// </summary>
    public class KinectInputProvider : MonoBehaviour, IBodyInputProvider
    {
        [Header("Joint smoothing (one filter per tracked joint)")]
        [SerializeField] JointSmoother _leftHand = new();
        [SerializeField] JointSmoother _rightHand = new();
        [SerializeField] JointSmoother _spineMid = new();
        [SerializeField] JointSmoother _spineBase = new();
        [SerializeField] JointSmoother _shoulderLeft = new();
        [SerializeField] JointSmoother _shoulderRight = new();

        [Header("Debug (HandDebugVisualizer already logs; this is extra hardware-side logging)")]
        [SerializeField] bool _logToConsole = false;
        [SerializeField, Min(0.05f)] float _logInterval = 0.5f;

        BodyData _body = BodyData.Empty;
        float _nextLogTime;
        bool _warnedNoSdk;

        public BodyData Body => _body;

        void OnEnable() => ServiceLocator.Register<IBodyInputProvider>(this);
        void OnDisable() => ServiceLocator.Unregister<IBodyInputProvider>();

#if RFILKOV_KINECT
        void Update()
        {
            var km = KinectManager.Instance;
            if (km == null || !km.IsInitialized())
            {
                _body.IsTracked = false;
                return;
            }

            long userId = km.GetPrimaryUserID();
            if (userId == 0)
            {
                _body.IsTracked = false;
                return;
            }

            _body.IsTracked = true;
            float dt = Time.deltaTime;

            ReadJoint(km, userId, KinectInterop.JointType.HandLeft, _leftHand, dt,
                out _body.LeftHand, out _body.LeftHandTracked);
            ReadJoint(km, userId, KinectInterop.JointType.HandRight, _rightHand, dt,
                out _body.RightHand, out _body.RightHandTracked);
            ReadJoint(km, userId, KinectInterop.JointType.SpineMid, _spineMid, dt,
                out _body.SpineMid, out _);
            ReadJoint(km, userId, KinectInterop.JointType.SpineBase, _spineBase, dt,
                out _body.SpineBase, out _);
            ReadJoint(km, userId, KinectInterop.JointType.ShoulderLeft, _shoulderLeft, dt,
                out _body.ShoulderLeft, out _);
            ReadJoint(km, userId, KinectInterop.JointType.ShoulderRight, _shoulderRight, dt,
                out _body.ShoulderRight, out _);

            _body.LeftHandState = ToGesture(km.GetLeftHandState(userId));
            _body.RightHandState = ToGesture(km.GetRightHandState(userId));

            LogIfDue();
        }

        static void ReadJoint(KinectManager km, long userId, KinectInterop.JointType joint,
            JointSmoother smoother, float dt, out Vector3 position, out bool tracked)
        {
            int j = (int)joint;
            tracked = km.IsJointTracked(userId, j);
            if (tracked)
            {
                position = smoother.Update(km.GetJointPosition(userId, j), dt);
            }
            else
            {
                smoother.Reset();      // next tracked frame snaps in, no stale lag
                position = smoother.Value;
            }
        }

        static HandGesture ToGesture(KinectInterop.HandState state) => state switch
        {
            KinectInterop.HandState.Open => HandGesture.Open,
            KinectInterop.HandState.Closed => HandGesture.Closed,
            KinectInterop.HandState.Lasso => HandGesture.Lasso,
            KinectInterop.HandState.NotTracked => HandGesture.NotTracked,
            _ => HandGesture.Unknown
        };

        void LogIfDue()
        {
            if (!_logToConsole || Time.time < _nextLogTime) return;
            _nextLogTime = Time.time + _logInterval;
            Debug.Log(
                $"[Kinect] L {_body.LeftHand:F2} ({_body.LeftHandState})  " +
                $"R {_body.RightHand:F2} ({_body.RightHandState})");
        }
#else
        void Update()
        {
            _body.IsTracked = false;
            if (_warnedNoSdk) return;
            _warnedNoSdk = true;
            Debug.LogWarning(
                "[Kinect] RFILKOV_KINECT is not defined — Kinect SDK not compiled in. " +
                "Import the RFilkov asset and add RFILKOV_KINECT to Scripting Define Symbols, " +
                "or use SimulatedInputProvider for now.", this);
        }
#endif
    }
}
