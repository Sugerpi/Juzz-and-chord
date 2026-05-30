// Dead-simple URP additive line shader for the skeleton bones. Outputs _Color
// regardless of vertex colour, so a LineRenderer can never come out invisible.
Shader "ChordPlayer/Line"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.4, 0.95, 1, 1)
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

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings O;
                O.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return O;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return half4(_Color.rgb * _Color.a, _Color.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
