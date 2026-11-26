// カウンターボタンを押したときの発光演出用 UI シェーダー。
// ボタン画像より一回り大きい板に描き、ボタン画像の形に沿った外側の光・広がる輪郭・内側の閃光・斜めの光の帯を加算合成で重ねる。
// _Progress（0→1）を C# から進めてアニメーションさせる。
Shader "Taurus/UI/PressGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture (unused)", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _SourceTex ("Button Texture", 2D) = "black" {}
        // ボタンのスプライトが _SourceTex 上で占める UV 範囲（xMin, yMin, 幅, 高さ）
        _SourceUVRect ("Button UV Rect", Vector) = (0,0,1,1)
        // 板の外周のうちボタン画像の外側に取る余白（板サイズに対する比率 x, y）
        _Padding ("Padding", Vector) = (0.2,0.2,0,0)

        [HDR] _GlowColor ("Glow Color", Color) = (0.3,0.85,1,1)
        _Progress ("Progress", Range(0,1)) = 1
        _GlowWidth ("Glow Width (UV)", Range(0,0.2)) = 0.06
        _RingWidth ("Ring Max Spread (UV)", Range(0,0.3)) = 0.14
        _FlashStrength ("Inner Flash", Range(0,2)) = 0.9
        _ShineStrength ("Shine Band", Range(0,2)) = 1.2

        // UI マスク用（uGUI が使う標準プロパティ）
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One One
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            sampler2D _SourceTex;
            float4 _SourceUVRect;
            float4 _Padding;
            fixed4 _Color;
            float4 _GlowColor;
            float _Progress;
            float _GlowWidth;
            float _RingWidth;
            float _FlashStrength;
            float _ShineStrength;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            // 板の UV（0..1）をボタン画像内の正規化座標へ変換し、その位置のアルファを返す。画像の外は 0。
            float SampleAlpha(float2 local)
            {
                float2 inside = step(0.0, local) * step(local, 1.0);
                float2 uv = _SourceUVRect.xy + local * _SourceUVRect.zw;
                return tex2Dlod(_SourceTex, float4(uv, 0, 0)).a * inside.x * inside.y;
            }

            // 半径 radius の円周 12 方向で最大のアルファ（＝その距離まで膨らませた形）
            float Dilate(float2 local, float radius, float2 aspect)
            {
                float result = 0;
                [unroll]
                for (int i = 0; i < 12; i++)
                {
                    float angle = i * (UNITY_TWO_PI / 12.0);
                    float2 offset = float2(cos(angle), sin(angle)) * radius * aspect;
                    result = max(result, SampleAlpha(local + offset));
                }
                return result;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 pad = _Padding.xy;
                float2 local = (i.uv - pad) / (1.0 - 2.0 * pad);

                // 横長のボタンでも光の幅が縦横で揃うよう、短辺基準に補正する
                float2 sizeRatio = (1.0 - 2.0 * pad);
                float2 aspect = float2(sizeRatio.y / max(sizeRatio.x, 1e-4), 1.0);
                aspect /= max(aspect.x, aspect.y);

                float p = saturate(_Progress);
                float fade = 1.0 - p;
                float fadeSq = fade * fade;

                float body = SampleAlpha(local);

                // 外側の光：内側から2段階に膨らませてやわらかい縁を作る
                float near = Dilate(local, _GlowWidth * 0.5, aspect);
                float far = Dilate(local, _GlowWidth, aspect);
                float glow = saturate((near * 0.65 + far * 0.35) - body) * fadeSq * 1.6;

                // 広がる輪郭：押した瞬間から外へ広がりながら消える細い線
                float ringRadius = lerp(_GlowWidth * 0.3, _RingWidth, sqrt(p));
                float ringOuter = Dilate(local, ringRadius, aspect);
                float ringInner = Dilate(local, ringRadius * 0.82, aspect);
                float ring = saturate(ringOuter - ringInner) * fade * 1.4;

                // 内側の閃光：押した直後に強く光ってすぐ引く
                float flash = body * pow(fade, 3.0) * _FlashStrength;

                // 斜めの光の帯：左下から右上へ一度だけ走る
                float bandPos = lerp(-0.4, 1.4, saturate(p * 1.6));
                float diag = (local.x + local.y) * 0.5;
                float band = saturate(1.0 - abs(diag - bandPos) / 0.12);
                float shine = body * band * band * _ShineStrength * fade;

                float3 color = _GlowColor.rgb * (glow + ring + flash * 0.6)
                             + lerp(_GlowColor.rgb, 1.0.xxx, 0.6) * (shine + flash * 0.4);
                return fixed4(color * i.color.rgb * i.color.a, 1);
            }
            ENDCG
        }
    }
}
