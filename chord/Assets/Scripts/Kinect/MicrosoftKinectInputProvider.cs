using UnityEngine;
using ChordPlayer.Core;

namespace ChordPlayer.Kinect
{
    /// <summary>
    /// Free-route Kinect v2 wrapper using Microsoft's official Unity plugin
    /// (<c>Windows.Kinect</c>). Implements the same <see cref="IBodyInputProvider"/>
    /// contract as the simulator, so nothing downstream changes.
    ///
    /// It also maps raw sensor-space joints into the scene "rig" space (relative
    /// to the spine, scaled, anchored) so the hand orbs sit by the wheels instead
    /// of out at the sensor's 2 m depth. Because every joint uses the SAME
    /// transform, shoulder-relative angles (used for selection) are preserved.
    ///
    /// All SDK calls are behind the <c>MSKINECT</c> scripting define.
    /// </summary>
    public class MicrosoftKinectInputProvider : MonoBehaviour, IBodyInputProvider
    {
        [Header("Joint smoothing (one filter per tracked joint)")]
        [SerializeField] JointSmoother _leftHand = new();
        [SerializeField] JointSmoother _rightHand = new();
        [SerializeField] JointSmoother _spineMid = new();
        [SerializeField] JointSmoother _spineBase = new();
        [SerializeField] JointSmoother _shoulderLeft = new();
        [SerializeField] JointSmoother _shoulderRight = new();

        [Header("Mapping")]
        [Tooltip("Mirror left/right so it reads like a mirror when facing the sensor.")]
        [SerializeField] bool _mirrorX = true;
        [Tooltip("Map sensor metres into scene rig space (orbs sit by the wheels).")]
        [SerializeField] bool _mapToRig = true;
        [SerializeField] Vector3 _rigOrigin = new(0f, 1.4f, -0.3f);   // where the spine maps to
        [SerializeField] float _sceneScale = 1.6f;                    // scene metres per body metre
        [SerializeField, Range(0f, 1f)] float _depthScale = 0.4f;     // compress sensor depth

        BodyData _body = BodyData.Empty;
        public BodyData Body => _body;

        /// <summary>Live shoulder→hand distance in raw sensor metres (for calibration).</summary>
        public float CurrentArmLength { get; private set; }

        /// <summary>Calibrate so an arm of <paramref name="armLengthSensor"/> metres reaches
        /// <paramref name="sceneReach"/> metres in the scene.</summary>
        public void ApplyCalibration(float armLengthSensor, float sceneReach)
        {
            if (armLengthSensor > 0.05f) _sceneScale = sceneReach / armLengthSensor;
        }

        // Full mapped skeleton (Kinect JointType index 0..24 → scene position) for SkeletonView.
        public Vector3[] JointsScene { get; } = new Vector3[25];
        public bool[] JointTracked { get; } = new bool[25];
        public bool BodyTracked { get; private set; }

        void OnEnable() => ServiceLocator.Register<IBodyInputProvider>(this);
        void OnDisable()
        {
            ServiceLocator.Unregister<IBodyInputProvider>();
            Shutdown();
        }

#if MSKINECT
        Windows.Kinect.KinectSensor _sensor;
        Windows.Kinect.BodyFrameReader _reader;
        Windows.Kinect.Body[] _bodies;
        bool _started;

        /// <summary>True while the sensor object is available (for the disconnect HUD).</summary>
        public bool SensorAvailable => _sensor != null && _sensor.IsAvailable;

        void Start()
        {
            _sensor = Windows.Kinect.KinectSensor.GetDefault();
            if (_sensor == null)
            {
                Debug.LogWarning("[Kinect] No sensor found (check adapter / SDK 2.0 / Kinect plugged in).", this);
                return;
            }
            _reader = _sensor.BodyFrameSource.OpenReader();
            if (!_sensor.IsOpen) _sensor.Open();
            _started = true;
        }

        void Update()
        {
            if (!_started || _reader == null) { _body.IsTracked = false; BodyTracked = false; return; }

            using (var frame = _reader.AcquireLatestFrame())
            {
                if (frame != null)
                {
                    _bodies ??= new Windows.Kinect.Body[_sensor.BodyFrameSource.BodyCount];
                    frame.GetAndRefreshBodyData(_bodies);
                }
            }

            Windows.Kinect.Body body = null;
            if (_bodies != null)
                foreach (var b in _bodies)
                    if (b != null && b.IsTracked) { body = b; break; }

            if (body == null) { _body.IsTracked = false; BodyTracked = false; return; }
            _body.IsTracked = true;
            BodyTracked = true;
            float dt = Time.deltaTime;

            // Read + smooth raw (sensor-space, no mirror) joints first.
            Vector3 handL = ReadRaw(body, Windows.Kinect.JointType.HandLeft, _leftHand, dt, out bool tL);
            Vector3 handR = ReadRaw(body, Windows.Kinect.JointType.HandRight, _rightHand, dt, out bool tR);
            Vector3 spineM = ReadRaw(body, Windows.Kinect.JointType.SpineMid, _spineMid, dt, out _);
            Vector3 spineB = ReadRaw(body, Windows.Kinect.JointType.SpineBase, _spineBase, dt, out _);
            Vector3 shL = ReadRaw(body, Windows.Kinect.JointType.ShoulderLeft, _shoulderLeft, dt, out _);
            Vector3 shR = ReadRaw(body, Windows.Kinect.JointType.ShoulderRight, _shoulderRight, dt, out _);

            CurrentArmLength = Mathf.Max(Vector3.Distance(shL, handL), Vector3.Distance(shR, handR));

            _body.LeftHandTracked = tL;
            _body.RightHandTracked = tR;
            _body.LeftHand = Place(handL, spineM);
            _body.RightHand = Place(handR, spineM);
            _body.SpineMid = Place(spineM, spineM);
            _body.SpineBase = Place(spineB, spineM);
            _body.ShoulderLeft = Place(shL, spineM);
            _body.ShoulderRight = Place(shR, spineM);

            _body.LeftHandState = ToGesture(body.HandLeftState);
            _body.RightHandState = ToGesture(body.HandRightState);

            FillSkeleton(body, spineM);
        }

        bool _skeletonInit;
        void FillSkeleton(Windows.Kinect.Body body, Vector3 spine)
        {
            foreach (var kv in body.Joints)
            {
                int i = (int)kv.Key;
                if (i < 0 || i >= JointsScene.Length) continue;
                JointTracked[i] = kv.Value.TrackingState != Windows.Kinect.TrackingState.NotTracked;
                var pos = kv.Value.Position;
                Vector3 mapped = Place(new Vector3(pos.X, pos.Y, pos.Z), spine);
                JointsScene[i] = _skeletonInit ? Vector3.Lerp(JointsScene[i], mapped, 0.5f) : mapped;
            }
            _skeletonInit = true;
        }

        static Vector3 ReadRaw(Windows.Kinect.Body body, Windows.Kinect.JointType type,
            JointSmoother smoother, float dt, out bool tracked)
        {
            var joint = body.Joints[type];
            tracked = joint.TrackingState != Windows.Kinect.TrackingState.NotTracked;
            var p = new Vector3(joint.Position.X, joint.Position.Y, joint.Position.Z);
            if (tracked) return smoother.Update(p, dt);
            smoother.Reset();
            return smoother.Value;
        }

        // Sensor metres → scene rig space (relative to the spine, mirrored, scaled).
        Vector3 Place(Vector3 raw, Vector3 spine)
        {
            Vector3 rel = raw - spine;
            if (_mirrorX) rel.x = -rel.x;
            if (!_mapToRig) return rel;
            return _rigOrigin + new Vector3(rel.x * _sceneScale, rel.y * _sceneScale, rel.z * _sceneScale * _depthScale);
        }

        static HandGesture ToGesture(Windows.Kinect.HandState state) => state switch
        {
            Windows.Kinect.HandState.Open => HandGesture.Open,
            Windows.Kinect.HandState.Closed => HandGesture.Closed,
            Windows.Kinect.HandState.Lasso => HandGesture.Lasso,
            Windows.Kinect.HandState.NotTracked => HandGesture.NotTracked,
            _ => HandGesture.Unknown
        };

        void Shutdown()
        {
            if (_reader != null) { _reader.Dispose(); _reader = null; }
            if (_sensor != null) { if (_sensor.IsOpen) _sensor.Close(); _sensor = null; }
            _started = false;
        }
#else
        public bool SensorAvailable => false;
        bool _warned;

        void Update()
        {
            _body.IsTracked = false;
            if (_warned) return;
            _warned = true;
            Debug.LogWarning(
                "[Kinect] MSKINECT is not defined — Microsoft Kinect plugin not compiled in. " +
                "Import the Kinect Unity packages and add MSKINECT to Scripting Define Symbols, " +
                "or keep using SimulatedInputProvider.", this);
        }

        void Shutdown() { }
#endif
    }
}
