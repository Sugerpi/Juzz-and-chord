namespace ChordPlayer.Kinect
{
    /// <summary>
    /// The single contract the rest of the game depends on for body input.
    /// Implemented by <see cref="KinectInputProvider"/> (real hardware) and
    /// <see cref="SimulatedInputProvider"/> (mouse/keyboard). Consumers resolve
    /// this interface via the ServiceLocator and never know which is live.
    /// </summary>
    public interface IBodyInputProvider
    {
        /// <summary>Latest body snapshot in Unity world space. Read in LateUpdate or later.</summary>
        BodyData Body { get; }
    }
}
