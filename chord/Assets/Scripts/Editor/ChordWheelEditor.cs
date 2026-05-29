using UnityEditor;
using UnityEngine;
using ChordPlayer.Visuals;

namespace ChordPlayer.EditorTools
{
    /// <summary>
    /// Custom inspector for ChordWheel: rebuild button + live selection sliders
    /// so you can verify the segment glow / Selection look without entering Play.
    /// </summary>
    [CustomEditor(typeof(ChordWheel))]
    public class ChordWheelEditor : Editor
    {
        int _highlight;
        float _ramp;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var wheel = (ChordWheel)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Phase 3 test tools", EditorStyles.boldLabel);

            if (GUILayout.Button("Rebuild mesh"))
            {
                wheel.Rebuild();
                EditorUtility.SetDirty(wheel);
            }

            int count = Mathf.Max(1, wheel.SegmentCount);

            EditorGUI.BeginChangeCheck();
            _highlight = EditorGUILayout.IntSlider("Highlight segment", Mathf.Clamp(_highlight, 0, count - 1), 0, count - 1);
            if (EditorGUI.EndChangeCheck()) wheel.PreviewSelection(_highlight);

            EditorGUI.BeginChangeCheck();
            _ramp = EditorGUILayout.Slider("Selection ramp (all)", _ramp, 0f, 1f);
            if (EditorGUI.EndChangeCheck()) wheel.PreviewAll(_ramp);

            EditorGUILayout.HelpBox(
                "Highlight segment = preview which sector lights up.\n" +
                "Selection ramp = scrub every segment 0→1 to tune the shader glow.",
                MessageType.None);
        }
    }
}
