Shader "Taurus/PendulumPseudoBloom"
{
    Properties
    {
        _CoreColor ("Core Color", Color) = (1, 1, 0.95, 1)
        _HaloColor ("Halo Color", Color) = (0.65, 1, 0.1, 1)
        _CoreIntensity ("Core Intensity", Float) = 1.0
        _InnerIntensity ("Inner Intensity", Float) = 0.8
        _MidIntensity ("Mid Intensity", Float) = 0.6
        _OuterIntensity ("Outer Intensity", Float) = 0.3
        _DiscRadius ("Disc Radius", Range(0.01, 0.5)) = 0.14
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend One One
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "PendulumPseudoBloom"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _HaloColor;
                half _CoreIntensity;
                half _InnerIntensity;
                half _MidIntensity;
                half _OuterIntensity;
                half _DiscRadius;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = IN.uv * 2.0 - 1.0;
                float d = length(p);
                float edgeMask = saturate(1.0 - smoothstep(0.82, 1.0, d));

                // 玉本体の白飛びコア＋中心ブルーム＋鮮やか中域ブルーム＋広域大気ハローを合成
                float disc = saturate(1.0 - smoothstep(_DiscRadius * 0.65, _DiscRadius * 1.45, d));
                float inner = exp(-d * d * 26.0);
                float mid = exp(-d * d * 5.2);
                float outer = pow(saturate(1.0 - d), 2.35);

                half3 rgb =
                    _CoreColor.rgb * (disc * _CoreIntensity + inner * _InnerIntensity) +
                    _HaloColor.rgb * (mid * _MidIntensity + outer * _OuterIntensity);

                rgb *= edgeMask;
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
