using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ChordPlayer.Music;
using ChordPlayer.Visuals;

namespace ChordPlayer.EditorTools
{
    /// <summary>
    /// One-click Phase 3 setup: creates the VisualStyle asset + a material from
    /// the WheelSegment shader, builds a Left (root) and Right (quality) wheel,
    /// positions them, and wires them to the ChordEngine. Requires URP installed
    /// (so the shader compiles). Menu: Tools ▸ Chord Player ▸ Setup Phase 3 (wheels).
    /// </summary>
    static class Phase3SceneSetup
    {
        const string Dir = "Assets/ChordPlayer";
        const string StylePath = Dir + "/VisualStyle.asset";
        const string MatPath = Dir + "/WheelSegment.mat";

        [MenuItem("Tools/Chord Player/Setup Phase 3 (wheels)")]
        static void Setup()
        {
            var shader = Shader.Find("ChordPlayer/WheelSegment");
            if (shader == null)
            {
                EditorUtility.DisplayDialog("URP / shader not ready",
                    "Couldn't find shader 'ChordPlayer/WheelSegment'.\n\n" +
                    "Install URP (Window ▸ Package Manager ▸ Universal RP) and assign a URP " +
                    "pipeline asset in Project Settings ▸ Graphics, then run this again.",
                    "OK");
                return;
            }

            EnsureFolder();
            var style = LoadOrCreate<VisualStyleSO>(StylePath);
            var mat = LoadOrCreateMaterial(shader);

            var engine = Object.FindAnyObjectByType<ChordEngine>();
            if (engine == null)
            {
                EditorUtility.DisplayDialog("Run Phase 2 first",
                    "No ChordEngine in the scene. Run Tools ▸ Chord Player ▸ Setup Phase 2 first.", "OK");
                return;
            }

            int rootCount = engine.Table != null ? engine.Table.RootCount : 7;
            int qualityCount = engine.Table != null ? engine.Table.QualityCount : 8;
            Transform parent = engine.transform.root;

            BuildWheel("LeftWheel", parent, new Vector3(-0.70f, 1.40f, 0f),
                ChordWheel.WheelKind.Root, rootCount, mat, style, engine);
            BuildWheel("RightWheel", parent, new Vector3(0.70f, 1.40f, 0f),
                ChordWheel.WheelKind.Quality, qualityCount, mat, style, engine);

            EditorSceneManager.MarkSceneDirty(engine.gameObject.scene);
            Debug.Log("[ChordPlayer] Phase 3 wheels built. Select a wheel to use the test sliders, " +
                      "or press Play and move the mouse to light up sectors.");
        }

        static void BuildWheel(string name, Transform parent, Vector3 pos, ChordWheel.WheelKind kind,
            int segmentCount, Material mat, VisualStyleSO style, ChordEngine engine)
        {
            var existing = FindChildByName(parent, name);
            GameObject go = existing != null ? existing : new GameObject(name);
            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(go, "Setup Phase 3");
                if (parent != null) go.transform.SetParent(parent, false);
            }
            go.transform.localPosition = pos;

            var wheel = go.GetComponent<ChordWheel>();
            if (wheel == null) wheel = go.AddComponent<ChordWheel>();

            var so = new SerializedObject(wheel);
            so.FindProperty("_kind").enumValueIndex = (int)kind;
            so.FindProperty("_segmentCount").intValue = Mathf.Max(1, segmentCount);
            so.FindProperty("_segmentMaterial").objectReferenceValue = mat;
            so.FindProperty("_style").objectReferenceValue = style;
            so.FindProperty("_engine").objectReferenceValue = engine;
            so.ApplyModifiedPropertiesWithoutUndo();

            wheel.Rebuild();
            EditorUtility.SetDirty(wheel);
        }

        // ---- asset helpers ----
        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets", "ChordPlayer");
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssets();
            }
            return asset;
        }

        static Material LoadOrCreateMaterial(Shader shader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "WheelSegment" };
                AssetDatabase.CreateAsset(mat, MatPath);
                AssetDatabase.SaveAssets();
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            return mat;
        }

        static GameObject FindChildByName(Transform parent, string name)
        {
            if (parent == null) return null;
            foreach (Transform t in parent)
                if (t.name == name) return t.gameObject;
            return null;
        }
    }
}
