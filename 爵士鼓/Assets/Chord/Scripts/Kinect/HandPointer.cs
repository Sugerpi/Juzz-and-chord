using UnityEngine;

namespace ChordPlayer.Kinect
{
    /// <summary>
    /// Shared math for "point with your arm" control. Produces a 2D direction
    /// (arm direction in the frontal plane) and a 0..1 reach, normalised by
    /// shoulder width so it works the same for any body size or input provider
    /// (no per-provider scale needed). Used by both ChordEngine (selection) and
    /// HandIndicator (cursor placement) so they always agree.
    /// </summary>
    public static class HandPointer
    {
        /// <summary>Amplifies arm-angle → cursor-angle so a small sweep covers more of
        /// the wheel (set by ChordEngine each frame). 1 = direct, higher = less waving.</summary>
        public static float AngleGain = 1.8f;

        /// <param name="dir">Unit arm direction in the XY plane (0,1 = up).</param>
        /// <param name="reach01">0 = hand at shoulder, ~1 = arm fully extended.</param>
        /// <returns>True if that hand is tracked this frame.</returns>
        public static bool Compute(in BodyData body, bool left, out Vector2 dir, out float reach01)
        {
            Vector3 hand = left ? body.LeftHand : body.RightHand;
            Vector3 shoulder = left ? body.ShoulderLeft : body.ShoulderRight;

            Vector2 v = new Vector2(hand.x - shoulder.x, hand.y - shoulder.y);

            float shoulderWidth = Mathf.Max(0.05f, Vector3.Distance(body.ShoulderLeft, body.ShoulderRight));
            float referenceArm = shoulderWidth * 1.7f;   // ~ a fully extended arm (frontal)
            reach01 = Mathf.Clamp01(v.magnitude / referenceArm);

            if (v.sqrMagnitude > 1e-6f)
            {
                float ang = Mathf.Atan2(v.x, v.y) * AngleGain;   // 0 = up; amplify the sweep
                dir = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang));
            }
            else dir = Vector2.up;

            return body.IsTracked && (left ? body.LeftHandTracked : body.RightHandTracked);
        }
    }
}
