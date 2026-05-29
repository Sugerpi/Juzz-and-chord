using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ChordPlayer.Kinect;
using ChordPlayer.Music;
using ChordPlayer.Visuals;
using ChordPlayer.UI;

namespace ChordPlayer.EditorTools
{
    /// <summary>
    /// Phase 4b (needs TMP Essentials imported): turns on wheel sector labels,
    /// adds the world-space center chord readout, and builds a screen-space HUD
    /// (status bar + connection dot). Menu: Tools ▸ Chord Player ▸ Setup Phase 4b (text + HUD).
    /// </summary>
    static class Phase4bSceneSetup
    {
        [MenuItem("Tools/Chord Player/Setup Phase 4b (text + HUD)")]
        static void Setup()
        {
            if (TMP_Settings.defaultFontAsset == null)
            {
                EditorUtility.DisplayDialog("Import TMP Essentials first",
                    "TextMeshPro isn't initialised. Do Window ▸ TextMeshPro ▸ Import TMP Essential Resources, " +
                    "then run this again.", "OK");
                return;
            }

            var provider = Object.FindAnyObjectByType<SimulatedInputProvider>();
            if (provider == null)
            {
                EditorUtility.DisplayDialog("Run earlier phases first",
                    "No SimulatedInputProvider found. Run Setup Phase 1–4a first.", "OK");
                return;
            }
            Transform root = provider.transform.root;

            EnableWheelLabels();
            BuildCenterDisplay(root);
            BuildHud();

            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Debug.Log("[ChordPlayer] Phase 4b built: sector labels, center chord text, HUD. " +
                      "Press Play — the wheels now read A–G / maj–dim and the center shows the chord.");
        }

        static void EnableWheelLabels()
        {
            foreach (var wheel in Object.FindObjectsByType<ChordWheel>(FindObjectsSortMode.None))
            {
                var so = new SerializedObject(wheel);
                so.FindProperty("_showLabels").boolValue = true;
                so.FindProperty("_labelSize").floatValue = 0.13f;   // world-metre box height
                so.ApplyModifiedPropertiesWithoutUndo();
                wheel.Rebuild();
                EditorUtility.SetDirty(wheel);
            }
        }

        static void BuildCenterDisplay(Transform root)
        {
            var go = FindChild(root, "CenterDisplay");
            if (go == null) { go = new GameObject("CenterDisplay"); go.transform.SetParent(root, false); }
            go.transform.localPosition = new Vector3(0f, 0.45f, 0f); // below the wheels, clear space
            go.transform.localRotation = Quaternion.identity; // readable by the +Z-facing camera

            var main = MakeWorldText(go.transform, "Main", new Vector3(0f, 0.12f, 0f), 0.34f, FontStyles.Bold);
            var sub = MakeWorldText(go.transform, "Sub", new Vector3(0f, -0.16f, 0f), 0.11f, FontStyles.Normal);
            sub.color = new Color(0.7f, 0.75f, 0.85f, 1f);

            var disp = EnsureComponent<CenterDisplay>(go);
            var so = new SerializedObject(disp);
            so.FindProperty("_main").objectReferenceValue = main;
            so.FindProperty("_sub").objectReferenceValue = sub;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static TextMeshPro MakeWorldText(Transform parent, string name, Vector3 localPos, float heightMetres, FontStyles style)
        {
            var go = FindChild(parent, name);
            if (go == null) { go = new GameObject(name); go.transform.SetParent(parent, false); }
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;

            var tmp = EnsureComponent<TextMeshPro>(go);
            tmp.text = name == "Main" ? "-" : "root - quality";
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 1f;
            tmp.fontSizeMax = 800f;
            tmp.rectTransform.sizeDelta = new Vector2(heightMetres * 5f, heightMetres);
            return tmp;
        }

        static void BuildHud()
        {
            var canvasGo = GameObject.Find("HUD Canvas");
            if (canvasGo == null)
            {
                canvasGo = new GameObject("HUD Canvas");
                Undo.RegisterCreatedObjectUndo(canvasGo, "Setup Phase 4b");
            }

            var canvas = EnsureComponent<Canvas>(canvasGo);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = EnsureComponent<CanvasScaler>(canvasGo);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            EnsureComponent<GraphicRaycaster>(canvasGo);

            // status bar (bottom)
            var barGo = FindChild(canvasGo.transform, "StatusBar") ?? NewUI("StatusBar", canvasGo.transform);
            var barText = EnsureComponent<TextMeshProUGUI>(barGo);
            var brt = barText.rectTransform;
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.sizeDelta = new Vector2(0f, 48f);
            brt.anchoredPosition = new Vector2(0f, 18f);
            barText.fontSize = 22;
            barText.alignment = TextAlignmentOptions.Center;
            barText.color = new Color(0.85f, 0.88f, 0.95f, 0.9f);
            barText.text = "STATE  —      CHORD  —      BODY  —";
            var statusBar = EnsureComponent<StatusBarUI>(barGo);
            var sbSo = new SerializedObject(statusBar);
            sbSo.FindProperty("_text").objectReferenceValue = barText;
            sbSo.ApplyModifiedPropertiesWithoutUndo();

            // connection dot (top-right)
            var dotGo = FindChild(canvasGo.transform, "ConnectionDot") ?? NewUI("ConnectionDot", canvasGo.transform);
            var dot = EnsureComponent<Image>(dotGo);
            dot.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            dot.color = new Color(0.4f, 0.4f, 0.45f, 0.5f);
            var drt = dot.rectTransform;
            drt.anchorMin = new Vector2(1f, 1f);
            drt.anchorMax = new Vector2(1f, 1f);
            drt.pivot = new Vector2(1f, 1f);
            drt.sizeDelta = new Vector2(18f, 18f);
            drt.anchoredPosition = new Vector2(-28f, -28f);
            var conn = EnsureComponent<ConnectionIndicator>(dotGo);
            var cSo = new SerializedObject(conn);
            cSo.FindProperty("_dot").objectReferenceValue = dot;
            cSo.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- helpers ----
        static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static GameObject FindChild(Transform parent, string name)
        {
            foreach (Transform t in parent)
                if (t.name == name) return t.gameObject;
            return null;
        }

        // Unity-safe get-or-add (avoids the GetComponent ?? AddComponent fake-null bug).
        static T EnsureComponent<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c == null ? go.AddComponent<T>() : c;
        }
    }
}
