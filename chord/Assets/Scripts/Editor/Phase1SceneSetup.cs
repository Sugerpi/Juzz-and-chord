using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ChordPlayer.Kinect;

namespace ChordPlayer.EditorTools
{
    /// <summary>
    /// One-click Phase 1 scene builder so you can just press Play.
    /// Menu: Tools ▸ Chord Player ▸ Setup Phase 1 Scene.
    /// Re-running it cleanly rebuilds the rig. Delete the
    /// "ChordPlayer (Phase 1)" object to remove everything.
    /// </summary>
    static class Phase1SceneSetup
    {
        const string RootName = "ChordPlayer (Phase 1)";

        [MenuItem("Tools/Chord Player/Setup Phase 1 Scene")]
        static void Setup()
        {
            // Clean rebuild if it already exists.
            var existing = GameObject.Find(RootName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Setup Phase 1");
            root.transform.position = Vector3.zero;
            root.AddComponent<SimulatedInputProvider>();
            var viz = root.AddComponent<HandDebugVisualizer>();

            var left = MakeSphere("LeftHandSphere", root.transform, new Color(0.659f, 0.333f, 0.969f)); // purple
            var right = MakeSphere("RightHandSphere", root.transform, new Color(0.024f, 0.714f, 0.831f)); // cyan

            // Wire the visualizer's private serialized fields.
            var so = new SerializedObject(viz);
            so.FindProperty("_leftHand").objectReferenceValue = left.transform;
            so.FindProperty("_rightHand").objectReferenceValue = right.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Camera per spec: FOV 50, slightly above and behind, tilted ~5°.
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
                Undo.RegisterCreatedObjectUndo(camGo, "Setup Phase 1");
            }
            cam.transform.SetPositionAndRotation(
                new Vector3(0f, 1.4f, -2.2f), Quaternion.Euler(5f, 0f, 0f));
            cam.fieldOfView = 50f;

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(cam.gameObject.scene);
            Debug.Log("[ChordPlayer] Phase 1 scene ready — press Play and move the mouse. " +
                      "(Mouse X = left hand, Mouse Y = right hand, click = fist.)");
        }

        static GameObject MakeSphere(string name, Transform parent, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * 0.08f;

            if (go.TryGetComponent<Collider>(out var col)) Object.DestroyImmediate(col);

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(mr.sharedMaterial) { color = color };
            return go;
        }
    }
}
