using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using ChordPlayer.Core;
using ChordPlayer.Kinect;
using ChordPlayer.Music;
using ChordPlayer.Visuals;

namespace ChordPlayer.EditorTools
{
    /// <summary>
    /// Phase 4a: background gradient quad, rim lights, glowing hand orbs (with
    /// trails) that replace the Phase 1 debug spheres, a camera rig and a
    /// GameDirector. Requires URP. Menu: Tools ▸ Chord Player ▸ Setup Phase 4a (environment + orbs).
    /// </summary>
    static class Phase4aSceneSetup
    {
        const string Dir = "Assets/ChordPlayer";
        const string StylePath = Dir + "/VisualStyle.asset";

        [MenuItem("Tools/Chord Player/Setup Phase 4a (environment + orbs)")]
        static void Setup()
        {
            var bgShader = Shader.Find("ChordPlayer/BackgroundGradient");
            var orbShader = Shader.Find("ChordPlayer/HandOrb");
            var trailShader = Shader.Find("ChordPlayer/AdditiveGlow");
            if (bgShader == null || orbShader == null || trailShader == null)
            {
                EditorUtility.DisplayDialog("Shaders not ready",
                    "Couldn't find the Phase 4 shaders. Make sure URP is installed and the project " +
                    "compiled, then run again.", "OK");
                return;
            }

            var provider = Object.FindAnyObjectByType<SimulatedInputProvider>();
            if (provider == null)
            {
                EditorUtility.DisplayDialog("Run Phase 1–3 first",
                    "No SimulatedInputProvider found. Run Setup Phase 1, 2 and 3 first.", "OK");
                return;
            }
            Transform root = provider.transform.root;

            EnsureFolder();
            var style = LoadOrCreate<VisualStyleSO>(StylePath);
            var bgMat = MakeMaterial(bgShader, "BackgroundGradient");
            var orbMat = MakeMaterial(orbShader, "HandOrb");
            var trailMat = MakeMaterial(trailShader, "HandTrail");

            BuildBackground(root, bgMat);
            BuildLights(root, style);
            BuildOrb(root, "LeftHandOrb", HandIndicator.Side.Left, new Color(0.66f, 0.33f, 0.97f), orbMat, trailMat, style);
            BuildOrb(root, "RightHandOrb", HandIndicator.Side.Right, new Color(0.02f, 0.71f, 0.83f), orbMat, trailMat, style);
            HidePhase1Spheres(root);
            AddCameraRig();
            EnsureComponent<GameDirector>(root.gameObject);

            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Debug.Log("[ChordPlayer] Phase 4a built: background, lights, hand orbs, camera rig, GameDirector. " +
                      "Press Play and move the mouse — orbs glow & trail, camera breathes.");
        }

        static void BuildBackground(Transform root, Material mat)
        {
            var go = RecreatePrimitive(root, "BackgroundQuad", PrimitiveType.Quad);
            go.transform.SetPositionAndRotation(new Vector3(0f, 1.4f, 6f), Quaternion.identity);
            go.transform.localScale = new Vector3(22f, 13f, 1f);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        static void BuildLights(Transform root, VisualStyleSO style)
        {
            var parent = FindOrCreate(root, "Lighting");
            MakePointLight(parent.transform, "Rim Light Left", new Vector3(-1.2f, 1.6f, -0.6f), new Color(0.66f, 0.33f, 0.97f));
            MakePointLight(parent.transform, "Rim Light Right", new Vector3(1.2f, 1.6f, -0.6f), new Color(0.02f, 0.71f, 0.83f));
        }

        static void MakePointLight(Transform parent, string name, Vector3 pos, Color color)
        {
            var go = FindChild(parent, name) ?? new GameObject(name);
            if (go.transform.parent != parent) go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var light = EnsureComponent<Light>(go);
            light.type = LightType.Point;
            light.color = color;
            light.intensity = 10f;
            light.range = 6f;
            light.shadows = LightShadows.None;
        }

        static void BuildOrb(Transform root, string name, HandIndicator.Side side, Color trailColor,
            Material orbMat, Material trailMat, VisualStyleSO style)
        {
            var go = RecreatePrimitive(root, name, PrimitiveType.Sphere);
            go.transform.localScale = Vector3.one * 0.06f;

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = orbMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.3f;
            trail.startWidth = 0.03f;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.01f;
            trail.numCapVertices = 2;
            trail.numCornerVertices = 2;
            trail.sharedMaterial = trailMat;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(trailColor, 0f), new GradientColorKey(trailColor, 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = grad;

            var hi = EnsureComponent<HandIndicator>(go);
            var so = new SerializedObject(hi);
            so.FindProperty("_side").enumValueIndex = (int)side;
            so.FindProperty("_orbRenderer").objectReferenceValue = mr;
            so.FindProperty("_style").objectReferenceValue = style;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void HidePhase1Spheres(Transform root)
        {
            // Stop the Phase 1 visualizer first, or it re-activates the spheres each frame.
            var viz = root.GetComponent<HandDebugVisualizer>();
            if (viz != null) viz.enabled = false;

            foreach (var n in new[] { "LeftHandSphere", "RightHandSphere" })
            {
                var go = FindChild(root, n);
                if (go != null) go.SetActive(false);
            }
        }

        static void AddCameraRig()
        {
            var cam = Camera.main;
            if (cam == null) return;
            EnsureComponent<CameraRig>(cam.gameObject);
        }

        // ---------- helpers ----------
        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets", "ChordPlayer");
        }

        static Material MakeMaterial(Shader shader, string name)
        {
            string path = $"{Dir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
                AssetDatabase.SaveAssets();
            }
            else if (mat.shader != shader) mat.shader = shader;
            return mat;
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

        static GameObject RecreatePrimitive(Transform parent, string name, PrimitiveType type)
        {
            var existing = FindChild(parent, name);
            if (existing != null) Object.DestroyImmediate(existing);

            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            if (go.TryGetComponent<Collider>(out var col)) Object.DestroyImmediate(col);
            Undo.RegisterCreatedObjectUndo(go, "Setup Phase 4a");
            return go;
        }

        static GameObject FindOrCreate(Transform parent, string name)
        {
            var existing = FindChild(parent, name);
            if (existing != null) return existing;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, "Setup Phase 4a");
            return go;
        }

        static GameObject FindChild(Transform parent, string name)
        {
            foreach (Transform t in parent)
                if (t.name == name) return t.gameObject;
            return null;
        }

        static T EnsureComponent<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }
}
