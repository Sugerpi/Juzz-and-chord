using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ChordPlayer.Kinect;
using ChordPlayer.Music;

namespace ChordPlayer.EditorTools
{
    /// <summary>
    /// One-click Phase 2 setup: creates a default ChordTable asset, adds an
    /// AudioConductor and a ChordEngine to the scene, and wires them together.
    /// Run "Setup Phase 1 Scene" first so an input provider + hand spheres exist.
    /// Menu: Tools ▸ Chord Player ▸ Setup Phase 2 (audio + engine).
    /// </summary>
    static class Phase2SceneSetup
    {
        const string AssetDir = "Assets/ChordPlayer";
        const string TablePath = AssetDir + "/ChordTable.asset";

        [MenuItem("Tools/Chord Player/Setup Phase 2 (audio + engine)")]
        static void Setup()
        {
            ChordTable table = LoadOrCreateTable();

            // Parent everything under the existing input provider's object if present.
            var provider = Object.FindAnyObjectByType<SimulatedInputProvider>();
            Transform parent = provider != null ? provider.transform.root : null;
            if (provider == null)
            {
                EditorUtility.DisplayDialog("Run Phase 1 first",
                    "No SimulatedInputProvider found. Run Tools ▸ Chord Player ▸ Setup Phase 1 Scene " +
                    "first, then run this again. (Adding a provider-only object now so audio can still be tested.)",
                    "OK");
                var systems = new GameObject("Systems");
                Undo.RegisterCreatedObjectUndo(systems, "Setup Phase 2");
                systems.AddComponent<SimulatedInputProvider>();
                parent = systems.transform;
            }

            var audio = GetOrAddChild<AudioConductor>(parent, "AudioConductor");
            var engine = GetOrAddChild<ChordEngine>(parent, "ChordEngine");

            // Wire the engine's private serialized references.
            var so = new SerializedObject(engine);
            so.FindProperty("_table").objectReferenceValue = table;
            so.FindProperty("_audio").objectReferenceValue = audio;
            so.ApplyModifiedPropertiesWithoutUndo();

            EnsureAudioListener();

            Selection.activeObject = engine;
            EditorSceneManager.MarkSceneDirty(engine.gameObject.scene);
            Debug.Log("[ChordPlayer] Phase 2 ready. Press Play, then move the mouse to select a chord " +
                      "and hold a mouse button to sound it. Or open Tools ▸ Chord Player ▸ Chord Tester.");
        }

        static ChordTable LoadOrCreateTable()
        {
            if (!AssetDatabase.IsValidFolder(AssetDir))
                AssetDatabase.CreateFolder("Assets", "ChordPlayer");

            var table = AssetDatabase.LoadAssetAtPath<ChordTable>(TablePath);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<ChordTable>(); // field initializers fill defaults
                AssetDatabase.CreateAsset(table, TablePath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[ChordPlayer] Created default ChordTable at {TablePath}");
            }
            return table;
        }

        static T GetOrAddChild<T>(Transform parent, string name) where T : Component
        {
            // Reuse an existing one anywhere in the scene before making a new object.
            var existing = Object.FindAnyObjectByType<T>();
            if (existing != null) return existing;

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Setup Phase 2");
            if (parent != null) go.transform.SetParent(parent, false);
            return go.AddComponent<T>();
        }

        static void EnsureAudioListener()
        {
            if (Object.FindAnyObjectByType<AudioListener>() != null) return;
            var cam = Camera.main;
            if (cam != null) cam.gameObject.AddComponent<AudioListener>();
        }
    }
}
