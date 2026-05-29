using UnityEngine;

namespace ChordPlayer.Visuals
{
    /// <summary>
    /// Central style asset (spec: "顏色 / 動畫時間 / 字型大小全部抽到一個 VisualStyleSO").
    /// Colours, segment palette and animation timings live here so nothing is
    /// hard-coded in scripts. Create via Assets ▸ Create ▸ Chord Player ▸ Visual Style.
    ///
    /// Palette hues are LDR; the holographic glow/HDR comes from the shader's
    /// emission multiplier (× up to _EmissionHigh) so Bloom catches selected segments.
    /// </summary>
    [CreateAssetMenu(fileName = "VisualStyle", menuName = "Chord Player/Visual Style")]
    public class VisualStyleSO : ScriptableObject
    {
        [Header("Palette (HDR — for background / text / lights later)")]
        [ColorUsage(true, true)] public Color bgTop = new(0.040f, 0.004f, 0.094f, 1f); // #0a0118
        [ColorUsage(true, true)] public Color bgBottom = new(0.102f, 0.043f, 0.180f, 1f); // #1a0b2e
        [ColorUsage(true, true)] public Color primaryPurple = new(1.65f, 0.83f, 2.42f, 1f);
        [ColorUsage(true, true)] public Color primaryPink = new(1.85f, 0.56f, 1.20f, 1f);
        [ColorUsage(true, true)] public Color accentCyan = new(0.07f, 2.14f, 2.49f, 1f);
        [ColorUsage(true, true)] public Color accentGreen = new(0.16f, 1.81f, 1.27f, 1f);
        [ColorUsage(true, true)] public Color neutralText = new(1.46f, 1.47f, 1.48f, 1f);

        [Header("Wheel segment colours (LDR hues, swept across segments)")]
        [GradientUsage(false)] public Gradient wheelPalette = DefaultPalette();

        [Header("Animation (seconds)")]
        [Min(0f)] public float selectionAnim = 0.2f;
        [Min(0f)] public float hoverAnim = 0.12f;

        public Color GetSegmentColor(int index, int count) =>
            wheelPalette.Evaluate(count <= 1 ? 0f : (float)index / (count - 1));

        void Reset() => wheelPalette = DefaultPalette();

        static Gradient DefaultPalette()
        {
            var g = new Gradient { mode = GradientMode.Blend };
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.659f, 0.333f, 0.969f), 0.00f), // purple
                    new GradientColorKey(new Color(0.925f, 0.282f, 0.600f), 0.34f), // pink
                    new GradientColorKey(new Color(0.024f, 0.714f, 0.831f), 0.67f), // cyan
                    new GradientColorKey(new Color(0.063f, 0.725f, 0.506f), 1.00f), // green
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}
