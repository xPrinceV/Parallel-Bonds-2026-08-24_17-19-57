Shader "ParallelBonds/WorldFlip"
{
    Properties
    {
        [PerRendererData] _MainTex ("World Capture", 2D) = "white" {}
        _BlurPixels ("Blur Pixels", Range(0, 8)) = 0
        _HorizontalScale ("Horizontal Scale", Range(0.01, 1)) = 1
        _ShakeUV ("Shake UV", Vector) = (0, 0, 0, 0)
        _BackgroundBlurPixels ("Background Blur Pixels", Range(0, 8)) = 4
        _BackgroundBrightness ("Background Brightness", Range(0.1, 1)) = 0.65
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "False"
        }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            Name "WorldFlip"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _BlurPixels;
            float _HorizontalScale;
            float4 _ShakeUV;
            float _BackgroundBlurPixels;
            float _BackgroundBrightness;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            half3 SampleWorld(float2 uv)
            {
                float2 halfTexel = abs(_MainTex_TexelSize.xy) * 0.5;
                // clamp every tap, not only the center, including UV shake at the borders
                return tex2D(_MainTex, clamp(uv, halfTexel, 1.0 - halfTexel)).rgb;
            }

            half3 BlurWorld(float2 uv, float radiusPixels)
            {
                float2 stepUV = abs(_MainTex_TexelSize.xy) * clamp(radiusPixels, 0.0, 8.0);
                // fixed nine-tap tent kernel; zero radius reproduces the original sample
                half3 color = SampleWorld(uv) * 4.0;
                color += (SampleWorld(uv + float2(stepUV.x, 0.0))
                    + SampleWorld(uv - float2(stepUV.x, 0.0))
                    + SampleWorld(uv + float2(0.0, stepUV.y))
                    + SampleWorld(uv - float2(0.0, stepUV.y))) * 2.0;
                color += SampleWorld(uv + stepUV) + SampleWorld(uv - stepUV)
                    + SampleWorld(uv + float2(stepUV.x, -stepUV.y))
                    + SampleWorld(uv + float2(-stepUV.x, stepUV.y));
                return color * (1.0 / 16.0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float scale = clamp(_HorizontalScale, 0.01, 1.0);
                float2 sceneUV = input.uv + _ShakeUV.xy;
                float halfWidth = scale * 0.5;
                float distanceFromCenter = abs(input.uv.x - 0.5);
                // fade the inward feather out at full width, including the outer half-pixel
                float feather = min(max(fwidth(input.uv.x), 0.000001), halfWidth);
                float cardMask = smoothstep(0.0, feather, halfWidth - distanceFromCenter);
                cardMask = lerp(1.0, cardMask, saturate((1.0 - scale) / feather));
                float2 cardUV = float2(0.5 + clamp((input.uv.x - 0.5) / scale, -0.5, 0.5), input.uv.y);
                cardUV += _ShakeUV.xy;
                // offsets are never divided by card width; sampling stays bounded edge-on
                half3 card = BlurWorld(cardUV, _BlurPixels);
                half3 background = BlurWorld(sceneUV, max(_BackgroundBlurPixels, _BlurPixels));
                background *= clamp(_BackgroundBrightness, 0.1, 1.0);
                // opaque output replaces the redirected camera, even if its clear alpha is zero
                return half4(lerp(background, card, cardMask), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
