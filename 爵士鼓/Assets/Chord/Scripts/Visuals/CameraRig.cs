using UnityEngine;
using ChordPlayer.Core;
using ChordPlayer.Kinect;

namespace ChordPlayer.Visuals
{
    /// <summary>
    /// Lightweight stand-in for Cinemachine's handheld noise + micro-parallax
    /// (no extra package). Adds gentle Perlin breathing to the camera and nudges
    /// it toward the midpoint of the hands. Put it on the Main Camera.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        [Header("Home pose")]
        [SerializeField] Vector3 _homePosition = new(0f, 1.4f, -2.2f);
        [SerializeField] Vector3 _homeEuler = new(5f, 0f, 0f);

        [Header("Handheld noise")]
        [SerializeField, Min(0f)] float _posAmplitude = 0.015f;
        [SerializeField, Min(0f)] float _rotAmplitude = 0.25f;
        [SerializeField, Min(0f)] float _noiseSpeed = 0.4f;

        [Header("Parallax to hand midpoint")]
        [SerializeField, Range(0f, 0.2f)] float _parallax = 0.05f;
        [SerializeField] float _bodyHeight = 1.4f;

        IBodyInputProvider _input;

        void LateUpdate()
        {
            float t = Time.time * _noiseSpeed;

            Vector3 noisePos = new(
                Mathf.PerlinNoise(t, 0.0f) - 0.5f,
                Mathf.PerlinNoise(0.0f, t) - 0.5f,
                Mathf.PerlinNoise(t, t) - 0.5f);
            noisePos *= 2f * _posAmplitude;

            Vector3 parallax = Vector3.zero;
            if (_input == null) ServiceLocator.TryGet(out _input);
            if (_input != null && _input.Body.IsTracked)
            {
                Vector3 mid = (_input.Body.LeftHand + _input.Body.RightHand) * 0.5f;
                parallax = new Vector3(mid.x * _parallax, (mid.y - _bodyHeight) * _parallax, 0f);
            }

            Vector3 noiseRot = new(
                Mathf.PerlinNoise(t, 5.0f) - 0.5f,
                Mathf.PerlinNoise(5.0f, t) - 0.5f,
                0f);
            noiseRot *= 2f * _rotAmplitude;

            transform.localPosition = _homePosition + noisePos + parallax;
            transform.localEulerAngles = _homeEuler + noiseRot;
        }
    }
}
