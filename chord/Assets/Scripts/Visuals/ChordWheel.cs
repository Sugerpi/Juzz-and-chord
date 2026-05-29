using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;
using ChordPlayer.Core;
using ChordPlayer.Music;

namespace ChordPlayer.Visuals
{
    /// <summary>
    /// Procedurally generates a donut of N extruded segments (each its own
    /// GameObject + WheelSegment + shared material) and highlights the one the
    /// ChordEngine currently selects. Tweak params in the Inspector — it
    /// regenerates live via OnValidate.
    /// </summary>
    public class ChordWheel : MonoBehaviour
    {
        public enum WheelKind { Root, Quality }

        [Header("Role")]
        [SerializeField] WheelKind _kind = WheelKind.Root;

        [Header("Geometry (metres / degrees)")]
        [SerializeField, Min(0f)] float _innerRadius = 0.40f;
        [SerializeField, Min(0f)] float _outerRadius = 0.60f;
        [SerializeField, Min(0f)] float _thickness = 0.08f;
        [SerializeField, Min(1)] int _segmentCount = 7;
        [SerializeField, Min(0f)] float _gapAngle = 1.5f;
        [SerializeField] float _startAngle = 0f;          // 0 = first segment centred at top
        [SerializeField, Range(90f, 360f)] float _arcDegrees = 360f;
        [SerializeField, Range(1f, 30f)] float _degreesPerStep = 6f;

        [Header("Look")]
        [SerializeField] Material _segmentMaterial;
        [SerializeField] VisualStyleSO _style;

        [Header("Labels (TMP)")]
        [SerializeField] bool _showLabels = false;
        [SerializeField] TMP_FontAsset _labelFont;             // null = TMP default
        [SerializeField, Min(0.02f)] float _labelSize = 0.13f; // label box HEIGHT in world metres
        [SerializeField] Color _labelColor = new(0.97f, 0.98f, 1f, 1f);

        [Header("Wiring (optional; resolves via ServiceLocator if empty)")]
        [SerializeField] ChordEngine _engine;

        readonly List<WheelSegment> _segments = new();
        int _highlighted = -1;

        public int SegmentCount => _segmentCount;
        public WheelKind Kind => _kind;
        public int Highlighted => _highlighted;
        public float InnerRadius => Mathf.Min(_innerRadius, _outerRadius);
        public float OuterRadius => Mathf.Max(_innerRadius, _outerRadius);

        /// <summary>The angle (deg, 0 = top) at which segment <paramref name="s"/> is centred.</summary>
        public float SegmentCenterAngle(int s) => _startAngle + s * (_arcDegrees / Mathf.Max(1, _segmentCount));

        /// <summary>The segment whose wedge an angle (deg, 0 = top) falls in.</summary>
        public int SectorAtAngle(float angleDeg)
        {
            float per = _arcDegrees / Mathf.Max(1, _segmentCount);
            int s = Mathf.RoundToInt((angleDeg - _startAngle) / per);
            return ((s % _segmentCount) + _segmentCount) % _segmentCount;
        }

        public void SetSegmentCount(int count) => _segmentCount = Mathf.Max(1, count);

        // ---------------- lifecycle ----------------
        void Awake() => Rebuild();

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (_engine == null) ServiceLocator.TryGet(out _engine);
            if (_engine != null) _engine.SelectionChanged += OnSelectionChanged;
            if (_engine != null) ApplySelection(IndexFor(_engine.Selection));
        }

        void OnDisable()
        {
            if (_engine != null) _engine.SelectionChanged -= OnSelectionChanged;
        }

        void OnValidate()
        {
#if UNITY_EDITOR
            if (Application.isPlaying) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null) Rebuild();
            };
#endif
        }

        // ---------------- selection ----------------
        void OnSelectionChanged(Chord chord) => ApplySelection(IndexFor(chord));

        int IndexFor(Chord chord) => _kind == WheelKind.Root ? chord.RootIndex : chord.QualityIndex;

        /// <summary>Highlight one segment (animated), called at runtime.</summary>
        public void ApplySelection(int index)
        {
            _highlighted = index;
            float dur = _style != null ? _style.selectionAnim : 0.2f;
            for (int i = 0; i < _segments.Count; i++)
                _segments[i].AnimateSelection(i == index ? 1f : 0f, dur);
        }

        /// <summary>Highlight instantly (used by the Editor test slider).</summary>
        public void PreviewSelection(int index)
        {
            _highlighted = index;
            for (int i = 0; i < _segments.Count; i++)
                _segments[i].ApplySelection(i == index ? 1f : 0f);
        }

        /// <summary>Set every segment's selection to the same value (shader-ramp test).</summary>
        public void PreviewAll(float value)
        {
            for (int i = 0; i < _segments.Count; i++)
                _segments[i].ApplySelection(value);
        }

        // ---------------- generation ----------------
        public void Rebuild()
        {
            ClearSegments();

            float per = _arcDegrees / _segmentCount;
            for (int s = 0; s < _segmentCount; s++)
            {
                float a0 = _startAngle + s * per + _gapAngle * 0.5f - per * 0.5f;
                float a1 = _startAngle + s * per - _gapAngle * 0.5f + per * 0.5f;

                var go = new GameObject($"Segment {s}");
                go.transform.SetParent(transform, false);

                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = BuildSector(a0, a1);

                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _segmentMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

                var seg = go.AddComponent<WheelSegment>();
                Color c = _style != null ? _style.GetSegmentColor(s, _segmentCount) : Color.white;
                seg.Init(s, c);
                _segments.Add(seg);

                if (_showLabels) CreateLabel(go.transform, s, per);
            }
        }

        // A TMP label sitting on the segment's arc centre, facing the camera.
        void CreateLabel(Transform parent, int index, float per)
        {
            string text = LabelFor(index);
            if (string.IsNullOrEmpty(text)) return;

            float rad = (_startAngle + index * per) * Mathf.Deg2Rad;
            float midR = (Mathf.Min(_innerRadius, _outerRadius) + Mathf.Max(_innerRadius, _outerRadius)) * 0.5f;

            var go = new GameObject($"Label {index}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(Mathf.Sin(rad) * midR, Mathf.Cos(rad) * midR, -_thickness * 0.5f - 0.012f);
            go.transform.localRotation = Quaternion.identity;   // readable by the +Z-facing camera

            var tmp = go.AddComponent<TextMeshPro>();
            if (_labelFont != null) tmp.font = _labelFont;
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = _labelColor;
            // Auto-fit into a real-world box, so size is reliable regardless of font units.
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 1f;
            tmp.fontSizeMax = 300f;
            tmp.rectTransform.sizeDelta = new Vector2(_labelSize * 2.4f, _labelSize);
        }

        string LabelFor(int index)
        {
            if (_engine == null || _engine.Table == null) return null;
            return _kind == WheelKind.Root
                ? _engine.Table.GetRootName(index)
                : _engine.Table.GetQualityName(index);
        }

        void ClearSegments()
        {
            _segments.Clear();
            var doomed = new List<GameObject>();
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.GetComponent<WheelSegment>() != null) doomed.Add(child.gameObject);
            }
            foreach (var go in doomed)
            {
                if (Application.isPlaying) Destroy(go);
                else DestroyImmediate(go);
            }
        }

        // Builds one extruded annular sector (front/back caps + inner/outer/end walls).
        Mesh BuildSector(float startDeg, float endDeg)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            float h = _thickness * 0.5f;
            float r0 = Mathf.Min(_innerRadius, _outerRadius);
            float r1 = Mathf.Max(_innerRadius, _outerRadius);
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(endDeg - startDeg) / _degreesPerStep));

            Vector3 P(float deg, float radius, float z)
            {
                float a = deg * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(a) * radius, Mathf.Cos(a) * radius, z);
            }

            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u0, float u1)
            {
                int b = verts.Count;
                verts.Add(p0); verts.Add(p1); verts.Add(p2); verts.Add(p3);
                uvs.Add(new Vector2(u0, 0)); uvs.Add(new Vector2(u0, 1));
                uvs.Add(new Vector2(u1, 1)); uvs.Add(new Vector2(u1, 0));
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }

            for (int i = 0; i < steps; i++)
            {
                float dA = Mathf.Lerp(startDeg, endDeg, (float)i / steps);
                float dB = Mathf.Lerp(startDeg, endDeg, (float)(i + 1) / steps);
                float uA = (float)i / steps;
                float uB = (float)(i + 1) / steps;

                // front cap (-h, toward camera)
                Quad(P(dA, r0, -h), P(dA, r1, -h), P(dB, r1, -h), P(dB, r0, -h), uA, uB);
                // back cap (+h)
                Quad(P(dA, r0, h), P(dB, r0, h), P(dB, r1, h), P(dA, r1, h), uA, uB);
                // outer wall
                Quad(P(dA, r1, -h), P(dA, r1, h), P(dB, r1, h), P(dB, r1, -h), uA, uB);
                // inner wall
                Quad(P(dA, r0, h), P(dA, r0, -h), P(dB, r0, -h), P(dB, r0, h), uA, uB);
            }

            // end caps (the gap-facing sides)
            Quad(P(startDeg, r0, -h), P(startDeg, r0, h), P(startDeg, r1, h), P(startDeg, r1, -h), 0, 0);
            Quad(P(endDeg, r1, -h), P(endDeg, r1, h), P(endDeg, r0, h), P(endDeg, r0, -h), 1, 1);

            var mesh = new Mesh { name = "WheelSector" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
