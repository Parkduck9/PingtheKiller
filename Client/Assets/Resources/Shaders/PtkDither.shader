// Screen-door dither for the own Survivor when the camera boom is shorter than 1 m (HUD_SPEC 4.10): an 8x8 ordered
// (Bayer) pattern discards _Discard of the pixels (0.4 = 40 %). Main light + ambient (SH) only: the weak fill light is
// left out, so a dithered character is slightly darker on its shaded side.
Shader "PingTheKiller/Dither"
{
    Properties
    {
        [MainColor] _BaseColor ("Color", Color) = (0.25, 0.72, 0.69, 1)
        [MainTexture] _BaseMap ("Base map", 2D) = "white" {}
        _Discard ("Discarded fraction", Range(0, 1)) = 0.4
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        Pass
        {
            Name "Dither"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Discard;
                float4 _BaseMap_ST;
            CBUFFER_END
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; float2 uv : TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                uint x = (uint)input.positionCS.x & 7u, y = (uint)input.positionCS.y & 7u;
                uint v = x ^ y;
                uint bayer = ((v & 1u) << 5) | ((x & 1u) << 4) | ((v & 2u) << 2) | ((x & 2u) << 1) | ((v & 4u) >> 1) | ((x & 4u) >> 2);
                clip(_BaseColor.a * (1 - _Discard) - (bayer + 0.5) / 64.0);
                half3 n = normalize(input.normalWS);
                Light main = GetMainLight();
                half3 light = SampleSH(n) + main.color * saturate(dot(n, main.direction));
                return half4(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb * light, 1);
            }
            ENDHLSL
        }
    }
}
