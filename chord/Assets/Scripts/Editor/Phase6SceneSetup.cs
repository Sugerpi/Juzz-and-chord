using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using ChordPlayer.Kinect;

namespace ChordPlayer.EditorTools
{
    /// <summary>
    /// Phase 6: adds the calibration / presence / disconnect prompt (a world-space
    /// TMP above the wheels) and a CalibrationController. Run after Phase 4.
    /// Menu: Tools ▸ Chord Player ▸ Setup Phase 6 (calibration + prompts).
    /// </summary>
    static class Phase6SceneSetup
    {
        [MenuItem("Tools/Chord Player/Setup Phase 6 (calibration + prompts)")]
        static void Setup()
        {
            if (TMP_Settings.defaultFontAsset == null)
            {
                EditorUtility.DisplayDialog("Import TMP Essentials first",
                    "Do Window ▸ TextMeshPro ▸ Import TMP Essential Resources, then run again.", "OK");
                return;
            }

            var sim = Object.FindAnyObjectByType<SimulatedInputProvider>(FindObjectsInactive.Include);
            var kin = Object.FindAnyObjectByType<MicrosoftKinectInputProvider>(FindObjectsInactive.Include);
            Transform root = sim != null ? sim.transform.root : (kin != null ? kin.transform.root : null);
            if (root == null)
            {
                EditorUtility.DisplayDialog("Run earlier phases first",
                    "No input provider found. Run Setup Phase 1–4 first.", "OK");
                return;
            }

            // world-space prompt above the wheels
            var promptGo = FindChild(root, "Prompt");
            if (promptGo == null) { promptGo = new GameObject("Prompt"); promptGo.transform.SetParent(root, false); }
            promptGo.transform.localPosition = new Vector3(0f, 2.15f, 0f);
            promptGo.transform.localRotation = Quaternion.identity;

            var tmp = promptGo.GetComponent<TextMeshPro>();
            if (tmp == null) tmp = promptGo.AddComponent<TextMeshPro>();
            tmp.text = "";
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.4f, 0.95f, 1f, 1f);
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 1f;
            tmp.fontSizeMax = 400f;
            tmp.rectTransform.sizeDelta = new Vector2(3.2f, 0.18f);

            var cc = root.GetComponent<CalibrationController>();
            if (cc == null) cc = root.gameObject.AddComponent<CalibrationController>();
            var so = new SerializedObject(cc);
            so.FindProperty("_prompt").objectReferenceValue = tmp;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Debug.Log("[ChordPlayer] Phase 6 built: calibration + prompts. " +
                      "With Kinect: stand in front, hold a T-pose ~3s to calibrate (press C to redo).");
        }

        static GameObject FindChild(Transform parent, string name)
        {
            foreach (Transform t in parent)
                if (t.name == name) return t.gameObject;
            return null;
        }
    }
}
