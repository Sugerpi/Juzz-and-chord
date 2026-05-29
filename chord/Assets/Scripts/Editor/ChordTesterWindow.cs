using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ChordPlayer.Core;
using ChordPlayer.Music;

namespace ChordPlayer.EditorTools
{
    /// <summary>
    /// Phase 2 verification tool. Simulate left/right angles with sliders (or
    /// pick root/quality directly), see the resolved chord and its exact MIDI
    /// notes / note names, and — in Play mode — press Play to hear it through
    /// the AudioConductor. Menu: Tools ▸ Chord Player ▸ Chord Tester.
    /// </summary>
    public class ChordTesterWindow : EditorWindow
    {
        static readonly string[] NoteNames =
            { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        ChordTable _table;
        float _leftAngle, _rightAngle;
        float _minAngle = -120f, _maxAngle = 120f;
        int _rootIndex, _qualityIndex;
        bool _useAngles = true;
        bool _showTable;
        readonly List<int> _notes = new();

        [MenuItem("Tools/Chord Player/Chord Tester")]
        static void Open() => GetWindow<ChordTesterWindow>("Chord Tester").minSize = new Vector2(320, 380);

        void OnGUI()
        {
            if (_table == null) _table = FindTable();

            _table = (ChordTable)EditorGUILayout.ObjectField("Chord Table", _table, typeof(ChordTable), false);
            if (_table == null)
            {
                EditorGUILayout.HelpBox(
                    "No ChordTable. Run Tools ▸ Chord Player ▸ Setup Phase 2, or create one via " +
                    "Assets ▸ Create ▸ Chord Player ▸ Chord Table.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            _useAngles = EditorGUILayout.ToggleLeft("Select by angle (sliders)", _useAngles);

            if (_useAngles)
            {
                EditorGUILayout.BeginHorizontal();
                _minAngle = EditorGUILayout.FloatField("Min°", _minAngle);
                _maxAngle = EditorGUILayout.FloatField("Max°", _maxAngle);
                EditorGUILayout.EndHorizontal();

                _leftAngle = EditorGUILayout.Slider("Left angle (root)", _leftAngle, _minAngle, _maxAngle);
                _rightAngle = EditorGUILayout.Slider("Right angle (quality)", _rightAngle, _minAngle, _maxAngle);

                _rootIndex = SectorOf(_leftAngle, _table.RootCount);
                _qualityIndex = SectorOf(_rightAngle, _table.QualityCount);
            }
            else
            {
                _rootIndex = EditorGUILayout.Popup("Root", _rootIndex, Names(_table.RootCount, _table.GetRootName));
                _qualityIndex = EditorGUILayout.Popup("Quality", _qualityIndex, Names(_table.QualityCount, _table.GetQualityName));
            }

            EditorGUILayout.Space();
            DrawChordReadout();
            EditorGUILayout.Space();
            DrawPlayButtons();
            EditorGUILayout.Space();
            DrawTableFoldout();

            Repaint();
        }

        void DrawChordReadout()
        {
            _table.BuildNotes(_rootIndex, _qualityIndex, _notes);

            var box = new GUIStyle(EditorStyles.helpBox) { fontSize = 12 };
            EditorGUILayout.BeginVertical(box);
            EditorGUILayout.LabelField("Chord",
                $"{_table.GetDisplayName(_rootIndex, _qualityIndex)}   " +
                $"(root {_table.GetRootName(_rootIndex)}, {_table.GetQualityName(_qualityIndex)})",
                EditorStyles.boldLabel);

            var sb = new System.Text.StringBuilder();
            foreach (int n in _notes) sb.Append($"{n} ({MidiName(n)})  ");
            EditorGUILayout.LabelField("Notes", sb.ToString());
            EditorGUILayout.EndVertical();
        }

        void DrawPlayButtons()
        {
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("▶ Play (NoteOn)", GUILayout.Height(30)))
                    Audio()?.PlayChord(_notes);
                if (GUILayout.Button("■ Stop", GUILayout.Height(30)))
                    Audio()?.StopAll();
                EditorGUILayout.EndHorizontal();
            }
            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("Enter Play mode to hear audio (the synth runs on the audio thread).",
                    MessageType.None);
        }

        void DrawTableFoldout()
        {
            _showTable = EditorGUILayout.Foldout(_showTable, "Chord table (intervals)");
            if (!_showTable) return;
            for (int q = 0; q < _table.QualityCount; q++)
            {
                int[] iv = _table.GetIntervals(q);
                EditorGUILayout.LabelField($"  {_table.GetQualityName(q)}", string.Join(", ", iv));
            }
        }

        // ---- helpers ----
        int SectorOf(float angle, int count)
        {
            if (count <= 0) return 0;
            float range = Mathf.Max(0.0001f, _maxAngle - _minAngle);
            float a = Mathf.Clamp(angle, _minAngle, _maxAngle - 0.0001f);
            return Mathf.Clamp((int)((a - _minAngle) / (range / count)), 0, count - 1);
        }

        static string MidiName(int midi) => $"{NoteNames[((midi % 12) + 12) % 12]}{midi / 12 - 1}";

        static string[] Names(int count, System.Func<int, string> getName)
        {
            var arr = new string[Mathf.Max(0, count)];
            for (int i = 0; i < arr.Length; i++) arr[i] = getName(i);
            return arr;
        }

        static ChordTable FindTable()
        {
            string[] guids = AssetDatabase.FindAssets("t:ChordTable");
            return guids.Length == 0 ? null
                : AssetDatabase.LoadAssetAtPath<ChordTable>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        static AudioConductor Audio()
        {
            if (Application.isPlaying && ServiceLocator.TryGet<AudioConductor>(out var a)) return a;
            return Object.FindAnyObjectByType<AudioConductor>();
        }
    }
}
