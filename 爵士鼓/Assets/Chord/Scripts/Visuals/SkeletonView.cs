using UnityEngine;
using UnityEngine.Rendering;
using ChordPlayer.Core;
using ChordPlayer.Kinect;

namespace ChordPlayer.Visuals
{
    /// <summary>
    /// Draws a simplified skeleton from the 8 joints exposed by <see cref="BodyData"/>.
    /// The original chord project drew the full 25-joint Kinect skeleton via the
    /// MicrosoftKinectInputProvider's per-joint arrays; in this integration we only
    /// ship 8 joints over UDP, so this is a lower-fidelity stick figure. It is still
    /// enough to make the chord player visible on stage.
    ///
    /// Bones drawn (each pair = one LineRenderer):
    ///   spineBase ↔ spineMid          (lower spine)
    ///   spineMid  ↔ shoulderLeft      (left clavicle)
    ///   spineMid  ↔ shoulderRight     (right clavicle)
    ///   shoulderLeft  ↔ handLeft      (left arm, no elbow)
    ///   shoulderRight ↔ handRight     (right arm, no elbow)
    ///
    /// Legs are intentionally omitted because chord's BodyData has no foot joints —
    /// the chord player interacts with hands only.
    /// </summary>
    public class SkeletonView : MonoBehaviour
    {
        enum Joint { SpineBase, SpineMid, ShoulderL, ShoulderR, HandL, HandR }

        static readonly int[] Bones =
        {
            (int)Joint.SpineBase,  (int)Joint.SpineMid,
            (int)Joint.SpineMid,   (int)Joint.ShoulderL,
            (int)Joint.SpineMid,   (int)Joint.ShoulderR,
            (int)Joint.ShoulderL,  (int)Joint.HandL,
            (int)Joint.ShoulderR,  (int)Joint.HandR,
        };

        [SerializeField] Material _lineMaterial;
        [SerializeField] Color _color = new(0.4f, 0.95f, 1f, 1f);
        [SerializeField, Min(0.001f)] float _width = 0.022f;

        IBodyInputProvider _input;
        LineRenderer[] _lines;
        readonly Vector3[] _joints = new Vector3[6];

        void Awake()
        {
            int count = Bones.Length / 2;
            _lines = new LineRenderer[count];
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject($"Bone {i}");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.numCapVertices = 2;
                lr.startWidth = lr.endWidth = _width;
                lr.sharedMaterial = _lineMaterial;
                lr.startColor = lr.endColor = _color;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.enabled = false;
                _lines[i] = lr;
            }
        }

        void LateUpdate()
        {
            if (_input == null) ServiceLocator.TryGet(out _input);

            bool show = _input != null && _input.Body.IsTracked;
            if (!show)
            {
                for (int i = 0; i < _lines.Length; i++)
                    if (_lines[i].enabled) _lines[i].enabled = false;
                return;
            }

            BodyData body = _input.Body;
            _joints[(int)Joint.SpineBase] = body.SpineBase;
            _joints[(int)Joint.SpineMid]  = body.SpineMid;
            _joints[(int)Joint.ShoulderL] = body.ShoulderLeft;
            _joints[(int)Joint.ShoulderR] = body.ShoulderRight;
            _joints[(int)Joint.HandL]     = body.LeftHand;
            _joints[(int)Joint.HandR]     = body.RightHand;

            for (int i = 0; i < _lines.Length; i++)
            {
                int a = Bones[i * 2], b = Bones[i * 2 + 1];
                if (!_lines[i].enabled) _lines[i].enabled = true;
                _lines[i].SetPosition(0, _joints[a]);
                _lines[i].SetPosition(1, _joints[b]);
            }
        }
    }
}
