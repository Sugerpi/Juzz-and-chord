// Hand-written URP shader for ChordWheel segments — same effect set the spec
// asked of WheelSegment.shadergraph: Fresnel rim, scrolling hex, selection-driven
// emission/alpha. _Selection / _HoverIntensity are meant to be driven per-segment
// via MaterialPropertyBlock from WheelSegment.cs.
Shader "ChordPlayer/WheelSegment"
{
    Properties
    {
        [HDR] _BaseColor     ("Base Color", Color)      = (0.66, 0.33, 0.97, 1)
        [HDR] _EmissionColor ("Emission Color", Color)  = (0.66, 0.33, 0.97, 1)
        _Selection      ("Selection", Range(0,1))       = 0
        _HoverIntensity ("Hover Intensity", Range(0,1)) = 0
        _RimPower       ("Rim Power", Range(0.5,8))     = 3
        _EmissionLow    ("Emission @ Unselected", Float)= 0.12
        _EmissionHigh   ("Emission @ Selected", Float)  = 5.0
        _BaseAlpha      ("Alpha @ Unselected", Range(0,1)) = 0.16
        _SelectedAlpha  ("Alpha @ Selected", Range(0,1))   = 0.92
        _HexScale       ("Hex Scale", Float)            = 7
        _HexSpeed       ("Hex Scroll Speed", Float)     = 0.15
        _HexStrength    ("Hex Strength", Range(0,1))    = 0.18
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
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
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _EmissionColor;
                float  _Selection;
                float  _HoverIntensity;
                float  _RimPower;
                float  _EmissionLow;
                float  _EmissionHigh;
                float  _BaseAlpha;
                float  _SelectedAlpha;
                float  _HexScale;
                float  _HexSpeed;
                float  _HexStrength;
            CBUFFER_END

            float HexDist(float2 p)
            {
                p = abs(p);
                float c = dot(p, normalize(float2(1.0, 1.73)));
                return max(c, p.x);
            }

            // Thin hex-cell edges, returns ~1 on edges, ~0 inside cells.
            float HexGrid(float2 uv)
            {
                const float2 r = float2(1.0, 1.73);
                const float2 h = r * 0.5;
                float2 a = fmod(uv, r) - h;
                float2 b = fmod(uv - h, r) - h;
                float2 gv = dot(a, a) < dot(b, b) ? a : b;
                return smoothstep(0.42, 0.5, HexDist(gv));
            }

            Varyings vert(Attributes IN)
            {
                Varyings O;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                O.positionHCS = p.positionCS;
                O.positionWS  = p.positionWS;
                O.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                O.uv          = IN.uv;
                return O;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(_WorldSpaceCameraPos.xyz - IN.positionWS);
                // abs() keeps the rim robust to winding / double-sided faces.
                float fres = pow(1.0 - saturate(abs(dot(N, V))), _RimPower);

                float2 huv = IN.uv * _HexScale;
                huv.y += _Time.y * _HexSpeed;
                float hex = HexGrid(huv) * _HexStrength;

                float emInt = lerp(_EmissionLow, _EmissionHigh, _Selection);
                float3 emission = _EmissionColor.rgb * emInt;
                emission += _EmissionColor.rgb * fres * (_Selection * 2.0 + 0.3);
                emission += _EmissionColor.rgb * hex  * (_Selection + 0.25);
                emission += _EmissionColor.rgb * _HoverIntensity * 1.5;

                float3 rgb = _BaseColor.rgb * 0.08 + emission;
                float alpha = lerp(_BaseAlpha, _SelectedAlpha, _Selection);
                alpha = saturate(alpha + fres * 0.3 + hex * 0.2);

                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
