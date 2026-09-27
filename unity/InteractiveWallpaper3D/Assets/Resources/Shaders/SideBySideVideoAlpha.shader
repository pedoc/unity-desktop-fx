Shader "InteractiveWallpaper/SideBySideVideoAlpha"
{
    Properties
    {
        _MainTex ("RGB + Alpha Video", 2D) = "black" {}
        _Opacity ("Opacity", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float _Opacity;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 colorUv = float2(input.uv.x * 0.5, input.uv.y);
                float2 alphaUv = float2(0.5 + input.uv.x * 0.5, input.uv.y);
                half3 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, colorUv).rgb;
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, alphaUv).r;
                alpha = saturate((alpha - 0.075) / 0.89) * _Opacity;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
