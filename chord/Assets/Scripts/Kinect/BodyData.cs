using UnityEngine;

namespace ChordPlayer.Kinect
{
    /// <summary>SDK-agnostic hand gesture. Maps from RFilkov's KinectInterop.HandState.</summary>
    public enum HandGesture
    {
        Unknown = 0,
        Open = 1,
        Closed = 2,
        Lasso = 3,
        NotTracked = 4
    }

    /// <summary>
    /// Plain snapshot of the tracked body for one frame, in Unity world space.
    /// This is the only data the rest of the game sees — no Microsoft.Kinect or
    /// RFilkov types are allowed past <see cref="KinectInputProvider"/>.
    /// Value type so consumers get a cheap, immutable copy with no GC churn.
    /// </summary>
    public struct BodyData
    {
        public bool IsTracked;          // a primary user is currently tracked

        public Vector3 LeftHand;
        public Vector3 RightHand;
        public Vector3 SpineMid;
        public Vector3 SpineBase;
        public Vector3 ShoulderLeft;
        public Vector3 ShoulderRight;

        public bool LeftHandTracked;
        public bool RightHandTracked;

        public HandGesture LeftHandState;
        public HandGesture RightHandState;

        public static BodyData Empty => new BodyData { IsTracked = false };
    }
}
