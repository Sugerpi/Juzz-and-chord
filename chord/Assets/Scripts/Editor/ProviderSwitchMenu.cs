using UnityEditor;
using UnityEngine;
using ChordPlayer.Kinect;

namespace ChordPlayer.EditorTools
{
    /// <summary>
    /// Switches the active body input between the mouse Simulator and the real
    /// Microsoft Kinect, keeping exactly one enabled. (Still add the MSKINECT
    /// scripting define after importing the Kinect plugin, or the Kinect path
    /// just warns and tracks nothing.)
    /// </summary>
    static class ProviderSwitchMenu
    {
        [MenuItem("Tools/Chord Player/Input/Use Kinect (Microsoft)")]
        static void UseKinect()
        {
            var (root, sim) = Resolve();
            if (root == null) return;
            if (sim != null) sim.enabled = false;

            var kinect = root.GetComponent<MicrosoftKinectInputProvider>();
            if (kinect == null) kinect = root.AddComponent<MicrosoftKinectInputProvider>();
            kinect.enabled = true;

            EditorUtility.SetDirty(root);
            Debug.Log("[ChordPlayer] Input → Microsoft Kinect. " +
                      "Make sure MSKINECT is in Scripting Define Symbols and the sensor is connected.");
        }

        [MenuItem("Tools/Chord Player/Input/Use Simulator (mouse)")]
        static void UseSimulator()
        {
            var (root, sim) = Resolve();
            if (root == null) return;

            var kinect = root.GetComponent<MicrosoftKinectInputProvider>();
            if (kinect != null) kinect.enabled = false;

            if (sim == null) sim = root.AddComponent<SimulatedInputProvider>();
            sim.enabled = true;

            EditorUtility.SetDirty(root);
            Debug.Log("[ChordPlayer] Input → Simulator (mouse).");
        }

        static (GameObject root, SimulatedInputProvider sim) Resolve()
        {
            var sim = Object.FindAnyObjectByType<SimulatedInputProvider>(FindObjectsInactive.Include);
            if (sim != null) return (sim.transform.root.gameObject, sim);

            var kinect = Object.FindAnyObjectByType<MicrosoftKinectInputProvider>(FindObjectsInactive.Include);
            if (kinect != null) return (kinect.transform.root.gameObject, null);

            EditorUtility.DisplayDialog("No input provider found",
                "Run Setup Phase 1 first so there's a Systems object to attach the provider to.", "OK");
            return (null, null);
        }
    }
}
