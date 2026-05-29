// Minimal additive unlit glow that respects vertex colour — used for the hand
// orb TrailRenderer (which feeds its colour-over-time as vertex colours).
Shader "ChordPlayer/AdditiveGlow"
{
    Properties
    {
        [HDR] _Color ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color       : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings O;
                O.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                O.color = IN.color;
                return O;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 rgb = _Color.rgb * IN.color.rgb;
                return half4(rgb * IN.color.a, IN.color.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
