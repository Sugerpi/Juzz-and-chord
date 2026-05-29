using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ChordPlayer.Kinect;
using ChordPlayer.Music;
using ChordPlayer.Visuals;

namespace ChordPlayer.EditorTools
{
    /// <summary>
    /// Wires the point-to-cover model: shrinks the wheels, tells the ChordEngine
    /// which wheel is root/quality, and gives the CalibrationController the wheels
    /// so it can snap them to your hands. Run once after Phase 3/4/6.
    /// Menu: Tools ▸ Chord Player ▸ Wire Hover Selection.
    /// </summary>
    static class WireSelectionSetup
    {
        [MenuItem("Tools/Chord Player/Wire Hover Selection")]
        static void Wire()
        {
            ChordWheel root = null, quality = null;
            foreach (var w in Object.FindObjectsByType<ChordWheel>(FindObjectsSortMode.None))
            {
                if (w.Kind == ChordWheel.WheelKind.Root) root = w;
                else quality = w;
            }

            var engine = Object.FindAnyObjectByType<ChordEngine>();
            if (engine == null || root == null || quality == null)
            {
                EditorUtility.DisplayDialog("Missing pieces",
                    "Need a ChordEngine plus a Root and a Quality wheel. Run Setup Phase 2 and 3 first.", "OK");
                return;
            }

            // Shrink the wheels so a hand can cover their segments with small moves.
            Shrink(root);
            Shrink(quality);

            var es = new SerializedObject(engine);
            es.FindProperty("_leftWheel").objectReferenceValue = root;
            es.FindProperty("_rightWheel").objectReferenceValue = quality;
            es.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(engine);

            var cc = Object.FindAnyObjectByType<CalibrationController>(FindObjectsInactive.Include);
            if (cc != null)
            {
                var cs = new SerializedObject(cc);
                cs.FindProperty("_leftWheel").objectReferenceValue = root;
                cs.FindProperty("_rightWheel").objectReferenceValue = quality;
                cs.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(cc);
            }

            EditorSceneManager.MarkSceneDirty(engine.gameObject.scene);
            Debug.Log("[ChordPlayer] Wired point-to-cover. Press Play, stand in front, hold your hands " +
                      "comfortably for ~2.5s (or press C) to snap the wheels onto your hands.");
        }

        static void Shrink(ChordWheel wheel)
        {
            var so = new SerializedObject(wheel);
            so.FindProperty("_innerRadius").floatValue = 0.14f;
            so.FindProperty("_outerRadius").floatValue = 0.34f;
            so.ApplyModifiedPropertiesWithoutUndo();
            wheel.Rebuild();
            EditorUtility.SetDirty(wheel);
        }
    }
}
