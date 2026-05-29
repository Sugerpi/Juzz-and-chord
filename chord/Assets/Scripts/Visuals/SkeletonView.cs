using UnityEngine;
using UnityEngine.Rendering;
using ChordPlayer.Core;
using ChordPlayer.Kinect;

namespace ChordPlayer.Visuals
{
    /// <summary>
    /// Draws the tracked Kinect skeleton as glowing bones, in the same rig space
    /// as the hand orbs (so it lines up). Kinect-only — does nothing on the mouse
    /// simulator (which has no full skeleton).
    /// </summary>
    public class SkeletonView : MonoBehaviour
    {
        // Kinect v2 bone pairs, as JointType integers (0..24).
        static readonly int[] Bones =
        {
            0,1,  1,20, 20,2, 2,3,                 // spine + head
            20,4, 4,5,  5,6,  6,7,  7,21, 6,22,    // left arm
            20,8, 8,9,  9,10, 10,11, 11,23, 10,24, // right arm
            0,12, 12,13, 13,14, 14,15,             // left leg
            0,16, 16,17, 17,18, 18,19              // right leg
        };

        [SerializeField] Material _lineMaterial;
        [SerializeField] Color _color = new(0.4f, 0.95f, 1f, 1f);
        [SerializeField, Min(0.001f)] float _width = 0.022f;

        MicrosoftKinectInputProvider _kinect;
        LineRenderer[] _lines;

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
            if (_kinect == null && ServiceLocator.TryGet<IBodyInputProvider>(out var p))
                _kinect = p as MicrosoftKinectInputProvider;

            bool show = _kinect != null && _kinect.BodyTracked;

            for (int i = 0; i < _lines.Length; i++)
            {
                int a = Bones[i * 2], b = Bones[i * 2 + 1];
                bool ok = show && _kinect.JointTracked[a] && _kinect.JointTracked[b];
                if (_lines[i].enabled != ok) _lines[i].enabled = ok;
                if (!ok) continue;
                _lines[i].SetPosition(0, _kinect.JointsScene[a]);
                _lines[i].SetPosition(1, _kinect.JointsScene[b]);
            }
        }
    }
}
