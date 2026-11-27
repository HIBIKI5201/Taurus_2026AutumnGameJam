// 振り子の玉の軌跡（TrailRenderer）用のネオン風シェーダー。
// トレイルの幅方向に細い光の線を数本（水色・ピンク・紫）並べ、周りにぼんやりした光を足して加算合成する。
// 線ごとに尾の長さをずらし、先端（玉側）ほど明るく、尾へ向かって消える。HDR の明るさで Bloom を効かせる前提。
// TrailRenderer の Texture Mode は Stretch（u = 0 が玉側、1 が尾）を想定する。頂点カラーの α でトレイル全体の濃さを調整できる。
Shader "Taurus/VFX/NeonTrail"
{
    Properties
    {
        [HDR] _ColorA ("Line Color A (外側)", Color) = (0.2, 0.95, 1.0, 1)
        [HDR] _ColorB ("Line Color B", Color) = (1.0, 0.25, 0.75, 1)
        [HDR] _ColorC ("Line Color C (内側)", Color) = (0.65, 0.3, 1.0, 1)
        _Intensity ("Intensity (HDR)", Range(0, 16)) = 4
        _LineCount ("Line Count", Range(1, 6)) = 4
        _LineWidth ("Line Width (幅に対する比率)", Range(0.005, 0.2)) = 0.022
        _GlowWidth ("Glow Width", Range(0.01, 0.5)) = 0.1
        _GlowStrength ("Glow Strength", Range(0, 2)) = 0.9
        _CoreWhiteness ("Core Whiteness (線の芯を白く)", Range(0, 1)) = 0.2
        _HeadSpread ("Head Spread (玉側での線の広がり)", Range(0, 1)) = 0.45
        _TailPower ("Tail Fade Power", Range(0.3, 4)) = 1.4
        _TailStagger ("Tail Stagger (線ごとの尾の長さのずれ)", Range(0, 0.5)) = 0.14
        _Wave ("Wave Amount", Range(0, 0.1)) = 0.025
        _WaveFrequency ("Wave Frequency", Range(0, 20)) = 7
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "NeonTrail"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorA;
                float4 _ColorB;
                float4 _ColorC;
                float _Intensity;
                float _LineCount;
                float _LineWidth;
                float _GlowWidth;
                float _GlowStrength;
                float _CoreWhiteness;
                float _HeadSpread;
                float _TailPower;
                float _TailStagger;
                float _Wave;
                float _WaveFrequency;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            // 線の番号 t（0〜1）に応じて A → B → C と色を移す
            float3 LineColor(float t)
            {
                return t < 0.5
                    ? lerp(_ColorA.rgb, _ColorB.rgb, t * 2.0)
                    : lerp(_ColorB.rgb, _ColorC.rgb, (t - 0.5) * 2.0);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float along = saturate(input.uv.x);   // 0 = 玉側、1 = 尾
                float across = input.uv.y;            // 0〜1 = 幅方向

                int count = (int)round(_LineCount);
                float3 result = 0;

                [loop]
                for (int i = 0; i < count; i++)
                {
                    float t = count > 1 ? i / (float)(count - 1) : 0.5;

                    // 玉側では線を中央に寄せ、尾に向かって扇状に広げる。尾ほどゆるく波打たせる
                    float spread = lerp(_HeadSpread, 1.0, along);
                    float center = 0.5 + (lerp(0.12, 0.88, t) - 0.5) * spread
                        + sin(along * _WaveFrequency + i * 1.7) * _Wave * along;
                    float distance = abs(across - center);

                    // 細い芯と、その周りのぼんやりした光
                    float core = smoothstep(_LineWidth, 0.0, distance);
                    float glow = exp(-(distance * distance) / (_GlowWidth * _GlowWidth)) * _GlowStrength;

                    // 線ごとに尾の長さをずらし、尾に向かって消す
                    float tailEnd = 1.0 - _TailStagger * frac(i * 0.618 + 0.3) * 2.0;
                    float fade = pow(saturate(1.0 - along / max(tailEnd, 0.05)), _TailPower);

                    float3 lineColor = LineColor(t);
                    float3 coreColor = lerp(lineColor, float3(1, 1, 1), _CoreWhiteness * core);
                    result += (coreColor * core + lineColor * glow) * fade;
                }

                // 頂点カラーの α でトレイル全体の濃さを調整できるようにする（加算合成なので色に掛ける）
                return half4(result * _Intensity * input.color.a, 1.0);
            }
            ENDHLSL
        }
    }
}
