using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ChordPlayer.Kinect;
using ChordPlayer.Visuals;

namespace ChordPlayer.EditorTools
{
    /// <summary>
    /// Adds a SkeletonView (glowing Kinect skeleton) to the rig, reusing the
    /// additive-glow trail material. Menu: Tools ▸ Chord Player ▸ Add Skeleton View.
    /// </summary>
    static class SkeletonSetup
    {
        const string LineMatPath = "Assets/ChordPlayer/SkeletonLine.mat";

        [MenuItem("Tools/Chord Player/Add Skeleton View")]
        static void Add()
        {
            var sim = Object.FindAnyObjectByType<SimulatedInputProvider>(FindObjectsInactive.Include);
            var kin = Object.FindAnyObjectByType<MicrosoftKinectInputProvider>(FindObjectsInactive.Include);
            Transform root = sim != null ? sim.transform.root : (kin != null ? kin.transform.root : null);
            if (root == null)
            {
                EditorUtility.DisplayDialog("Run earlier phases first",
                    "No input provider found. Run Setup Phase 1-4 first.", "OK");
                return;
            }

            var shader = Shader.Find("ChordPlayer/Line");
            if (shader == null)
            {
                EditorUtility.DisplayDialog("Shader missing",
                    "Couldn't find ChordPlayer/Line. Make sure URP is installed and the project compiled.", "OK");
                return;
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(LineMatPath);
            if (mat == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/ChordPlayer")) AssetDatabase.CreateFolder("Assets", "ChordPlayer");
                mat = new Material(shader) { name = "SkeletonLine" };
                AssetDatabase.CreateAsset(mat, LineMatPath);
                AssetDatabase.SaveAssets();
            }
            mat.shader = shader;
            mat.SetColor("_Color", new Color(0.4f, 0.95f, 1f, 1f));

            var go = FindChild(root, "SkeletonView");
            if (go == null) { go = new GameObject("SkeletonView"); go.transform.SetParent(root, false); }

            var sv = go.GetComponent<SkeletonView>();
            if (sv == null) sv = go.AddComponent<SkeletonView>();

            var so = new SerializedObject(sv);
            so.FindProperty("_lineMaterial").objectReferenceValue = mat;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Debug.Log("[ChordPlayer] Skeleton view added. It only shows in Kinect mode; press Play and stand in front.");
        }

        static GameObject FindChild(Transform parent, string name)
        {
            foreach (Transform t in parent)
                if (t.name == name) return t.gameObject;
            return null;
        }
    }
}
