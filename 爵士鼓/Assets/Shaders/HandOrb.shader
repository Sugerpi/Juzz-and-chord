// Glowing hand orb: bright centre + fresnel rim, additive so Bloom catches it.
// _Color and _Proximity are driven per-orb via MaterialPropertyBlock.
Shader "ChordPlayer/HandOrb"
{
    Properties
    {
        [HDR] _Color    ("Color", Color)            = (0.66, 0.33, 0.97, 1)
        _CenterPower    ("Center Power", Range(0.5,8)) = 2
        _RimPower       ("Rim Power", Range(0.5,8))    = 2.5
        _Intensity      ("Intensity", Float)           = 2.0
        _Proximity      ("Proximity", Range(0,1))      = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha One        // additive glow
        ZWrite Off
        Cull Back

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _CenterPower;
                float  _RimPower;
                float  _Intensity;
                float  _Proximity;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings O;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                O.positionHCS = p.positionCS;
                O.positionWS  = p.positionWS;
                O.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                return O;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(_WorldSpaceCameraPos.xyz - IN.positionWS);
                float ndv = saturate(dot(N, V));

                float center = pow(ndv, _CenterPower);
                float rim    = pow(1.0 - ndv, _RimPower);
                float glow   = center * 0.7 + rim * 1.3;

                float prox = lerp(0.55, 1.4, saturate(_Proximity));
                float3 rgb = _Color.rgb * glow * _Intensity * prox;
                float alpha = saturate(glow * prox);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
