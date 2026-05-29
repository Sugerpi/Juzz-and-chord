// Background quad: vertical gradient + slow radial "breathing" pulse.
// (Vertex displacement from the spec is omitted — a Unity Quad has only 4
//  verts so it can't curve smoothly; the gradient + pulse carry the look.)
Shader "ChordPlayer/BackgroundGradient"
{
    Properties
    {
        [HDR] _TopColor    ("Top Color", Color)    = (0.04, 0.004, 0.094, 1)
        [HDR] _BottomColor ("Bottom Color", Color) = (0.102, 0.043, 0.18, 1)
        [HDR] _PulseColor  ("Pulse Color", Color)  = (0.20, 0.05, 0.35, 1)
        _PulsePeriod   ("Pulse Period (s)", Float) = 6
        _PulseStrength ("Pulse Strength", Range(0,1)) = 0.35
        _PulseRadius   ("Pulse Radius", Range(0.1,1.5)) = 0.75
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background" "RenderPipeline"="UniversalPipeline" }
        ZWrite On
        Cull Off

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float4 _TopColor;
                float4 _BottomColor;
                float4 _PulseColor;
                float  _PulsePeriod;
                float  _PulseStrength;
                float  _PulseRadius;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings O;
                O.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                O.uv = IN.uv;
                return O;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 grad = lerp(_BottomColor.rgb, _TopColor.rgb, saturate(IN.uv.y));

                float period = max(0.01, _PulsePeriod);
                float breath = sin(_Time.y * 6.2831853 / period) * 0.5 + 0.5;
                float d = distance(IN.uv, float2(0.5, 0.5));
                float halo = (1.0 - smoothstep(0.0, _PulseRadius, d)) * breath * _PulseStrength;

                float3 col = grad + _PulseColor.rgb * halo;
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
